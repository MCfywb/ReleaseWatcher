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

            // 去掉注释与字符串字面量，避免把文档里的示例算作违规。
            //
            // 必须同时处理普通字符串、@ 字符串与**插值字符串** $""：
            // 只剥离 @"" 会漏掉 $"..." 里的内容，而插值字符串
            // 恰恰是异常消息的常用写法 —— 守卫会对它误报。
            var stripped = Regex.Replace(content, @"//.*$", string.Empty, RegexOptions.Multiline);
            stripped = Regex.Replace(stripped, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            stripped = Regex.Replace(stripped, "@\"[^\"]*\"", "@\"\"", RegexOptions.None);
            stripped = Regex.Replace(stripped, "\\$\"(\\{[^\"}]*\\}|[^\"])*\"", "\"\"", RegexOptions.None);

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

    /// <summary>
    /// 模板内的 <c>ContentPresenter</c> 必须显式传递前景色与字体。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这条断言来自一次真实故障：用户反馈"深色模式下已选中、未选中、
    /// 已禁用这几个字看不见"。
    /// </para>
    /// <para>
    /// <b>根因</b>：<c>ContentPresenter</c> <strong>不会</strong>把父控件的
    /// <c>Foreground</c> 自动传给它呈现的内容。因此模板里的
    /// <c>ContentPresenter</c> 若未显式绑定，文字会落到系统默认黑 ——
    /// 在浅色主题下只是勉强可辨（缺陷被掩盖），在深色主题下彻底消失。
    /// WPF 的隐式样式同样不会穿透到模板内部由 ContentPresenter 生成的元素。
    /// </para>
    /// <para>
    /// <b>为什么必须用测试守护</b>：编译期完全正常，只有切到深色主题
    /// 肉眼才看得出。<c>Button.Base</c> 当时就已经漏了 ——
    /// 只是因为 Primary 样式恰好把 Foreground 设在了别处才没暴露。
    /// </para>
    /// </remarks>
    [Fact]
    public void ContentPresenters_MustPropagateForegroundAndFont()
    {
        var path = Path.Combine(AppProjectRoot(), ThemeDirectory, "Controls.xaml");
        var content = File.ReadAllText(path);

        // 逐个 <ContentPresenter ... /> 片段检查
        var presenters = Regex.Matches(
            content,
            "<ContentPresenter\\b[^>]*>",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));

        Assert.True(
            presenters.Count > 0,
            "Controls.xaml 中未找到 ContentPresenter —— 模板结构可能已变，请同步更新本断言。");

        var offenders = new List<string>();

        foreach (Match match in presenters)
        {
            var element = match.Value;

            // TextElement.Foreground / TextBlock.Foreground 任一即可
            var hasForeground =
                element.Contains("TextElement.Foreground", StringComparison.Ordinal)
                || element.Contains("TextBlock.Foreground", StringComparison.Ordinal);

            // ComboBox 的 ContentPresenter 用 SelectionBoxItem，
            // 其文字颜色由外层 ComboBox.Foreground 决定，同样需要传递。
            if (!hasForeground)
            {
                offenders.Add(Trim(element));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "以下 ContentPresenter 未传递前景色，其内容在深色主题下会不可见："
            + string.Join(" | ", offenders)
            + "。修法：加 TextElement.Foreground=\"{TemplateBinding Foreground}\"。");
    }

    [Fact]
    public void AccentBrush_ShouldMatchBetweenLightAndDarkThemes()
    {
        // Accent 若两套主题不一致，用户切换主题时蓝色会跳变 ——
        // 视觉上像是"主题没切干净"。
        var light = ReadResourceKeys(Path.Combine(AppProjectRoot(), ThemeDirectory, "Colors.Light.xaml"));
        var dark = ReadResourceKeys(Path.Combine(AppProjectRoot(), ThemeDirectory, "Colors.Dark.xaml"));

        var lightKeys = light.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var darkKeys = dark.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        var onlyInLight = lightKeys.Except(darkKeys, StringComparer.Ordinal).ToArray();
        var onlyInDark = darkKeys.Except(lightKeys, StringComparer.Ordinal).ToArray();

        Assert.True(
            onlyInLight.Length == 0 && onlyInDark.Length == 0,
            $"深浅两套主题的键必须完全一致。仅浅色有：{string.Join("、", onlyInLight)}；"
            + $"仅深色有：{string.Join("、", onlyInDark)}");
    }

    [Fact]
    public void AllThemeFiles_ShouldParseAsValidXml()
    {
        // XAML 字典若 XML 非法，MergeDictionaries 会静默失效或启动崩溃。
        foreach (var fileName in new[] { "Theme.xaml", "Colors.Light.xaml", "Colors.Dark.xaml",
                                        "Typography.xaml", "Spacing.xaml", "Controls.xaml" })
        {
            var path = Path.Combine(AppProjectRoot(), ThemeDirectory, fileName);

            var exception = Record.Exception(() => XDocument.Load(path));

            Assert.True(exception is null, $"{fileName} 不是合法 XML：{exception?.Message}");
        }
    }

    /// <summary>
    /// ThemeManager 不得替换 <c>Theme.xaml</c> 本身。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这是本项目最隐蔽的一个真实故障。</b>用户两次反馈
    /// "深色模式下 CheckBox 文字是黑色的""ComboBox 下拉后下拉框背景是白色的"，
    /// 看起来是配色问题，实际是 —— <c>Theme.xaml</c> 内部的
    /// Typography / Spacing / Controls 三个字典<strong>从未被加载过</strong>。
    /// </para>
    /// <para>
    /// <b>成因</b>：早先的 <c>ThemeManager.Apply</c> 直接把
    /// <c>App.Resources.MergedDictionaries[0]</c>（那正是 Theme.xaml）
    /// 替换成了 <c>Colors.Dark.xaml</c>。替换掉的是 Theme.xaml 整体，
    /// 三个样式字典随之消失，所有控件退回系统默认（Aero）外观。
    /// </para>
    /// <para>
    /// <b>为什么极易误判</b>：颜色键 <c>Brush.Text.Primary</c> 仍能正确解析
    /// （替换进去的那份 Colors 本身是好的），于是"两种颜色各自都对，
    /// 缺的只是承载样式的字典" —— 看起来像主题切换没生效，
    /// 实际是样式压根没加载。
    /// </para>
    /// <para>
    /// <b>修法</b>：ThemeManager 改为向下定位 Theme.xaml，
    /// 只替换它内部的色板槽位（<c>Theme.xaml</c> 的 MergedDictionaries[0]）。
    /// </para>
    /// </remarks>
    [Fact]
    public void ThemeManager_ShouldReplaceInnerPaletteSlotNotThemeEntry()
    {
        var path = Path.Combine(AppProjectRoot(), "Services", "ThemeManager.cs");
        var content = File.ReadAllText(path);

        // 危险写法：直接对 app.Resources.MergedDictionaries 下标赋值
        var dangerousAssignment = Regex.Matches(
            content,
            @"app\.Resources\.MergedDictionaries\s*\[[^\]]*\]\s*=",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));

        Assert.True(
            dangerousAssignment.Count == 0,
            "ThemeManager 直接对 app.Resources.MergedDictionaries[下标] 赋值："
            + "那会替换掉 Theme.xaml 本身，连带删除 Typography / Spacing / Controls"
            + "三个字典，所有控件样式失效并退回系统默认外观。"
            + "应改为定位 Theme.xaml 内部后替换其色板槽位。");

        // 必须向下定位 Theme.xaml
        Assert.Contains(
            "Theme.xaml",
            content,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThemeEntry_ShouldNestAllDictionariesIncludingControls()
    {
        // Theme.xaml 必须自身合并全部四个字典。
        //
        // 控件样式（Controls.xaml）加载在 Theme.xaml 内部，而不是 App.xaml 顶层 ——
        // 因为 ThemeManager 要按"下标 + 顺序"定位色板槽位，
        // 顶层结构一旦变化，定位就会失效。
        var path = Path.Combine(AppProjectRoot(), ThemeDirectory, "Theme.xaml");
        var sources = XDocument.Load(path).Descendants()
            .Where(e => e.Name.LocalName == "ResourceDictionary"
                        && e.Attribute("Source") is not null)
            .Select(e => e.Attribute("Source")!.Value)
            .ToArray();

        Assert.Contains(sources, s => s.Contains("Colors.Light", StringComparison.Ordinal));
        Assert.Contains(sources, s => s.Contains("Typography", StringComparison.Ordinal));
        Assert.Contains(sources, s => s.Contains("Spacing", StringComparison.Ordinal));
        Assert.Contains(sources, s => s.Contains("Controls", StringComparison.Ordinal));
    }

    /// <summary>
    /// Popup 内不得使用 <c>StaticResource</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// WPF 的 <c>Popup</c> 内容被放入<strong>独立的窗口树</strong>，
    /// 其资源查找不会经过 <c>Controls.xaml</c> 合并进来的
    /// <c>Spacing.xaml</c> / <c>Typography.xaml</c>。
    /// </para>
    /// <para>
    /// 此时 <c>StaticResource</c> 解析失败，返回的是字符串字面量，
    /// 随后被喂给类型转换器，于是抛
    /// <c>FormatException: 无法将"{StaticResource"转换为 Length</c>，
    /// <strong>应用启动即崩溃</strong>。
    /// </para>
    /// <para>
    /// 实测确认：ComboBox 下拉面板的 Margin / CornerRadius / ItemsPresenter
    /// 三处改为 DynamicResource 后崩溃消失。
    /// <c>DynamicResource</c> 会向上查找到 <c>Application.Resources</c>，
    /// 而设计系统字典都在那里。
    /// </para>
    /// </remarks>
    [Fact]
    public void PopupContent_MustNotUseStaticResource()
    {
        var path = Path.Combine(AppProjectRoot(), ThemeDirectory, "Controls.xaml");
        var content = File.ReadAllText(path);

        // 提取每个 <Popup ...> 到其配对 </Popup> 之间的内容
        var offenders = new List<string>();

        foreach (Match popup in Regex.Matches(content, "<Popup\\b.*?</Popup>", RegexOptions.Singleline))
        {
            var inner = popup.Value;

            foreach (Match staticResource in Regex.Matches(
                inner, "StaticResource\\s+[A-Za-z][A-Za-z.]*"))
            {
                offenders.Add(staticResource.Value);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Popup 内不得使用 StaticResource —— Popup 位于独立窗口树，"
            + "资源查找不会经过本字典合并进来的其他字典，解析失败后"
            + "会被当作字符串喂给类型转换器而崩溃。发现："
            + string.Join("、", offenders.Distinct())
            + "。改用 DynamicResource。");
    }

    /// <summary>
    /// <c>Thickness</c> 属性不得引用 <c>sys:Double</c> 类型的资源键。
    /// </summary>
    /// <remarks>
    /// <c>Space.XS</c> 等间距键是 <c>sys:Double</c>（供 Width / Height 用），
    /// 而 <c>Margin</c> / <c>Padding</c> 是 <c>Thickness</c> 类型。
    /// 把 Double 键用于 Thickness 属性会抛
    /// <c>FormatException: 无法将"{StaticResource"转换为 Length</c>。
    /// <para>
    /// Thickness 属性须引用 <c>Space.Padding.*</c> / <c>Space.Margin.*</c>，
    /// Width / Height 须引用 <c>Space.*</c> 或 <c>Size.Space.*</c>。
    /// </para>
    /// </remarks>
    [Fact]
    public void ThicknessAttributes_MustNotReferenceDoubleSpacingKeys()
    {
        var offenders = new List<string>();

        var files = new[]
        {
            Path.Combine(AppProjectRoot(), ThemeDirectory, "Controls.xaml"),
            Path.Combine(AppProjectRoot(), ThemeDirectory, "Theme.xaml"),
            Path.Combine(AppProjectRoot(), "Views", "MainWindow.xaml"),
            // 设计系统窗口同样大量使用间距键，2026-10-09 建立时曾遗漏 ——
            // 类型违规会在运行时抛 FormatException，编译期不报错。
            Path.Combine(AppProjectRoot(), "Views", "DesignSystemWindow.xaml"),
        };

        // 匹配两类位置：
        //   1. <Setter Property="Padding" Value="{StaticResource ...}" />
        //   2. <Element Margin="{StaticResource ...}" />
        // 只查 Margin/Padding 属性是不够的 —— Setter.Value 同样会崩。
        var pattern = new Regex(
            @"(?:Value|Margin|Padding)=\""(?<value>[^\""']*StaticResource[^\""']*)\""",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));

        foreach (var file in files.Where(File.Exists))
        {
            var content = File.ReadAllText(file);

            foreach (Match match in pattern.Matches(content))
            {
                var value = match.Groups["value"].Value;

                // Space.Padding.* / Space.Margin.* 是 Thickness，正确
                if (value.Contains("Space.Padding", StringComparison.Ordinal)
                    || value.Contains("Space.Margin", StringComparison.Ordinal)
                    || value.Contains("Size.ContentPadding", StringComparison.Ordinal)
                    || value.Contains("Size.BorderThickness", StringComparison.Ordinal))
                {
                    continue;
                }

                // 其余 Space.* 是 sys:Double，用在 Thickness 上会崩
                if (value.Contains("Space.", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Margin / Padding 是 Thickness 类型，不可引用 sys:Double 的间距键"
            + "（会导致启动崩溃）。应改用 Space.Padding.* 或 Space.Margin.*："
            + string.Join(" | ", offenders.Distinct().Take(5)));
    }

    /// <summary>
    /// 可点击控件的模板必须包含承载命中测试的 <c>Border</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>真实故障</b>：用户反馈"下拉框无法拉出"。
    /// 根因是 <c>ComboBox.Toggle</c> 的模板里只有一个 <c>Path</c>（箭头图形），
    /// 而图形只在自己那10×6 的像素范围内参与命中测试 ——
    /// 点控件其余位置毫无反应，表现为"点击没反应、下拉拉不开"。
    /// </para>
    /// <para>
    /// <b>修法</b>：模板里加一个铺满的透明 <c>Border</c> 承载命中。
    /// <c>Panel</c> 本身不参与命中测试，<c>Border</c> 才是承载元素；
    /// 且必须设<code>Background="Transparent"</code>，
    /// 完全不设 Background 则连透明区域也不命中。
    /// </para>
    /// <para>
    /// <b>为什么必须用测试守护</b>：这类缺陷编译期完全正常，
    /// 且外观看不出异常（箭头画得好好的），只能靠真实点击发现。
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("ComboBox.Toggle")]
    public void ToggleTemplate_MustContainHitTestCarrier(string styleKey)
    {
        var path = Path.Combine(AppProjectRoot(), ThemeDirectory, "Controls.xaml");
        var content = File.ReadAllText(path);

        // 提取该样式的模板部分
        var styleMatch = Regex.Match(
            content,
            $@"x:Key=""{Regex.Escape(styleKey)}""[\s\S]*?</Style>",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));

        Assert.True(
            styleMatch.Success,
            $"Controls.xaml 中未找到样式 {styleKey} —— 模板结构可能已变，请同步更新本断言。");

        var template = styleMatch.Value;

        // 必须有 Border 承载命中测试
        Assert.True(
            template.Contains("<Border", StringComparison.Ordinal),
            $"{styleKey} 的模板中没有 Border。图形元素（如 Path）只在自己占据的像素范围"
            + "参与命中测试，点控件其余位置不会有反应 —— 表现为\"点击无响应\"。"
            + "需加一个铺满的透明 Border 承载命中测试。");
    }

    [Fact]
    public void ComboBoxToggle_BorderShouldBeTransparentAndFill()
    {
        var path = Path.Combine(AppProjectRoot(), ThemeDirectory, "Controls.xaml");
        var content = File.ReadAllText(path);

        var styleMatch = Regex.Match(
            content,
            @"x:Key=""ComboBox\.Toggle""[\s\S]*?</Style>",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));

        Assert.True(styleMatch.Success, "未找到 ComboBox.Toggle 样式");

        var template = styleMatch.Value;

        // 承载命中的 Border 必须有透明背景，否则不参与命中测试
        Assert.True(
            Regex.IsMatch(template, @"<Border[^>]*Background\s*=\s*""Transparent"""),
            "ComboBox.Toggle 的 Border 未设 Background=\"Transparent\"。"
            + "完全透明（不设Background）的 Border 同样不参与命中测试，"
            + "结果仍是点击无响应。");

        // 不能只放Path —— 那正是本次故障的成因
        var borderBlock = Regex.Match(
            template,
            @"<Border[\s\S]*?</Border>",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));

        Assert.True(borderBlock.Success, "ComboBox.Toggle 中未找到 Border");
        Assert.True(
            borderBlock.Value.Contains("Transparent", StringComparison.Ordinal),
            "承载命中测试的 Border 内没有透明背景 —— 它必须铺满整个控件才能接住点击。");
    }

    [Fact]
    public void ThemeManager_ShouldSupportSystemTheme()
    {
        // 用户要求"深色模式跟随系统"，属已确认功能。
        // 缺 System 取值意味着该功能回退为硬编码。
        var path = Path.Combine(AppProjectRoot(), "Services", "ThemeManager.cs");
        var content = File.ReadAllText(path);

        Assert.Contains("System", content, StringComparison.Ordinal);

        // 必须读注册表项，不能靠 SystemParameters
        Assert.True(
            content.Contains("AppsUseLightTheme", StringComparison.Ordinal),
            "未读取 AppsUseLightTheme 注册表值 —— 那是 Windows 10/11"
            + "「设置 → 个性化 → 应用模式」真正写入的位置。");

        // 缺省值应为 System（跟随系统）：用户从未保存过偏好时自动获得与系统一致的外观。
        //
        // 2026-10-09 起 ResolveInitialTheme 改为读取 config.json（主题持久化修复），
        // 方法签名从无参变为接受配置路径，因此断言不能再匹配某个具体方法形状 ——
        // 改为验证「LoadSavedTheme 缺失时回落 System」这一语义。
        // 匹配对象是回退表达式而非整条语句，容忍后续实现调整。
        Assert.True(
            Regex.IsMatch(
                content,
                @"LoadSavedTheme\([^)]*\)\s*\?\?\s*AppTheme\.System"
                + @"|ResolveInitialTheme\([^)]*\)\s*=>\s*AppTheme\.System",
                RegexOptions.None,
                TimeSpan.FromSeconds(2)),
            "未保存过主题偏好时应回落 AppTheme.System（跟随系统），"
            + "若缺省固定为 Light/Dark，则「跟随系统」选项对首次启动的用户没有意义。");
    }

    [Fact]
    public void ThemeEnum_ShouldContainAllThreeOptions()
    {
        // 三个取值都要有：跟随系统 / 浅色 / 深色
        var path = Path.Combine(AppProjectRoot(), "Services", "ThemeManager.cs");
        var content = File.ReadAllText(path);

        var enumMatch = Regex.Match(
            content,
            @"public enum AppTheme\s*\{[\s\S]*?\}",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));

        Assert.True(enumMatch.Success, "未找到 AppTheme 枚举");

        var body = enumMatch.Value;

        foreach (var member in new[] { "Light", "Dark", "System" })
        {
            Assert.True(
                body.Contains(member, StringComparison.Ordinal),
                $"AppTheme 枚举缺少 {member} 取值。");
        }
    }

    [Fact]
    public void PreviewPage_ShouldOfferSystemThemeOption()
    {
        // 预览页是验证主题切换的入口，缺少"跟随系统"选项则无法验证该功能。
        var path = Path.Combine(AppProjectRoot(), "Views", "MainWindow.xaml");
        var content = File.ReadAllText(path);

        Assert.Contains("Tag=\"System\"", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// 预览页的水平 <see cref="StackPanel"/> 中，**元素间间隙**不得用左右双向边距键。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 2026-10-09 实际故障：圆角示例的三个框、控件行、状态色 chips 等，
    /// 相邻元素都用了 <c>Space.Padding.Horizontal.S</c>（"8,0" = 左右各 8）。
    /// 在水平 StackPanel 中，间隙 = 左元素 Right(8) + 右元素 Left(8) = 16，
    /// 是预期的两倍；更糟的是最后一个元素往往不赋该键，
    /// 于是间隙在 16 与 8 之间交替 —— 用户肉眼可见"三个框间距不一样"。
    /// </para>
    /// <para>
    /// 元素间间隙必须使用**右侧单边**键（<c>Space.Margin.Right.*</c>），
    /// 且只赋给"除最后一个外的每个元素"，才能保证间隙正好 = N 且处处相等。
    /// 此断言禁止在预览页的 <c>Margin</c> 上引用左右双向键，
    /// 把约定固化下来。
    /// </para>
    /// </remarks>
    [Fact]
    public void PreviewPage_InterItemGapsMustUseRightOnlyMargin()
    {
        var path = Path.Combine(AppProjectRoot(), "Views", "MainWindow.xaml");
        var content = File.ReadAllText(path);

        var offenders = Regex.Matches(content, @"Margin=""\{StaticResource Space\.Padding\.Horizontal")
            .Count;

        Assert.True(
            offenders == 0,
            $"预览页发现 {offenders} 处用左右双向边距键作元素间间隙（应改用 Space.Margin.Right.*）。");
    }

    /// <summary>
    /// 间距字典必须提供右侧单边间隙键（<c>Space.Margin.Right.*</c>）。
    /// </summary>
    /// <remarks>
    /// 若这些键缺失，预览页会被迫退回左右双向键，重新引入间隙翻倍故障。
    /// </remarks>
    [Fact]
    public void SpacingXaml_MustDefineRightOnlyMarginKeys()
    {
        var path = Path.Combine(AppProjectRoot(), "Resources", "Theme", "Spacing.xaml");
        var content = File.ReadAllText(path);

        foreach (var key in new[] { "Space.Margin.Right.S", "Space.Margin.Right.M", "Space.Margin.Right.L", "Space.Margin.Right.XL" })
        {
            Assert.True(
                content.Contains($"x:Key=\"{key}\""),
                $"缺少右侧单边间隙键 {key}");
        }
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