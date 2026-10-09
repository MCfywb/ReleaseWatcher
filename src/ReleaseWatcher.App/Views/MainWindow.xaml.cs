using System.Windows;
using System.Windows.Controls;
using ReleaseWatcher.App.Services;
using ReleaseWatcher.Application.Abstractions;

namespace ReleaseWatcher.App.Views;

/// <summary>
/// 应用主页。
/// </summary>
/// <remarks>
/// <para>
/// 2026-10-09 起本窗承载应用主界面；原设计系统预览内容已整体迁至
/// <see cref="DesignSystemWindow"/>（独立窗口，由 Toolbar 的「设计系统」按钮打开）。
/// </para>
/// <para>
/// <b>当前状态：骨架已就位，内容待接。</b>
/// 应用层检测用例（<c>ReleaseDetectionService</c>）已实现，
/// 但界面尚未绑定任何数据 —— 本次刻意不做 UI，只把结构立起来。
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
    /// <remarks>
    /// 保存为字段是因为构造参数不会自动成为成员 ——
    /// 而主题下拉框的 <see cref="ThemeSelector_SelectionChanged"/> 处理器
    /// 需要在构造之后很久才拿到它。
    /// </remarks>
    private readonly IAppPaths _paths;

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

        // 标题中显示路径，用于验证 IAppPaths 已正确注入。
        // 真实标题将随 i18n 落地后改为资源引用。
        Title = $"ReleaseWatcher — {paths.AppDirectory}";

        // 尺寸取自设计系统（Spacing.xaml），与原方案第 4.1 节一致。
        Width = ThemeMetrics.WindowWidth;
        Height = ThemeMetrics.WindowHeight;
        MinWidth = ThemeMetrics.WindowMinWidth;
        MinHeight = ThemeMetrics.WindowMinHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // 让下拉框反映当前实际主题，而不是硬编码第 0 项。
        //
        // App.xaml.cs 在创建窗口前已应用 ResolveInitialTheme(paths.ConfigFile)，
        // 此处若固定 SelectedIndex=0，显示就会与实际配色不符
        // （系统为深色时下拉框却显示"浅色"）。
        SelectThemeItem(ThemeManager.Current);
    }

    /// <summary>
    /// 打开设计系统手册窗口。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="Show()"/> 而非 <see cref="ShowDialog()"/>：
    /// 手册需要与主页并排对照，模态对话框会挡住另一边。
    /// 单例复用已存在的实例，避免反复点击开出一堆窗口。
    /// </remarks>
    private void OpenDesignSystem_Click(object sender, RoutedEventArgs e)
    {
        if (_designSystemWindow is not null)
        {
            // 已开则前置，不重复创建
            if (_designSystemWindow.IsVisible)
            {
                _designSystemWindow.Activate();
                return;
            }
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
