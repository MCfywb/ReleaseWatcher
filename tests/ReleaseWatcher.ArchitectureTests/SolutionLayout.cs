namespace ReleaseWatcher.ArchitectureTests;

/// <summary>
/// 定位解决方案根目录，供架构检查读取工程文件。
/// </summary>
/// <remarks>
/// <para>
/// 本测试项目**故意不 ProjectReference** 主解决方案的任何项目，原因有三：
/// </para>
///
/// <list type="number">
/// <item>架构检查的对象就是各项目的 <c>.csproj</c> 本身；
/// 若引用它们，检查逻辑会自我循环。</item>
/// <item>避免把 App（WPF，net10.0-windows）与全部依赖拖进测试进程 —— 测试项目
/// 应保持 <c>net10.0</c> 跨平台可用，未来可在 CI 的非 Windows 代理上执行。</item>
/// <item>与 D-019 确立的"物理独立、逻辑单向关联"思路一致：
/// 检查者不侵入被检查者。</item>
/// </list>
///
/// <para>
/// <b>这不适用于单元测试项目。</b>
/// <c>ReleaseWatcher.Infrastructure.Tests</c> 采取相反取舍 ——
/// 它必须引用被测项目才能拿到类型，否则无从测试。
/// 两个项目分工不同，约束也不同，切勿把「零引用」套用过去。
/// </para>
///
/// 依据：<c>DECISIONS.md</c> D-019 / D-037、<c>RISKS.md</c> R-007。
/// </remarks>
public static class SolutionLayout
{
    /// <summary>主解决方案文件相对文件名。</summary>
    public const string SolutionFileName = "ReleaseWatcher.slnx";

    /// <summary>四个主项目的目录名，顺序即分层顺序。</summary>
    public static readonly string[] LayerProjects =
    [
        "ReleaseWatcher.Domain",
        "ReleaseWatcher.Application",
        "ReleaseWatcher.Infrastructure",
        "ReleaseWatcher.App",
    ];

    /// <summary>
    /// 测试项目目录名。
    /// </summary>
    /// <remarks>
    /// 三个测试项目的引用策略<strong>刻意不同</strong>：
    /// <list type="bullet">
    /// <item><c>ArchitectureTests</c> 零引用 —— 它检查的是 .csproj 文本本身，
    /// 引用主项目会造成自我循环（见本文件类型注释）。</item>
    /// <item>单元测试项目（<c>Infrastructure.Tests</c> / <c>Domain.Tests</c>）
    /// 必须引用被测项目，否则拿不到真实类型。</item>
    /// </list>
    /// 因此这里只登记名字，不统一约束引用方式 ——
    /// 具体约束见 <c>LayeringTests</c> 中的对应断言。
    /// <para>
    /// <b>新增测试项目必须登记到此数组</b>：D-019 边界断言
    /// （<c>NoProject_MayReferenceLocalizationGenerator</c>）遍历此数组，
    /// 漏登记会让新项目<b>静默脱离</b>该边界保护。
    /// </para>
    /// </remarks>
    public static readonly string[] TestProjects =
    [
        "ReleaseWatcher.ArchitectureTests",
        "ReleaseWatcher.Application.Tests",
        "ReleaseWatcher.Domain.Tests",
        "ReleaseWatcher.Infrastructure.Tests",
    ];

    /// <summary>
    /// 由当前测试程序集位置向上查找 <c>ReleaseWatcher.slnx</c>，
    /// 返回解决方案根目录。
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">
    /// 向上若干级仍未找到解决方案文件 —— 通常意味着测试被从错误位置运行。
    /// </exception>
    public static string SolutionRoot { get; } = FindSolutionRoot();

    /// <summary>
    /// 某个项目的工程文件绝对路径。
    /// </summary>
    /// <param name="directoryName">
    /// 项目目录名，例如 <c>ReleaseWatcher.Domain</c>；
    /// 也可传入测试项目名 <c>ReleaseWatcher.ArchitectureTests</c>，此时自动在
    /// <c>src/</c> 与 <c>tests/</c> 下查找。
    /// </param>
    public static string ProjectFile(string directoryName)
    {
        // 多数项目在 src/，测试项目在 tests/ —— 允许调用方不必关心这个区别。
        var srcCandidate = Path.Combine(SolutionRoot, "src", directoryName, $"{directoryName}.csproj");
        if (File.Exists(srcCandidate))
        {
            return srcCandidate;
        }

        var testsCandidate = Path.Combine(SolutionRoot, "tests", directoryName, $"{directoryName}.csproj");
        if (File.Exists(testsCandidate))
        {
            return testsCandidate;
        }

        throw new FileNotFoundException(
            $"未找到项目 {directoryName} 的工程文件（已查找 src/ 与 tests/ 目录）。",
            srcCandidate);
    }

    /// <summary>读取某项目的工程文件内容。</summary>
    public static string ReadProjectFile(string directoryName)
        => File.ReadAllText(ProjectFile(directoryName));

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"未能从 {AppContext.BaseDirectory} 向上找到 {SolutionFileName}。");
    }
}