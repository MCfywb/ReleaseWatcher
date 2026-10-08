using System.Text.RegularExpressions;

namespace ReleaseWatcher.ArchitectureTests;

/// <summary>
/// 组合根约束的架构断言。
/// </summary>
/// <remarks>
/// D-037 规定：App 引用 Infrastructure **仅用于 DI 组合根**注册具体实现；
/// 业务代码（View / ViewModel / 用例）不得直接 <c>using ReleaseWatcher.Infrastructure.*</c>。
///
/// 编译期无法阻止这种误用（项目引用已建立），因此需要静态检查兜底。
/// 依据：<c>DECISIONS.md</c> D-037、<c>RISKS.md</c> R-007 残余风险。
/// </remarks>
public sealed class CompositionRootTests
{
    /// <summary>
    /// 唯一允许直接依赖 Infrastructure 的文件后缀白名单。
    /// DI 注册本质上是把实现类型暴露给容器，无法完全避免 using。
    /// </summary>
    private static readonly string[] CompositionRootFileSuffixes =
    [
        "AppServices.cs",
        "CompositionRoot.cs",
        "ServiceCollectionExtensions.cs",
    ];

    private static string SolutionRoot => SolutionLayout.SolutionRoot;

    /// <summary>业务代码目录（这些目录下的代码不得直接依赖 Infrastructure）。</summary>
    private static readonly string[] BusinessCodeSubdirectories =
    [
        Path.Combine("src", "ReleaseWatcher.App", "Views"),
        Path.Combine("src", "ReleaseWatcher.App", "ViewModels"),
        Path.Combine("src", "ReleaseWatcher.App", "Controls"),
        Path.Combine("src", "ReleaseWatcher.App", "Windows"),
        Path.Combine("src", "ReleaseWatcher.Application", "Abstractions"),
        Path.Combine("src", "ReleaseWatcher.Application", "Commands"),
        Path.Combine("src", "ReleaseWatcher.Application", "Queries"),
    ];

    private static bool IsCompositionRoot(string filePath)
        => CompositionRootFileSuffixes.Any(suffix => filePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void AppViewModels_MustNotUsingInfrastructure()
    {
        // ViewModel 是最容易图省事直接 new 具体实现的地方，也是最需要守住的地方。
        AssertNoInfrastructureDependency(Path.Combine("src", "ReleaseWatcher.App", "ViewModels"));
    }

    [Fact]
    public void AppViews_MustNotUsingInfrastructure()
    {
        // 注意：XAML 无法被本检查覆盖（XAML 里的命名空间声明是另一种机制）。
        // 本断言只覆盖 ViewModels 下的 .cs 文件。
        AssertNoInfrastructureDependency(Path.Combine("src", "ReleaseWatcher.App", "Views"));
    }

    [Fact]
    public void ApplicationLayer_MustNotUsingInfrastructure()
    {
        // Application 是定义抽象接口的层。若它反向依赖 Infrastructure，
        // 抽象就失去意义 —— 这比 App 层的违规更严重。
        AssertNoInfrastructureDependency(Path.Combine("src", "ReleaseWatcher.Application"));
    }

    [Fact]
    public void DomainLayer_MustNotReferenceInfrastructureAtAll()
    {
        // 领域层连项目引用都不该有（见 LayeringTests），此处额外防止源码文本中出现该命名空间。
        AssertNoInfrastructureDependency(Path.Combine("src", "ReleaseWatcher.Domain"));
    }

    /// <summary>
    /// 约束覆盖度自检：确认 D-037 的组合根文件已存在。
    /// </summary>
    /// <remarks>
    /// <b>当前为跳过状态（已实现 1 / 跳过 1）。</b>
    ///
    /// 这条断言本身不是检查"是否违规"，而是检查"守卫是否有效"：
    /// 若组合根文件始终不落地，D-037 就一直只靠文件后缀白名单兜底 ——
    /// 白名单可以随时被放宽，属于无强制状态。
    ///
    /// 一旦创建 <c>src/ReleaseWatcher.App/AppServices.cs</c>，
    /// 本测试自动变为通过，同时应把 <see cref="CompositionRootFileSuffixes"/> 收窄至实际文件。
    /// </remarks>
    [Fact(Skip = "尚未创建 AppServices.cs。创建组合根文件后本测试应转为通过。")]
    public void CompositionRootFile_ShouldExist()
    {
        var hasCompositionRoot = Directory
            .EnumerateFiles(SolutionRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsUnderBuildOutput(p))
            .Any(IsCompositionRoot);

        Assert.True(
            hasCompositionRoot,
            "未找到组合根文件。建议创建 src/ReleaseWatcher.App/AppServices.cs 集中注册，"
            + "并将 CompositionRootFileSuffixes 收窄为实际文件。");
    }

    private static void AssertNoInfrastructureDependency(string relativeDirectory)
    {
        var directory = Path.Combine(SolutionRoot, relativeDirectory);

        if (!Directory.Exists(directory))
        {
            // 目录尚未创建（如 ViewModels 仍为空）—— 不是失败，只是暂无内容可查。
            return;
        }

        var violations = Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsUnderBuildOutput(p))
            .Where(p => !IsCompositionRoot(p))
            .Where(ReferencesInfrastructureNamespace)
            .Select(p => Path.GetRelativePath(SolutionRoot, p))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "以下业务代码直接依赖了 Infrastructure，违反 D-037：\n  "
            + string.Join("\n  ", violations)
            + "\n\n应改为通过 Application 层的抽象接口获取实现。");
    }

    /// <summary>是否使用了 Infrastructure 的命名空间。</summary>
    private static bool ReferencesInfrastructureNamespace(string filePath)
    {
        // 同时匹配 using 指令与完全限定名，两种写法都会造成实际耦合。
        var usingPattern = new Regex(
            @"^\s*(global\s+)?using\s+(static\s+)?ReleaseWatcher\.Infrastructure(\.[A-Za-z0-9_.]+)?\s*;",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        var qualifiedPattern = new Regex(
            @"\bReleaseWatcher\.Infrastructure\.[A-Za-z0-9_.]+",
            RegexOptions.IgnoreCase);

        var source = File.ReadAllText(filePath);
        return usingPattern.IsMatch(source) || qualifiedPattern.IsMatch(source);
    }

    /// <summary>排除 bin / obj 下的编译产物与生成文件。</summary>
    private static bool IsUnderBuildOutput(string filePath)
    {
        var normalized = filePath.Replace('/', '\\');
        return normalized.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase);
    }
}