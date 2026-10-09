namespace ReleaseWatcher.ArchitectureTests;

/// <summary>
/// 构建脚本与版本控制约定的守卫断言。
/// </summary>
/// <remarks>
/// 这些约束曾被真实违反过，因此固化为测试：
/// - <c>.gitignore</c> 曾因缺失产物规则导致 bin/obj 被误提交；
/// - <c>build.cmd</c> 曾因含中文注释导致 cmd.exe 解析错乱、命令断裂。
///
/// 依据：<c>DECISIONS.md</c> D-038 / D-039、<c>RISKS.md</c> R-012 / R-013、
/// <c>AGENTS.md</c> 第 1.3 节。
/// </remarks>
public sealed class RepositoryConventionTests
{
    private static string SolutionRoot => SolutionLayout.SolutionRoot;

    [Fact]
    public void BuildScript_MustBeAsciiOnly()
    {
        // cmd.exe 按 OEM 代码页解析批处理文件（简体中文机器为 GBK/936）。
        // 非 ASCII 字符会导致行内容被错解，进而把 rem / if 等当成命令执行 ——
        // 2026-10-09 实际发生过：中文注释行被截断，引发大量"不是内部或外部命令"。
        var scriptPath = Path.Combine(SolutionRoot, "build.cmd");
        Assert.True(File.Exists(scriptPath), "未找到 build.cmd");

        var bytes = File.ReadAllBytes(scriptPath);
        var hasNonAscii = bytes.Any(b => b > 0x7F);

        Assert.False(
            hasNonAscii,
            "build.cmd 含非 ASCII 字符。必须改为纯 ASCII（注释用英文），"
            + "否则 cmd.exe 解析会错乱。");
    }

    [Fact]
    public void BuildScript_MustPinAbsoluteDotnetPath()
    {
        // 本机 PATH 会先解析到 Program Files (x86) 下缺少 sdk 目录的 dotnet.exe，
        // 直接调用报"No .NET SDKs were found"。脚本必须固定完整路径。
        var script = File.ReadAllText(Path.Combine(SolutionRoot, "build.cmd"));

        Assert.Contains(@"set ""DOTNET=", script, StringComparison.OrdinalIgnoreCase);

        // 固定路径必须是绝对路径，不能是裸 dotnet。
        var match = System.Text.RegularExpressions.Regex.Match(
            script, @"set\s+""DOTNET=([^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.True(match.Success, "build.cmd 中未找到 DOTNET 变量定义");

        var value = match.Groups[1].Value;
        Assert.Contains(@":\", value, StringComparison.Ordinal);
        Assert.False(
            value.Equals("dotnet", StringComparison.OrdinalIgnoreCase) || value == "dotnet.exe",
            "DOTNET 必须使用绝对路径，不能是裸 dotnet。");
    }

    [Fact]
    public void BuildScript_MustRefusePublishWithoutAuthorization()
    {
        // AGENTS.md 第 1.6 节：发布属发布授权范畴。
        // build.cmd publish 被有意实现为拒绝执行，防止误触发。
        var script = File.ReadAllText(Path.Combine(SolutionRoot, "build.cmd"));

        Assert.Contains("publish", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WARN", script, StringComparison.OrdinalIgnoreCase);

        // publish 分支必须以非零码退出。
        Assert.Contains("exit /b 1", script, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>build.cmd clean</c> 必须真正删除 <c>bin</c> 与 <c>obj</c>，而非只调用 <c>dotnet clean</c>。
    /// </summary>
    /// <remarks>
    /// AGENTS.md 1.6.1（D-048）：交给用户验收前必须 clean + build，
    /// 且产物须可证明来自当前源码。
    /// <para>
    /// <c>dotnet clean</c> 只清中间产物，**保留已构建的 EXE**。
    /// 仅执行 <c>dotnet clean</c> + <c>build</c> 时，
    /// 上一次残留的 EXE 可能被复用，或本次构建未重新生成输出，
    /// 于是用户启动到旧产物 —— 表现为"代码改了但界面没变"，极难归因。
    /// </para>
    /// <para>
    /// 本断言检查脚本里存在显式删除 bin/obj 的逻辑。
    /// 它不验证实际删除效果（那需要真实文件系统状态变化），
    /// 只保证规则没有在文本层面被悄悄删掉。
    /// </para>
    /// </remarks>
    [Fact]
    public void BuildScript_CleanMustRemoveBinAndObj()
    {
        var script = File.ReadAllText(Path.Combine(SolutionRoot, "build.cmd"));

        var mentionsBin = script.Contains("\"bin\"") || script.Contains("==\"bin\"");
        var mentionsObj = script.Contains("\"obj\"") || script.Contains("==\"obj\"");

        Assert.True(mentionsBin, "build.cmd clean 未提及删除 bin 目录。");
        Assert.True(mentionsObj, "build.cmd clean 未提及删除 obj 目录。");
        Assert.True(
            script.Contains("rmdir", StringComparison.OrdinalIgnoreCase),
            "build.cmd clean 应使用 rmdir /s /q 删除 bin 与 obj。");
    }

    [Theory]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("clean")]
    [InlineData("sdk")]
    public void BuildScript_MustNotRunDotnetInsideParentheticalBlock(string subcommand)    {
        // 曾因把子命令写成 if (...) ( ... exit /b %ERRORLEVEL% ) 而埋下严重缺陷：
        // cmd.exe 在**解析**括号块时就展开 %ERRORLEVEL%，此时 dotnet 尚未执行，
        // 拿到的是上一条命令的旧值 0 —— 于是无论构建或测试是否失败，
        // build.cmd 一律返回 0，把编译错误伪装成"测试全部通过"。
        // 架构守卫一旦被这样绕过就等于不存在。
        //
        // 因此子命令必须走 goto 标签，在块外执行 exit /b %ERRORLEVEL%。
        var script = File.ReadAllText(Path.Combine(SolutionRoot, "build.cmd"));

        // 匹配形如： if /i "%1"=="test" ( ... 的一行
        var blockStart = System.Text.RegularExpressions.Regex.Match(
            script,
            $@"if\s+/i\s+""%1""==""{subcommand}""\s*\(",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.False(
            blockStart.Success,
            $"build.cmd 的 {subcommand} 分支被放进了 if (...) 块。"
            + "%ERRORLEVEL% 会在解析期展开，导致失败被隐藏为成功。"
            + "请改用 goto 标签形式（见脚本内注释）。");
    }

    [Theory]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("clean")]
    [InlineData("sdk")]
    public void BuildScript_MustPropagateExitCodeForEachSubcommand(string subcommand)
    {
        // 与上一条配对：每个子命令都必须以 dotnet 的真实退出码收尾，
        // 否则 CI 与人工校验都无法察觉失败。
        var script = File.ReadAllText(Path.Combine(SolutionRoot, "build.cmd"));

        var label = $":do_{subcommand}";
        var labelIndex = script.IndexOf(label, StringComparison.OrdinalIgnoreCase);

        Assert.True(
            labelIndex >= 0,
            $"build.cmd 缺少 {label} 标签，无法传递退出码。");

        var afterLabel = script[labelIndex..];
        var nextLabel = afterLabel.IndexOf(":do_", 1, StringComparison.OrdinalIgnoreCase);

        var body = nextLabel > 0 ? afterLabel[..nextLabel] : afterLabel;

        Assert.Contains("exit /b %ERRORLEVEL%", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("bin/", "bin")]
    [InlineData("obj/", "obj")]
    public void GitIgnore_MustExcludeBuildOutput(string entry, string directoryName)
    {
        // 曾在缺少该规则时把 dll / exe / pdb 与 obj 生成文件全部暂存。
        var gitignore = File.ReadAllLines(Path.Combine(SolutionRoot, ".gitignore"));

        Assert.Contains(
            gitignore,
            line => line.Trim() == entry || line.Trim() == $"{directoryName}/");
    }

    [Theory]
    [InlineData("*.db")]
    [InlineData(".env")]
    public void GitIgnore_MustExcludeSensitiveOrRuntimeFiles(string pattern)
    {
        var gitignore = File.ReadAllLines(Path.Combine(SolutionRoot, ".gitignore"));
        Assert.Contains(gitignore, line => line.Trim() == pattern);
    }

    [Fact]
    public void GitIgnore_MustStillExcludeProjectDocuments()
    {
        // D-039：用户决定项目文档不纳入版本控制。保留现状是明确决定，
        // 但"保留"本身也需要被固定下来 —— 防止某次"顺手清理"悄悄改变它。
        var gitignore = File.ReadAllLines(Path.Combine(SolutionRoot, ".gitignore"))
            .Select(l => l.Trim())
            .ToArray();

        foreach (var document in new[]
                 {
                     "AGENTS.md", "DECISIONS.md", "MAP.md", "NOW.md",
                     "PROJECT_INDEX.md", "RISKS.md", "RUNBOOK.md", "docs/", "history/",
                 })
        {
            Assert.Contains(document, gitignore);
        }
    }

    [Fact]
    public void SolutionFile_MustUseSlnxFormat()
    {
        // DECISIONS.md D-001 / MAP.md 第 8 节：使用新式 slnx 解决方案格式。
        var solutionPath = Path.Combine(SolutionRoot, SolutionLayout.SolutionFileName);
        Assert.True(File.Exists(solutionPath), $"未找到 {SolutionLayout.SolutionFileName}");

        var solution = File.ReadAllText(solutionPath);
        Assert.Contains("<Solution", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("GlobalSection", solution, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalizationDirectory_MustExist()
    {
        // D-022：语言源文件在 localization/，发布时复制到 Languages/。
        Assert.True(
            Directory.Exists(Path.Combine(SolutionRoot, "localization")),
            "缺少 localization/ 目录（Generator 的输出目标）。");
    }
}