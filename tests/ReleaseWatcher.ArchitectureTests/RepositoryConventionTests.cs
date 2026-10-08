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