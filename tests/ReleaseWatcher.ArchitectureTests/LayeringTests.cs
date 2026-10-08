using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ReleaseWatcher.ArchitectureTests;

/// <summary>
/// 分层依赖方向的架构断言。
/// </summary>
/// <remarks>
/// 依据：<c>DECISIONS.md</c> D-003（分层）、D-037（App 对 Infrastructure 的引用仅用于组合根）、
/// <c>AGENTS.md</c> 第 1.2 节、<c>RISKS.md</c> R-007。
///
/// 这些断言把此前只能靠人工执行的 <c>RUNBOOK.md</c> 2.2 校验自动化，
/// 防止分层在后续维护中被无意破坏。
/// </remarks>
public sealed class LayeringTests
{
    /// <summary>
    /// 提取 <c>ProjectReference</c> 引用的项目名。
    /// </summary>
    /// <remarks>
    /// 两个细节：
    /// <list type="bullet">
    /// <item><c>dotnet add reference</c> 生成的是仅含 <c>Include</c> 的相对路径形式
    /// （<c>&lt;ProjectReference Include="..\X\X.csproj" /&gt;</c>），没有 <c>Name</c> 子元素，
    /// 因此必须从路径反推项目名。手写 csproj 时可能带 <c>&lt;Name&gt;</c>，两种都支持。</item>
    /// <item>不要用 <c>XNamespace</c> 过滤。SDK 风格的 csproj（如
    /// <c>&lt;Project Sdk="Microsoft.NET.Sdk"&gt;</c>）<b>没有 xmlns 声明</b>，
    /// 其元素位于空命名空间；带 MSBuild 命名空间的查询会一条都匹配不到。</item>
    /// </list>
    /// </remarks>
    private static string[] ReferencedProjects(string projectName)
    {
        var xml = XDocument.Load(SolutionLayout.ProjectFile(projectName));

        var names = new List<string>();

        foreach (var reference in xml.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            // 支持两种写法：仅 Include 的相对路径（dotnet add reference 生成），
            // 以及含 <Name> 子元素的手写形式。
            var name = reference.Elements().FirstOrDefault(c => c.Name.LocalName == "Name")?.Value
                       ?? reference.Attribute("Include")?.Value;

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var referencedName = Path.GetFileNameWithoutExtension(name.Trim());
            if (!string.IsNullOrEmpty(referencedName))
            {
                names.Add(referencedName);
            }
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>读取工程文件中的某项属性值（如 TargetFramework / UseWPF）。</summary>
    private static string? ReadMsBuildProperty(string projectName, string propertyName)
    {
        var xml = XDocument.Load(SolutionLayout.ProjectFile(projectName));

        return xml.Descendants()
            .Where(e => e.Name.LocalName == propertyName)
            .Select(e => e.Value)
            .FirstOrDefault();
    }

    [Fact]
    public void Domain_MustNotReferenceAnyOtherProject()
    {
        // 领域层是依赖链底端：一旦它引用任何东西，整套分层就失去意义。
        Assert.Empty(ReferencedProjects("ReleaseWatcher.Domain"));
    }

    [Fact]
    public void Application_MustOnlyReferenceDomain()
    {
        Assert.Equal(
            ["ReleaseWatcher.Domain"],
            ReferencedProjects("ReleaseWatcher.Application"));
    }

    [Fact]
    public void Infrastructure_MustOnlyReferenceApplicationAndDomain()
    {
        Assert.Equal(
            ["ReleaseWatcher.Application", "ReleaseWatcher.Domain"],
            ReferencedProjects("ReleaseWatcher.Infrastructure")
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    [Fact]
    public void App_MustNotDirectlyReferenceDomain()
    {
        // App 只应经由 Application 触达领域层。直接引用会绕过应用层的用例编排。
        Assert.DoesNotContain("ReleaseWatcher.Domain", ReferencedProjects("ReleaseWatcher.App"));
    }

    [Fact]
    public void App_MayReferenceApplicationAndInfrastructure()
    {
        // Infrastructure 允许存在：它承载 DI 组合根所需的实现注册（D-037）。
        // 用途约束不在这里断言 —— 见 CompositionRootTests。
        var references = ReferencedProjects("ReleaseWatcher.App");
        Assert.Contains("ReleaseWatcher.Application", references);
        Assert.Contains("ReleaseWatcher.Infrastructure", references);
    }

    [Fact]
    public void NoProject_MayReferenceLocalizationGenerator()
    {
        // D-019 硬边界：LocalizationGenerator 是独立工作空间，物理独立、逻辑单向关联。
        // 任何 ProjectReference 都会把 Roslyn / TOML Parser 等开发期依赖拖入发布产物。
        // 测试项目同样受约束 —— 它会连带拖进被引用项目的一切。
        foreach (var project in SolutionLayout.LayerProjects.Concat(SolutionLayout.TestProjects))
        {
            Assert.DoesNotContain(
                ReferencedProjects(project),
                r => r.Contains("LocalizationGenerator", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void ArchitectureTestProject_MustNotReferenceLayerProjects()
    {
        // 见 SolutionLayout 的说明：架构检查的对象是 .csproj 本身，
        // 引用它们会造成自我循环，并把 WPF 拖入测试进程。
        Assert.Empty(ReferencedProjects("ReleaseWatcher.ArchitectureTests"));
    }

    [Fact]
    public void UnitTestProject_MustOnlyReferenceInfrastructure()
    {
        // 单元测试项目与架构测试项目的取舍相反：它**必须**引用被测项目才能拿到类型。
        // 但边界仍要守住 ——
        //   不可引用 App：那会把 WPF 与 net10.0-windows 拖进测试进程，测试无法在非 Windows 上跑；
        //   不可引用 Application / Domain 的测试包装层：当前无业务类型可测，提前引入只是噪音。
        Assert.Equal(
            ["ReleaseWatcher.Infrastructure"],
            ReferencedProjects("ReleaseWatcher.Infrastructure.Tests"));
    }

    [Fact]
    public void UnitTestProject_MustNotReferenceWpfApp()
    {
        // 上条断言的根因单独锁定：一旦有人为图方便直接引用 App，
        // 测试会静默变成 WPF 进程，跨平台执行能力随之丧失。
        Assert.DoesNotContain(
            "ReleaseWatcher.App",
            ReferencedProjects("ReleaseWatcher.Infrastructure.Tests"));
    }

    [Theory]
    [InlineData("ReleaseWatcher.Domain")]
    [InlineData("ReleaseWatcher.Application")]
    [InlineData("ReleaseWatcher.Infrastructure")]
    [InlineData("ReleaseWatcher.App")]
    public void AllLayerProjects_MustTargetNet10(string projectName)
    {
        // 防止某个项目被误设为旧版本框架，导致运行时行为差异难以排查。
        var targetFramework = ReadMsBuildProperty(projectName, "TargetFramework");

        Assert.True(
            !string.IsNullOrWhiteSpace(targetFramework),
            $"{projectName} 未声明 TargetFramework");
        Assert.Matches(new Regex(@"^net10\.0(-windows)?$"), targetFramework!);
    }

    [Fact]
    public void App_MustTargetWindowsAndUseWpf()
    {
        // 不可移除：整个 UI 层是 WPF，误删会得到一个无界面的控制台程序。
        Assert.Equal("net10.0-windows", ReadMsBuildProperty("ReleaseWatcher.App", "TargetFramework"));
        Assert.Equal("true", ReadMsBuildProperty("ReleaseWatcher.App", "UseWPF"));
    }
}