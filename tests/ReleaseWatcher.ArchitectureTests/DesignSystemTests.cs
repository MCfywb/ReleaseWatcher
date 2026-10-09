using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ReleaseWatcher.ArchitectureTests;

/// <summary>
/// XAML 与资源字典的约定守卫。
/// </summary>
/// <remarks>
/// <para>
/// 依据：<c>DECISIONS.md</c> D-046（设计系统以资源字典承载）、
/// D-026（禁止硬编码颜色）。
/// </para>
/// <para>
/// 这些约束的共同特点是<strong>编译器不管</strong>：
/// XAML 里写错注释、漏掉语义键、硬编码颜色，编译照样通过，
/// 但会在运行期表现为色板缺色、主题切换不生效、或干脆解析失败。
/// 因此必须由测试守护。
/// </para>
/// </remarks>
public sealed class DesignSystemTests
{
    /// <summary>设计系统字典目录（相对 App 项目）。</summary>
    private const string ThemeDirectory = "Resources/Theme";

    /// <summary>设计系统必须存在的字典文件。</summary>
    public static TheoryData<string> RequiredThemeFiles =>
    [
        "Theme.xaml",
        "Colors.Light.xaml",
        "Colors.Dark.xaml",
        "Typography.xaml",
        "Spacing.xaml",
        "Controls.xaml",
    ];

    /// <summary>必须存在的语义色键（两套主题都要有且名称一致）。</summary>
    public static TheoryData<string> RequiredBrushKeys =>
    [
        "Brush.Background.Primary",
        "Brush.Background.Secondary",
        "Brush.Surface",
        "Brush.Surface.Hover",
        "Brush.Surface.Selected",
        "Brush.Surface.Pressed",
        "Brush.Text.Primary",
        "Brush.Text.Secondary",
        "Brush.Text.OnAccent",
        "Brush.Border",
        "Brush.Border.Subtle",
        "Brush.Accent",
        "Brush.Accent.Hover",
        "Brush.Success",
        "Brush.Warning",
        "Brush.Error",
    ];

    [Theory]
    [MemberData(nameof(RequiredThemeFiles))]
    public void ThemeFile_MustExist(string fileName)
    {
        // 缺文件不会报错，只会在MergedDictionaries 里静默失效或启动时抛异常，
        // 难以定位。显式断言存在性。
        var path = Path.Combine(AppProjectRoot(), ThemeDirectory, fileName);

        Assert.True(
            File.Exists(path),
            $"设计系统文件缺失：{path}。"
            + "新增字典后请同步更新 Theme.xaml 的 MergedDictionaries 与 RequiredThemeFiles。");
    }

    [Theory]
    [MemberData(nameof(RequiredBrushKeys))]
    public void ColorDictionary_MustDefineKeyInBothThemes(string key)
    {
        // 深浅两套主题必须键名完全一致，否则切换时部分控件会取不到色。
        foreach (var themeFile in new[] { "Colors.Light.xaml", "Colors.Dark.xaml" })
        {
            var path = Path.Combine(AppProjectRoot(), ThemeDirectory, themeFile);
            var keys = ReadResourceKeys(path);

            Assert.True(
                keys.Contains(key),
                $"{themeFile} 未定义语义键 {key}。深浅两套主题的键名必须一致。");
        }
    }

    [Fact]
    public void ThemeEntry_MustMergeColorBeforeControls()
    {
        // 合并顺序有依赖：Controls.xaml 会 StaticResource 引用 Colors 的键，
        // Colors 必须在前面。反向会导致解析失败。
        var path = Path.Combine(AppProjectRoot(), ThemeDirectory, "Theme.xaml");
        var xml = XDocument.Load(path);

        var sources = xml.Descendants()
            .Where(e => e.Name.LocalName == "ResourceDictionary"
                        && e.Attribute("Source") is not null)
            .Select(e => e.Attribute("Source")!.Value)
            .ToArray();

        Assert.True(sources.Length > 0, "Theme.xaml 未合并任何字典");

        var colorIndex = Array.FindIndex(
            sources, s => s.Contains("Colors.", StringComparison.OrdinalIgnoreCase));
        var typographyIndex = Array.FindIndex(
            sources, s => s.Contains("Typography", StringComparison.OrdinalIgnoreCase));
        var spacingIndex = Array.FindIndex(
            sources, s => s.Contains("Spacing", StringComparison.OrdinalIgnoreCase));

        Assert.True(colorIndex >= 0, "Theme.xaml 未合并任何颜色字典");
        Assert.True(colorIndex < typographyIndex,
            "颜色字典必须排在 Typography 之前 —— 控件样式要引用颜色键。");
        Assert.True(colorIndex < spacingIndex,
            "颜色字典必须排在 Spacing 之前 —— 控件样式要引用尺寸键。");
    }

    [Fact]
    public void AppXaml_MustReferenceThemeEntryOnly()
    {
        // App.xaml 只应引用合并入口 Theme.xaml。
        // 直接引用 Colors.Light.xaml 会让主题切换失效：
        // 被直接引用的字典不在MergedDictionaries 的可替换位置上。
        var path = Path.Combine(AppProjectRoot(), "App.xaml");
        var xml = XDocument.Load(path);

        var sources = xml.Descendants()
            .Where(e => e.Name.LocalName == "ResourceDictionary"
                        && e.Attribute("Source") is not null)
            .Select(e => e.Attribute("Source")!.Value)
            .ToArray();

        Assert.All(sources, s => Assert.Contains("Theme.xaml", s, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ControlsXaml_MustUseDynamicResourceForBrushes()
    {
        // 这是主题切换能否生效的关键：
        // 控件样式里凡涉及颜色的都必须用 DynamicResource。
        // 误用 StaticResource 会让值在解析时固定，切换后颜色停留在浅色值上，
        // 且**不会报任何错** —— 只能靠肉眼发现。
        var path = Path.Combine(AppProjectRoot(), ThemeDirectory, "Controls.xaml");
        var content = File.ReadAllText(path);

        var staticBrushUsages = Regex.Matches(
            content,
            """StaticResource\s+Brush\.[A-Za-z.]+""",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));

        Assert.True(
            staticBrushUsages.Count == 0,
            "Controls.xaml 中出现了 StaticResource 引用 Brush.*："
            + string.Join("、", staticBrushUsages.Select(m => m.Value))
            + "。颜色必须用 DynamicResource，否则主题切换不生效。");
    }

    [Fact]
    public void AllXamlFiles_MustNotContainDoubleHyphenInComments()
    {
        // XML 规范禁止注释内出现 '--'。曾因此反复触发 MC3000 编译错误，
        // 一旦发生又不易一眼看出是注释写法问题，故由测试统一扫描。
        AssertNoDoubleHyphenInComments(AppProjectRoot());
    }

    [Fact]
    public void ColorValues_MustNotBeHardcodedInViews()
    {
        // D-026：禁止硬编码颜色。视图里出现 #RRGGBB 说明设计系统缺了对应语义键，
        // 应补到 Colors 字典而不是在页面上绕过。
        //
        // 例外：设计系统自身（Theme 目录）就是颜色的**定义处**，允许写字面值。
        var viewsDirectory = Path.Combine(AppProjectRoot(), "Views");
        if (!Directory.Exists(viewsDirectory))
        {
            return;
        }

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(viewsDirectory, "*.xaml", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);

            // 去掉注释后再检查，避免把注释里的示例值算作硬编码
            var stripped = Regex.Replace(content, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);

            foreach (Match match in Regex.Matches(stripped, """#[0-9A-Fa-f]{6,8}\b"""))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "视图中出现硬编码颜色：" + string.Join("、", offenders)
            + "。应改为引用 Brush.* 语义键（D-026）。");
    }

    /// <summary>
    /// App 层源码中不得出现未加前缀的 <c>Application.</c>。
    /// </summary>
    /// <remarks>
    /// 本项目存在 <c>ReleaseWatcher.Application</c> <strong>命名空间</strong>
    /// （对应 Application 项目），它会遮蔽 <c>System.Windows.Application</c>。
    /// 写 <c>Application.Current</c> 会得到
    /// <c>CS0234: 命名空间中不存在"Current"</c> —— 错误信息完全不提示
    /// "是你自己遮蔽了它"，属极易重复踩的坑（已发生两次：App.xaml.cs、ThemeManager）。
    /// </remarks>
    [Fact]
    public void AppSource_MustNotUseAmbiguousApplicationReference()
    {
        var appRoot = AppProjectRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file))
            {
                continue;
            }

            var content = File.ReadAllText(file);

            // 去掉注释与字符串字面量，避免把文档里的示例算作违规
            var stripped = Regex.Replace(content, @"//.*$", string.Empty, RegexOptions.Multiline);
            stripped = Regex.Replace(stripped, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            stripped = Regex.Replace(stripped, "@\"[^\"]*\"", "@\"\"", RegexOptions.None);

            // 匹配未加前缀的 Application. —— System.Windows.Application. 已在前面，
            // 因此排除该前缀。ReleaseWatcher.Application. 同样不算违规。
            foreach (Match match in Regex.Matches(
                stripped, @"(?<![\w.])Application\.[A-Za-z]"))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "App 层须写全名 System.Windows.Application. —— 项目内"
            + " ReleaseWatcher.Application 命名空间会遮蔽它："
            + string.Join("、", offenders.Distinct().Take(5)));
    }

    /// <summary>App 项目根目录。</summary>
    private static string AppProjectRoot()
        => Path.Combine(SolutionLayout.SolutionRoot, "src", "ReleaseWatcher.App");

    /// <summary>是否属于 bin/obj 下的构建产物副本。</summary>
    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
           || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");

    /// <summary>读取资源字典中定义的所有 x:Key。</summary>
    private static HashSet<string> ReadResourceKeys(string path)
    {
        var xml = XDocument.Load(path);

        return xml.Descendants()
            .Where(e => e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) is not null)
            .Select(e => e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>扫描所有 .xaml 文件，找出注释内含 '--' 的违规。</summary>
    private static void AssertNoDoubleHyphenInComments(string root)
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            // bin/obj 下的副本不是源文件
            if (IsBuildOutput(file))
            {
                continue;
            }

            var content = File.ReadAllText(file);

            foreach (Match match in Regex.Matches(content, @"<!--.*?-->", RegexOptions.Singleline))
            {
                var body = match.Value;

                // 注释体为 "<-- ... -->"：去掉首尾标记后不应含 "--"
                var inner = body.Length >= 7 ? body[4..^3] : string.Empty;

                if (inner.Contains("--", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {Trim(body)}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "XML 注释内不得出现 '--'（会触发 MC3000 编译错误）："
            + string.Join(" | ", offenders.Take(5))
            + "。请改用 <!-- 标题 --> 这类单行注释。");
    }

    private static string Trim(string value)
        => value.Length <= 60 ? value : value[..60] + "...";
}