using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ReleaseWatcher.App.Animation;
using ReleaseWatcher.App.Services;
using ReleaseWatcher.App.ViewModels;
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

    /// <summary>概览页 ViewModel。</summary>
    private readonly OverviewViewModel _overviewViewModel;

    /// <summary>检测列表 ViewModel（三栏选择态与响应式驱动来源）。</summary>
    private readonly RepositoryListViewModel _repositoryList;

    public MainWindow(
        IAppPaths paths,
        RepositoryListViewModel repositoryList,
        OverviewViewModel overview)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(repositoryList);
        ArgumentNullException.ThrowIfNull(overview);

        InitializeComponent();

        _paths = paths;
        _overviewViewModel = overview;
        _repositoryList = repositoryList;

        // DataContext 设为概览：概览与检测列表共享同一次检测结果，
        // 两个页面各自设DataContext 会导致它们的数据源分叉。
        // 检测列表页通过 {Binding DataContext.RepositoryList, RelativeSource=...}
        // 取用同一实例（见 MainWindow.xaml）。
        DataContext = overview;

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

        // 应用 Sidebar 初始状态（展开：宽度按设计系统、图标靠左）。
        // 注意宽度不能在 XAML 绑定：ColumnDefinition.Width 是 GridLength，
        // 而 Size.SidebarWidth 是 sys:Double，绑定会在启动时抛 XamlParseException。
        ApplySidebarState(animate: false);

        // 初始页：XAML 里 IsChecked="True" 触发的 Checked 事件发生在解析期间，
        // 已被 Nav_Checked 的 null 保护忽略，故此处在 InitializeComponent()
        // 之后显式设置一次。
        ShowPage(OverviewPage);

        // 三栏响应式 + 选择态观察：窄屏时折叠为逐级导航，宽屏时三栏并排。
        // 必须在 InitializeComponent() 之后（ReposPage 等子控件已创建）注册。
        WireReposResponsive();

        // 让设置页的主题下拉框反映当前实际主题。
        // App.xaml.cs 在创建窗口前已应用 ResolveInitialTheme(paths.ConfigFile)，
        // 此处若固定 SelectedIndex=0，显示就会与实际配色不符。
        SelectThemeItem(ThemeManager.Current);

        // 最大化 / 还原时更新标题栏按钮图标（最大化方框 ↔ 还原双框，均为矢量图形）。
        StateChanged += (_, _) => UpdateMaximizeIcon();

        // 启动即跑一轮检测，让界面一打开就有内容，
        // 而不是让用户先点一次"刷新"才知道有没有数据。
        // 用 Load 事件而非构造函数直接调用：构造函数此刻窗口尚未显示，
        // 启动一轮异步检测会让界面先闪一下空态再跳到有数据。
        Loaded += async (_, _) => await _overviewViewModel.Refresh();

        // 无边框窗口默认丢失系统原生的 最小化/最大化/还原 动画（WindowStyle=None
        // 去掉了 WS_CAPTION，DWM 据此跳过原生过渡）。窗口句柄就绪后补回相关样式位，
        // 由 WindowChrome 的 GlassFrameThickness=0 保持这些样式不可见，仅用于恢复动画。
        SourceInitialized += (_, _) => EnableNativeWindowAnimations();
    }

    // ==================== 标题栏 ====================

    /// <summary>最小化（交给系统原生 DWM 动画）。</summary>
    /// <remarks>
    /// 原生最小化动画依赖窗口具备 WS_CAPTION 样式位；无边框窗口默认没有，
    /// 由 <see cref="EnableNativeWindowAnimations"/> 在句柄就绪后补回，故此处只改状态即可。
    /// </remarks>
    private void Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    /// <summary>最大化 / 还原切换（交给系统原生 DWM 动画）。</summary>
    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    /// <summary>
    /// 关闭窗口（与 Windows 系统原生一致：瞬时移除，无任何自定义动画）。
    /// </summary>
    /// <remarks>
    /// Windows 本身没有"关闭"窗口的动画；自绘淡出反而偏离原生观感，
    /// 违反 AGENTS.md §1.5 "实现必须是 Windows 原生" 的约束。
    /// ShutdownMode 为默认 OnLastWindowClose（D-049），最后一个窗口关闭即退出进程。
    /// </remarks>
    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    /// <summary>按窗口状态切换最大化按钮图标（最大化 ↔ 还原）。</summary>
    private void UpdateMaximizeIcon()
    {
        // 图标是 Path（矢量），不是 TextBlock —— 故赋 Data（Geometry）而非 Text。
        // 赋 Text 会编译失败，因为 Path 没有 Text 属性。
        // 图形定义在本窗口的 Window.Resources 里，用 this.FindResource 取
        // （先查本窗口资源再逐级上溯；Application.Current.FindResource
        //   只查 Application 级资源，取不到窗口级资源，会返回 null）。
        MaximizeIcon.Data = (Geometry)this.FindResource(
            WindowState == WindowState.Maximized
                ? "WindowChrome.Geometry.Restore"
                : "WindowChrome.Geometry.Maximize");
    }

    // ==================== 原生窗口动画：补回边框样式位 ====================

    /// <summary>恢复无边框窗口的系统原生 最小化/最大化/还原 动画。</summary>
    /// <remarks>
    /// <para>
    /// <see cref="WindowStyle"/>=None 会移除 WS_CAPTION，DWM 据此跳过原生过渡
    /// （官方确认：borderless 窗口不播最小化/还原动画）。本方法在句柄就绪后把
    /// WS_CAPTION / WS_THICKFRAME / WS_SYSMENU 等样式位 OR 回去，DWM 便会像对待
    /// 普通窗口一样播放缩放过渡；视觉上这些样式由 <c>WindowChrome.GlassFrameThickness=0</c>
    /// 保持不可见，故标题栏外观不变（自定义工具栏保留）。
    /// </para>
    /// <para>
    /// 该做法要求窗口非透明（未设 AllowsTransparency）；本窗口满足。
    /// 参考：StackOverflow #56175722、MS Learn "No minimize/restore animation after removing WindowStyle"。
    /// </para>
    /// </remarks>
    private void EnableNativeWindowAnimations()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int style = GetWindowLong(hwnd, GWL_STYLE);
        style |= (int)(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MAXIMIZEBOX | WS_MINIMIZEBOX);
        SetWindowLong(hwnd, GWL_STYLE, style);

        // 强制非客户区重算，让新样式立即生效（不移动/不缩放/不激活）。
        SetWindowPos(
            hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private const int GWL_STYLE = -16;
    private const int SWP_FRAMECHANGED = 0x0020;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_NOACTIVATE = 0x0010;

    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_SYSMENU = 0x00080000;
    private const uint WS_MAXIMIZEBOX = 0x00010000;
    private const uint WS_MINIMIZEBOX = 0x00020000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLong64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static int GetWindowLong(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 8
            ? GetWindowLong64(hWnd, nIndex).ToInt32()
            : GetWindowLong32(hWnd, nIndex);

    private static void SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLong64(hWnd, nIndex, new IntPtr(dwNewLong));
        }
        else
        {
            SetWindowLong32(hWnd, nIndex, dwNewLong);
        }
    }

    // ==================== Sidebar ====================

    /// <summary>
    /// 展开 / 收起 Sidebar。
    /// </summary>
    /// <remarks>
    /// 收起时只显示图标（ToolTip 仍有文字提示），宽度切换到
    /// <c>Size.SidebarWidth.Collapsed</c>。
    /// 切换时播放平滑过渡（列宽用 <see cref="GridLengthAnimation"/> 补间，
    /// 文字用透明度淡入淡出），避免硬切。
    /// </remarks>
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _sidebarExpanded = !_sidebarExpanded;

        ApplySidebarState(animate: true);
    }

    /// <summary>
    /// 把 Sidebar 的当前状态落到界面。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>单一入口</b>：构造函数与汉堡点击都调它，
    /// 避免两处各写一份导致初始态与切换后不一致
    /// —— 漏设初始态是这类展开/收起功能最常见的缺陷。
    /// </para>
    /// <para>
    /// <b>图标位移的由来（2026-10-10 修复）：</b>
    /// 原先收起态同时改了「宽度 / 内边距 / 对齐」三项，
    /// 导致图标左边缘在展开态为 margin 4 + padding 16 = 20px、
    /// 收起态为 margin 4 + (40−16)/2 = 16px，切换时跳动 4px。
    /// 現改为：图标放在固定宽 48 的容器内居中，
    /// 收起时只切换文字可见性 —— 图标绝对位置因此恒定。
    /// </para>
    /// <para>
    /// <b>过渡（Request 3）：</b>列宽用 <see cref="GridLengthAnimation"/> 在
    /// 200↔48 之间补间；文字在显隐时淡入淡出，避免硬切。
    /// </para>
    /// </remarks>
    private void ApplySidebarState(bool animate = false)
    {
        var target = _sidebarExpanded
            ? ThemeMetrics.SidebarWidth
            : ThemeMetrics.SidebarCollapsedWidth;

        if (animate && SidebarColumn is not null)
        {
            var anim = new GridLengthAnimation
            {
                From = SidebarColumn.Width,
                To = new GridLength(target, GridUnitType.Pixel),
                Duration = TimeSpan.FromMilliseconds(200)
            };
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, anim);
        }
        else if (SidebarColumn is not null)
        {
            SidebarColumn.Width = new GridLength(target);
        }

        // 文字随收起隐藏、随展开显示，并配合透明度淡入淡出。
        AnimateNavText(OverviewNavText, _sidebarExpanded);
        AnimateNavText(ReposNavText, _sidebarExpanded);
        AnimateNavText(DownloadsNavText, _sidebarExpanded);
        AnimateNavText(SettingsNavText, _sidebarExpanded);

        StatusText.Text = _sidebarExpanded ? "侧边栏已展开" : "侧边栏已收起";
    }

    /// <summary>
    /// 让导航文字在显隐时淡入淡出，而不是硬切。
    /// </summary>
    private void AnimateNavText(UIElement text, bool expand)
    {
        if (expand)
        {
            text.Visibility = Visibility.Visible;
            text.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
        else
        {
            var fade = new DoubleAnimation(
                text.Opacity, 0, TimeSpan.FromMilliseconds(120));
            fade.Completed += (_, _) =>
            {
                // 仅在仍处于收起意图时才收起，避免快速来回切换误收起文字。
                if (!_sidebarExpanded)
                {
                    text.Visibility = Visibility.Collapsed;
                }
            };
            text.BeginAnimation(UIElement.OpacityProperty, fade);
        }
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

        // XAML 里 IsChecked="True" 的导航项会在 InitializeComponent() 解析期间
        // 就触发 Checked 事件，此时其余页面控件尚未创建（它们声明在导航之后）。
        // 不判断就直接切换可见性会抛 NullReferenceException ——
        // 编译期不报错，只在启动时崩，表现为"exe 打不开"。
        // 初始页由构造函数在 InitializeComponent() 之后显式设置。
        if (OverviewPage is null
            || ReposPage is null
            || DownloadsPage is null
            || SettingsPage is null)
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

    /// <summary>只显示指定页，其余隐藏，并对新页面做淡入过渡（Request 3）。</summary>
    private void ShowPage(UIElement page)
    {
        OverviewPage.Visibility = page == OverviewPage ? Visibility.Visible : Visibility.Collapsed;
        ReposPage.Visibility = page == ReposPage ? Visibility.Visible : Visibility.Collapsed;
        DownloadsPage.Visibility = page == DownloadsPage ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == SettingsPage ? Visibility.Visible : Visibility.Collapsed;

        // 新页面从 0 淡入到 1，给出页面切换的轻量过渡（Windows / Fluent 风格）。
        page.Opacity = 0;
        page.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));

        // 切换到检测列表页时，按当前窗口宽度重算三栏布局
        // （页签由隐藏转可见不会自动触发 SizeChanged）。
        if (page == ReposPage)
        {
            UpdatePanes();
        }
    }

    // ==================== 检测列表：三栏响应式 ====================

    /// <summary>
    /// 窄屏阈值（检测列表内容区宽度，单位与 <see cref="FrameworkElement.ActualWidth"/> 一致）。
    /// </summary>
    /// <remarks>
    /// 低于此宽度时三栏改为逐级导航（仓库 → 历史 → 详情），
    /// 符合设计文档 §1105 的 Split View 适应性约定。
    /// </remarks>
    private const double ReposNarrowThreshold = 960;

    /// <summary>
    /// 注册三栏的响应式与选择态监听。
    /// </summary>
    private void WireReposResponsive()
    {
        // 窗口/页面尺寸变化 → 重算宽窄模式。
        ReposPage.SizeChanged += (_, _) => UpdatePanes();

        // 选择态或可见性变化 → 窄屏下切换可见面板、宽屏下保持三栏。
        _repositoryList.PropertyChanged += OnRepositoryListPropertyChanged;
    }

    /// <summary>
    /// 监听检测列表 ViewModel 的选择与可见性变化，同步三栏面板。
    /// </summary>
    private void OnRepositoryListPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RepositoryListViewModel.SelectedRow)
            or nameof(RepositoryListViewModel.SelectedRelease)
            or nameof(RepositoryListViewModel.ListVisibility)
            or nameof(RepositoryListViewModel.EmptyStateVisibility))
        {
            UpdatePanes();
        }
    }

    /// <summary>
    /// 根据当前宽度与选择态，切换三栏的可见性与列宽。
    /// </summary>
    /// <remarks>
    /// 宽屏：三栏并排，返回按钮隐藏。
    /// 窄屏：只显示一级 —— 有选中 Release 看详情、否则有选中仓库看历史、否则看列表；
    /// 返回按钮在"非列表级"时显示，用于逐级回退。
    /// </remarks>
    private void UpdatePanes()
    {
        if (ReposSplit is null || MasterCol is null || HistoryCol is null || DetailCol is null
            || MasterPane is null || HistoryPane is null || DetailPane is null
            || ReposBackButton is null || HistorySep is null || DetailSep is null)
        {
            return;
        }

        var narrow = ReposPage.ActualWidth > 0 && ReposPage.ActualWidth < ReposNarrowThreshold;

        if (!narrow)
        {
            MasterCol.Width = new GridLength(1.1, GridUnitType.Star);
            HistoryCol.Width = new GridLength(1.4, GridUnitType.Star);
            DetailCol.Width = new GridLength(1.5, GridUnitType.Star);
            MasterPane.Visibility = Visibility.Visible;
            HistoryPane.Visibility = Visibility.Visible;
            DetailPane.Visibility = Visibility.Visible;
            HistorySep.Visibility = Visibility.Visible;
            DetailSep.Visibility = Visibility.Visible;
            ReposBackButton.Visibility = Visibility.Collapsed;
            return;
        }

        var showDetail = _repositoryList.SelectedRelease is not null;
        var showHistory = !showDetail && _repositoryList.SelectedRow is not null;
        var showMaster = !showDetail && !showHistory;

        MasterCol.Width = new GridLength(showMaster ? 1 : 0, GridUnitType.Star);
        HistoryCol.Width = new GridLength(showHistory ? 1 : 0, GridUnitType.Star);
        DetailCol.Width = new GridLength(showDetail ? 1 : 0, GridUnitType.Star);

        MasterPane.Visibility = showMaster ? Visibility.Visible : Visibility.Collapsed;
        HistoryPane.Visibility = showHistory ? Visibility.Visible : Visibility.Collapsed;
        DetailPane.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;

        // 窄屏下非活动列宽为 0，两个分隔条统一隐藏，避免残留竖线。
        HistorySep.Visibility = Visibility.Collapsed;
        DetailSep.Visibility = Visibility.Collapsed;

        // 返回按钮仅在"非列表级"出现：详情级回退到历史、历史级回退到列表。
        ReposBackButton.Visibility = (showDetail || showHistory) ? Visibility.Visible : Visibility.Collapsed;
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
    // 2026-10-11 用户要求：启动窗口为正方形（1000 × 1000）。
    // 回退值与 Spacing.xaml 的键保持一致，仅在该字典未合并时兜底。
    public static double WindowWidth => Read("Size.WindowWidth", 1000);

    public static double WindowHeight => Read("Size.WindowHeight", 1000);

    public static double WindowMinWidth => Read("Size.WindowMinWidth", 800);

    public static double WindowMinHeight => Read("Size.WindowMinHeight", 640);

    /// <summary>Sidebar 展开宽度。</summary>
    public static double SidebarWidth => Read("Size.SidebarWidth", 220);

    /// <summary>Sidebar 收起宽度（仅图标）。</summary>
    public static double SidebarCollapsedWidth => Read("Size.SidebarWidth.Collapsed", 52);

    /// <summary>
    /// 导航项内边距（展开态）。收起态由代码置零，见 <c>ApplySidebarState</c>。
    /// </summary>
    public static Thickness NavItemPadding
        => ReadThickness("Space.Padding.Horizontal.M", new Thickness(16, 0, 16, 0));

    /// <summary>
    /// 导航图标与文字之间的间距。收起态由代码置零，否则图标会偏离中心。
    /// </summary>
    public static Thickness NavIconGap
        => ReadThickness("Size.NavIconGap", new Thickness(0, 0, 16, 0));

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

    private static Thickness ReadThickness(string key, Thickness fallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is Thickness value)
        {
            return value;
        }

        return fallback;
    }
}
