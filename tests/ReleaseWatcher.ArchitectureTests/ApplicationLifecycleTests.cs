namespace ReleaseWatcher.ArchitectureTests;

/// <summary>
/// 应用生命周期与主题行为的守卫断言。
/// </summary>
/// <remarks>
/// 这三项都曾是<strong>真实缺陷</strong>，且修复后极易被无意回退，因此固化为测试：
/// <list type="bullet">
/// <item>关闭主窗口后进程无窗口残留 —— <c>ShutdownMode="OnExplicitShutdown"</c>
/// 是为尚未实现的托盘预留的，托盘缺席时它只造成"关窗后进程还在"。</item>
/// <item>主题选择未持久化 —— 用户在下拉框选的主题重启后丢失。</item>
/// <item>系统主题监听未解除 —— <c>SystemEvents</c> 是静态事件，
/// 不解除会一直持有订阅者。</item>
/// </list>
///
/// 依据：<c>NOW.md</c> 已知待修项、<c>DECISIONS.md</c> D-026 / D-046、
/// <c>RISKS.md</c> Q-007、<c>AGENTS.md</c> 1.6.2。
/// </remarks>
public sealed class ApplicationLifecycleTests
{
    private static string AppSource(string relativePath)
        => Path.Combine(SolutionLayout.SolutionRoot, "src", "ReleaseWatcher.App", relativePath);

    private static string ReadAppSource(string relativePath)
    {
        var path = AppSource(relativePath);
        Assert.True(File.Exists(path), $"未找到 {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void AppXaml_MustNotUseExplicitShutdownUntilTrayExists()
    {
        // OnExplicitShutdown 的语义是"关闭最后一个窗口时不退出进程"。
        // 托盘尚未实现（D-026 的设置项也未生效），该设置会让用户关闭主窗口后
        // 只剩一个看不见的进程，只能靠任务管理器结束。
        // 实现托盘且「关闭时最小化到托盘」生效后，方可改回 —— 届时应同步删除本断言。
        //
        // 只检查 <Application> 元素上的属性，忽略注释：
        // App.xaml 的注释里会说明"当初为何设过OnExplicitShutdown、为何改回"，
        // 那是决策记录而非生效设置，按文本全文匹配会误报。
        var appXaml = ReadAppSource("App.xaml");

        var applicationTag = System.Text.RegularExpressions.Regex.Match(
            appXaml,
            @"<Application\b[^>]*>",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(2));

        Assert.True(applicationTag.Success, "App.xaml 中未找到 <Application> 元素");

        Assert.DoesNotContain(
            "OnExplicitShutdown",
            applicationTag.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeManager_MustWatchSystemThemeChanges()
    {
        // 跟随系统需实时生效：系统切换深浅色后界面应随之更新，无需重启。
        var themeManager = ReadAppSource(Path.Combine("Services", "ThemeManager.cs"));

        Assert.Contains(
            "SystemEvents.UserPreferenceChanged",
            themeManager,
            StringComparison.Ordinal);

        Assert.Contains(
            "StartWatchingSystemChanges",
            themeManager,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeManager_SystemEventHandler_MustMarshalToUiThread()
    {
        // SystemEvents 在系统事件线程上触发，不在 UI 线程。
        // Application.Resources 有线程亲和性，在错误线程改动会抛
        // InvalidOperationException —— 这类故障只在用户切系统主题时才出现，极难复现。
        var themeManager = ReadAppSource(Path.Combine("Services", "ThemeManager.cs"));

        Assert.Contains(
            "Dispatcher",
            themeManager,
            StringComparison.Ordinal);

        Assert.Contains(
            "HasShutdownStarted",
            themeManager,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppMustUnsubscribeSystemThemeWatcherOnExit()
    {
        // SystemEvents 是静态事件：订阅后若不解除，ThemeManager 与 Application
        // 会被长期持有而无法回收。托盘驻留场景下进程可能存活很久，泄漏持续累积（Q-007）。
        var appCode = ReadAppSource("App.xaml.cs");

        Assert.Contains("StopWatchingSystemChanges", appCode, StringComparison.Ordinal);

        // 必须位于 OnExit 内而非 OnStartup：启动时解除没有意义
        var onExitIndex = appCode.IndexOf("OnExit", StringComparison.Ordinal);
        Assert.True(onExitIndex > 0, "App.xaml.cs 中未找到 OnExit");

        var unsubscribeIndex = appCode.IndexOf("StopWatchingSystemChanges", StringComparison.Ordinal);
        Assert.True(
            unsubscribeIndex > onExitIndex,
            "StopWatchingSystemChanges 应在 OnExit 中调用，而非 OnStartup");
    }

    [Fact]
    public void ThemeManager_ResolveInitialTheme_MustReadConfigFile()
    {
        // 持久化契约：初始主题来自 config.json 的 appearance.theme，
        // 配置缺失时回落 System。原实现无条件返回 System，导致用户选择重启后丢失。
        var themeManager = ReadAppSource(Path.Combine("Services", "ThemeManager.cs"));

        Assert.Contains("LoadSavedTheme", themeManager, StringComparison.Ordinal);
        Assert.Contains("configFilePath", themeManager, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeManager_MustPersistUserSelection()
    {
        // 用户切换主题时必须写入配置，否则偏好无法跨会话保留
        var themeManager = ReadAppSource(Path.Combine("Services", "ThemeManager.cs"));

        Assert.Contains("TrySaveTheme", themeManager, StringComparison.Ordinal);
        Assert.Contains("appearance", themeManager, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeManager_SaveMustPreserveOtherSettings()
    {
        // 配置项会随设置页增多，保存主题时只能改appearance.theme，
        // 整体覆写会抹掉其他已保存的设置。
        var themeManager = ReadAppSource(Path.Combine("Services", "ThemeManager.cs"));

        Assert.Contains("UpsertAppearanceTheme", themeManager, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignSystemWindow_MustExistAsSeparateWindow()
    {
        // 2026-10-09 决策：设计系统从 MainWindow 迁到独立窗口。
        //
        // 原因：调色板与业务布局混在一处时，每次改色都要在业务结构里
        // 辨认"是设计问题还是布局问题"；两者互不干扰才能各自快速迭代。
        // 同时保留可看的核对页面 —— D-048 要求视觉产出交用户验收，
        // 没有页面可看就无法验收。
        var designSystemXaml = AppSource(Path.Combine("Views", "DesignSystemWindow.xaml"));

        Assert.True(File.Exists(designSystemXaml), "未找到 DesignSystemWindow.xaml");
        Assert.Contains("ReleaseWatcher.App.Views.DesignSystemWindow", File.ReadAllText(designSystemXaml));

        // 预览页内容必须真的搬过去了，不能是新建一个空窗口
        var content = File.ReadAllText(designSystemXaml);
        Assert.Contains("语义色", content);
        Assert.Contains("ColorSamples", content);
    }

    [Fact]
    public void MainWindow_MustUseWindowChromeForIntegratedTitleBar()
    {
        // 2026-10-09 用户决策：标题栏与窗口一体（无边框）。
        //
        // 实现必须是 WindowStyle=None + WindowChrome —— WindowChrome 通过
        // 非客户区命中测试保留边缘缩放、Aero Snap、Win+方向键贴边等系统行为；
        // 手写 MouseLeftButtonDown + DragMove 会全部失去这些能力。
        // 若未来改用别的机制，本断言应随之更新而非简单删除。
        var xaml = ReadAppSource(Path.Combine("Views", "MainWindow.xaml"));

        Assert.Contains("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowChrome", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_ThemeSelector_MustLiveInSettingsPageNotToolbar()
    {
        // 2026-10-09 用户决策：主题选择放设置页，不占主页顶部。
        // 验证方式：ThemeSelector（Tag="System" 的下拉框）必须出现在
        // SettingsPage 区域之内、且在标题栏之后 —— 字符串位置比较。
        var xaml = ReadAppSource(Path.Combine("Views", "MainWindow.xaml"));

        var settingsPageIndex = xaml.IndexOf("x:Name=\"SettingsPage\"", StringComparison.Ordinal);
        var themeSelectorIndex = xaml.IndexOf("x:Name=\"ThemeSelector\"", StringComparison.Ordinal);
        var titleBarIndex = xaml.IndexOf("标题栏", StringComparison.Ordinal);

        Assert.True(settingsPageIndex > 0, "MainWindow.xaml 中未找到 SettingsPage");
        Assert.True(themeSelectorIndex > 0, "MainWindow.xaml 中未找到 ThemeSelector —— 主题设置被移除了？");

        Assert.True(
            themeSelectorIndex > settingsPageIndex,
            "ThemeSelector 应位于 SettingsPage 区域内（主题选择属设置页，不属于主页顶部）");
    }

    [Fact]
    public void MainWindow_MustOfferEntryToDesignSystemWindow()
    {
        // 独立窗口若没有入口就等于藏起来了 —— 用户无从打开。
        var windowCode = ReadAppSource(Path.Combine("Views", "MainWindow.xaml.cs"));
        var windowXaml = ReadAppSource(Path.Combine("Views", "MainWindow.xaml"));

        Assert.Contains("OpenDesignSystem_Click", windowCode, StringComparison.Ordinal);
        Assert.Contains("DesignSystem", windowXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignSystemWindow_MustNotDuplicateThemeMetricsClass()
    {
        // ThemeMetrics 曾随文件复制被复制一份，两个文件同程序集内重复定义
        // 同类型会编译冲突（CS0101）。故只允许在 MainWindow.xaml.cs 中定义一次。
        var designSystemCode = ReadAppSource(Path.Combine("Views", "DesignSystemWindow.xaml.cs"));

        var offenders = designSystemCode
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line =>
                !line.StartsWith("//", StringComparison.Ordinal)
                && line.Contains("class ThemeMetrics", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ThemeSelector_MustPersistUserSelectionOnChange()
    {
        // 只有 Apply 而不落盘 = 持久化写了却永远存不进值，重启后仍丢失。
        // 处理器必须同时调用 TrySaveTheme。
        var windowCode = ReadAppSource(Path.Combine("Views", "MainWindow.xaml.cs"));

        Assert.Contains("TrySaveTheme", windowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeSelector_MustNotPersistProgrammaticSelection()
    {
        // 构造末尾的 SelectThemeItem 会设置 SelectedItem，从而连带触发
        // SelectionChanged。若不抑制，首次启动（用户从未选过）也会写一次配置 ——
        // 系统恰为深色时会把"跟随系统"悄悄固化成"深色"，
        // 之后系统切浅色也不再跟随，属于静默劫持用户偏好。
        var windowCode = ReadAppSource(Path.Combine("Views", "MainWindow.xaml.cs"));

        Assert.Contains("_suppressThemePersist", windowCode, StringComparison.Ordinal);

        // 抑制标志必须真的参与守卫判断，不能只是声明了却从不使用
        Assert.Contains(
            "if (!_suppressThemePersist)",
            windowCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppStartup_MustApplyThemeBeforeCreatingWindow()
    {
        // 主题必须在创建任何窗口之前定下来：控件样式在构造阶段即解析颜色，
        // 先创建窗口再切主题会导致可见的闪烁（旧色板 → 新色板）。
        //
        // 断言匹配 "ThemeManager" + ".Apply(" 两段，而不是某段具体源码 ——
        // 刻意不写成 themeManager.Apply(...)：曾用局部变量别名引用这个静态类，
        // 触发 CS0119/CS0723（别名与类型同名），且写法一变断言就失效。
        var appCode = ReadAppSource("App.xaml.cs");

        var applyIndex = appCode.IndexOf("ThemeManager", StringComparison.Ordinal);
        applyIndex = applyIndex >= 0
            ? appCode.IndexOf(".Apply(", applyIndex, StringComparison.Ordinal)
            : -1;

        var windowIndex = appCode.IndexOf(
            "Services.GetRequiredService<Views.MainWindow>", StringComparison.Ordinal);

        Assert.True(applyIndex > 0, "App.xaml.cs 中未找到主题应用调用");
        Assert.True(windowIndex > 0, "App.xaml.cs 中未找到主窗口创建调用");

        Assert.True(
            applyIndex < windowIndex,
            "主题必须在创建主窗口之前应用，否则控件会先以旧色板构造再被更新，肉眼可见闪烁");
    }

    [Fact]
    public void AppStartup_MustNotAliasStaticTypesWithSameNamedLocals()
    {
        // 防回归：曾写 var themeManager = ...ThemeManager; 随后调用 themeManager.Apply(...)，
        // C# 会把后者解析为"用变量访问静态成员"，报 CS0119 / CS0723 / CS0176。
        //
        // 该缺陷只在 App 的 XAML 临时项目（_wpftmp）上暴露，
        // dotnet test 走的是另一条编译路径，因此不会被测试环节拦住 ——
        // 靠本断言把规则固定下来。
        //
        // 只检查非注释行：本文件与 App.xaml.cs 的注释里会写明这个禁用写法作为说明，
        // 按全文匹配会误报。
        var offendingLines = ReadAppSource("App.xaml.cs")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line =>
                !line.StartsWith("//", StringComparison.Ordinal)
                && line.Contains("var themeManager =", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offendingLines);
    }
}