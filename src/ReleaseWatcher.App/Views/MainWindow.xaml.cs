using System.Windows;
using System.Windows.Controls;
using ReleaseWatcher.App.Services;
using ReleaseWatcher.Application.Abstractions;

namespace ReleaseWatcher.App.Views;

/// <summary>
/// 应用主页 —— 一体化标题栏 + 可收起 Sidebar。
/// </summary>
/// <remarks>
/// <para>
/// 2026-10-09 按用户提供的截图重构布局：
/// <list type="bullet">
/// <item>无边框窗口（WindowChrome），标题栏与窗口一体：应用名 + 汉堡 + 窗口控制。</item>
/// <item>Sidebar 可展开/收起，收起时只显示图标；设置项固定底部。</item>
/// <item>主题选择移至「设置」页，不在主页。</item>
/// </list>
/// </para>
/// <para>
/// <b>WindowChrome 而非手写 DragMove：</b>WindowChrome 通过非客户区命中测试
/// 保留边缘缩放、Win+方向键贴边、Aero Snap 等系统行为；
/// <c>MouseLeftButtonDown + DragMove()</c> 的老办法会全部失去这些能力，
/// 且拖动时窗口最大化状态还原等细节都要自己补 —— 不走这条路。
/// </para>
/// <para>
/// <b>约束：不要在此类中 <c>using ReleaseWatcher.Infrastructure.*</c>。</b>
/// 需要路径等信息时注入 <c>IAppPaths</c>。
/// 该约束由 <c>CompositionRootTests</c> 自动守护（D-037）。
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>
    /// 路径提供者。用于取 <c>config.json</c> 路径以持久化主题选择。
    /// </summary>
    private readonly IAppPaths _paths;

    /// <summary>Sidebar 当前是否展开。</summary>
    private bool _sidebarExpanded = true;

    /// <summary>
    /// 设计系统窗口的当前实例。
    /// </summary>
    /// <remarks>
    /// 持有引用是为了避免重复点击按钮时开出多个窗口。
    /// 窗口关闭后置 <c>null</c>，下次点击才重新创建。
    /// </remarks>
    private DesignSystemWindow? _designSystemWindow;

    public MainWindow(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        InitializeComponent();

        _paths = paths;

        // Taskbar / Alt+Tab 仍需要窗口标题；标题栏上显示的应用名由 XAML 承担。
        Title = "ReleaseWatcher";

        // 尺寸取自设计系统（Spacing.xaml），与原方案第 4.1 节一致。
        Width = ThemeMetrics.WindowWidth;
        Height = ThemeMetrics.WindowHeight;
        MinWidth = ThemeMetrics.WindowMinWidth;
        MinHeight = ThemeMetrics.WindowMinHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // 设置页「关于」区展示数据目录，替代原标题栏里的路径
        // （标题栏现在只放应用名）。
        AboutPathsText.Text = paths.AppDirectory;

        // 让设置页的主题下拉框反映当前实际主题。
        // App.xaml.cs 在创建窗口前已应用 ResolveInitialTheme(paths.ConfigFile)，
        // 此处若固定 SelectedIndex=0，显示就会与实际配色不符。
        SelectThemeItem(ThemeManager.Current);

        // 最大化 / 还原时更新标题栏按钮图标（E922 最大化 / E923 还原）。
        StateChanged += (_, _) => UpdateMaximizeIcon();
    }

    // ==================== 标题栏 ====================

    /// <summary>最小化。</summary>
    private void Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    /// <summary>最大化 / 还原切换。</summary>
    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    /// <summary>
    /// 关闭窗口。
    /// </summary>
    /// <remarks>
    /// ShutdownMode 为默认 OnLastWindowClose（D-049），
    /// 最后一个窗口关闭即退出进程，无需额外处理。
    /// </remarks>
    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    /// <summary>按窗口状态切换最大化按钮图标（最大化 ↔ 还原）。</summary>
    private void UpdateMaximizeIcon()
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    // ==================== Sidebar ====================

    /// <summary>
    /// 展开 / 收起 Sidebar。
    /// </summary>
    /// <remarks>
    /// 收起时只显示图标（ToolTip 仍有文字提示），宽度切换到
    /// <c>Size.SidebarWidth.Collapsed</c>。
    /// 刻意不做动画：宽度动画涉及计时器与布局抖动，
    /// 等布局稳定后再考虑（先要"能用"，再要"好看"）。
    /// </remarks>
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _sidebarExpanded = !_sidebarExpanded;

        SidebarColumn.Width = new GridLength(_sidebarExpanded
            ? ThemeMetrics.SidebarWidth
            : ThemeMetrics.SidebarCollapsedWidth);

        // 文字随收起隐藏；图标保留。ToolTip 继续提供文字提示。
        var textVisibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
        OverviewNavText.Visibility = textVisibility;
        ReposNavText.Visibility = textVisibility;
        DownloadsNavText.Visibility = textVisibility;
        SettingsNavText.Visibility = textVisibility;

        StatusText.Text = _sidebarExpanded ? "侧边栏已展开" : "侧边栏已收起";
    }

    /// <summary>
    /// 导航切换：按 Tag 显示对应页面。
    /// </summary>
    /// <remarks>
    /// 页面是 MainWindow 内的四个 Grid，切换可见性而非导航框架 ——
    /// 四页内容都很轻，无需 Frame/Page 的历史栈机制。
    /// Tag 决定目标页，避免靠控件名硬编码分支顺序。
    /// </remarks>
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton nav || nav.Tag is not string tag)
        {
            return;
        }

        ShowPage(tag switch
        {
            "Overview" => OverviewPage,
            "Repos" => ReposPage,
            "Downloads" => DownloadsPage,
            "Settings" => SettingsPage,
            _ => OverviewPage,
        });
    }

    /// <summary>只显示指定页，其余隐藏。</summary>
    private void ShowPage(UIElement page)
    {
        OverviewPage.Visibility = page == OverviewPage ? Visibility.Visible : Visibility.Collapsed;
        ReposPage.Visibility = page == ReposPage ? Visibility.Visible : Visibility.Collapsed;
        DownloadsPage.Visibility = page == DownloadsPage ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == SettingsPage ? Visibility.Visible : Visibility.Collapsed;
    }

    // ==================== 设置页 ====================

    /// <summary>
    /// 打开设计系统手册窗口。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="Show()"/> 而非 <see cref="ShowDialog()"/>：
    /// 手册需要与主页并排对照，模态对话框会挡住另一边。
    /// </remarks>
    private void OpenDesignSystem_Click(object sender, RoutedEventArgs e)
    {
        if (_designSystemWindow is not null && _designSystemWindow.IsVisible)
        {
            // 已开则前置，不重复创建
            _designSystemWindow.Activate();
            return;
        }

        _designSystemWindow = new DesignSystemWindow(_paths);
        _designSystemWindow.Closed += (_, _) => _designSystemWindow = null;

        _designSystemWindow.Show();
        _designSystemWindow.Activate();
    }

    /// <summary>
    /// 把主题下拉框选中项对齐到指定主题。
    /// </summary>
    /// <remarks>
    /// 用 Tag 匹配而非索引 —— 下拉框项的顺序属于展示细节，
    /// 用索引绑定会在调整顺序后静默错位。
    /// <para>
    /// <b>期间会触发 <see cref="_suppressThemePersist"/>：</b>
    /// 设置 <c>SelectedItem</c> 会连带触发 <c>SelectionChanged</c>，
    /// 而本方法在构造函数末尾被调用。此时若不抑制，
    /// 首次启动（用户从未选过主题）也会写一次 config.json ——
    /// 系统恰为深色时就会把"跟随系统"悄悄固化成"深色"。
    /// </para>
    /// </remarks>
    private void SelectThemeItem(AppTheme theme)
    {
        _suppressThemePersist = true;

        try
        {
            foreach (var item in ThemeSelector.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is string tag
                    && Enum.TryParse<AppTheme>(tag, out var parsed)
                    && parsed == theme)
                {
                    ThemeSelector.SelectedItem = item;
                    return;
                }
            }
        }
        finally
        {
            _suppressThemePersist = false;
        }
    }

    /// <summary>
    /// 抑制切换处理器中的配置写入。
    /// </summary>
    /// <remarks>
    /// 用于程序化对齐下拉框（<see cref="SelectThemeItem"/>）的场景：
    /// 那不是用户的选择，不应被当成偏好记录。
    /// </remarks>
    private bool _suppressThemePersist;

    /// <summary>
    /// 主题切换：立即应用并持久化。
    /// </summary>
    /// <remarks>
    /// 切换主题会让所有 <c>DynamicResource</c> 重新求值，
    /// 因此界面各处颜色自动跟随，无需手动刷新任何控件。
    /// </remarks>
    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSelector.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && Enum.TryParse<AppTheme>(tag, out var theme))
        {
            ThemeManager.Apply(theme);

            // 立即生效之外还要能跨会话保留 —— 否则用户的选择重启后就丢。
            // 写入失败（磁盘只读等）不影响本次切换，TrySaveTheme 内部已吞掉异常。
            //
            // 程序化对齐下拉框时跳过：那不是用户的选择。
            if (!_suppressThemePersist)
            {
                ThemeManager.TrySaveTheme(_paths.ConfigFile, theme);
            }
        }
    }
}

/// <summary>
/// 从 Theme 资源中读取的窗口尺寸常量。
/// </summary>
/// <remarks>
/// Spacing.xaml 中的 <c>Size.*</c> 是 <c>x:Double</c> 资源键，
/// 在代码中通过 <c>Application.Current.TryFindResource</c> 读取。
/// 这样窗口尺寸与设计系统保持单一来源，不在代码里重写一遍数字。
/// </remarks>
internal static class ThemeMetrics
{
    public static double WindowWidth => Read("Size.WindowWidth", 1440);

    public static double WindowHeight => Read("Size.WindowHeight", 900);

    public static double WindowMinWidth => Read("Size.WindowMinWidth", 1200);

    public static double WindowMinHeight => Read("Size.WindowMinHeight", 760);

    /// <summary>Sidebar 展开宽度。</summary>
    public static double SidebarWidth => Read("Size.SidebarWidth", 220);

    /// <summary>Sidebar 收起宽度（仅图标）。</summary>
    public static double SidebarCollapsedWidth => Read("Size.SidebarWidth.Collapsed", 52);

    private static double Read(string key, double fallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is double value)
        {
            return value;
        }

        // 读不到说明 Spacing.xaml 未被合并 —— 这本身是配置错误，
        // 但不应让应用崩溃，回退到原方案给的数值。
        return fallback;
    }
}
