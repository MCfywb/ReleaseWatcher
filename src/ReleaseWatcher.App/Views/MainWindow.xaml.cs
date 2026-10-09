using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReleaseWatcher.App.Services;
using ReleaseWatcher.Application.Abstractions;

namespace ReleaseWatcher.App.Views;

/// <summary>
/// 主窗口。
/// </summary>
/// <remarks>
/// <para>
/// Phase 1 当前承载**设计系统预览页**：把 Theme 中的色板、字阶、控件
/// 三态摆在屏幕上供肉眼核对。见 XAML 内注释。
/// </para>
/// <para>
/// **约束：不要在此类中 <c>using ReleaseWatcher.Infrastructure.*</c>。**
/// 需要路径等信息时注入 <c>IAppPaths</c>。
/// 该约束由 <c>CompositionRootTests</c> 自动守护（D-037）。
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>
    /// 预览页要展示的语义色清单。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="ThemeManager"/> 逐个解析，**不在此处写死颜色值** ——
    /// 若某个键解析不到，页面上会出现"取不到色"的缺口，
    /// 这正是需要被看见的问题。
    /// </remarks>
    private static readonly string[] SemanticBrushKeys =
    [
        "Brush.Background.Primary",
        "Brush.Background.Secondary",
        "Brush.Surface",
        "Brush.Text.Primary",
        "Brush.Text.Secondary",
        "Brush.Border",
        "Brush.Accent",
        "Brush.Success",
        "Brush.Warning",
        "Brush.Error",
    ];

    /// <summary>
    /// 路径提供者。用于取 <c>config.json</c> 路径以持久化主题选择。
    /// </summary>
    /// <remarks>
    /// 保存为字段是因为构造参数不会自动成为成员 ——
    /// 而主题下拉框的 <see cref="ThemeSelector_SelectionChanged"/> 处理器
    /// 需要在构造之后很久才拿到它。
    /// </remarks>
    private readonly IAppPaths _paths;

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

        BuildColorSamples();

        // 让下拉框反映当前实际主题，而不是硬编码第 0 项。
        //
        // App.xaml.cs 在创建窗口前已应用 ResolveInitialTheme(paths.ConfigFile)，
        // 此处若固定 SelectedIndex=0，显示就会与实际配色不符
        // （系统为深色时下拉框却显示"浅色"）。
        SelectThemeItem(ThemeManager.Current);
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
    /// 逐个解析语义色并生成色板。
    /// </summary>
    /// <remarks>
    /// 解析失败时画一个红色描边的"未定义"块，而不是静默跳过 ——
    /// 缺色是设计系统的真实缺陷，静默失败会让人误以为色板已完整。
    /// </remarks>
    private void BuildColorSamples()
    {
        ColorSamples.Items.Clear();

        var panel = new WrapPanel();

        foreach (var key in SemanticBrushKeys)
        {
            panel.Children.Add(CreateSwatch(key));
        }

        ColorSamples.Items.Add(panel);
    }

    /// <summary>
    /// 生成一个色板卡片：色块 + 键名 + 十六进制值。
    /// </summary>
    private static Border CreateSwatch(string key)
    {
        var brush = ThemeManager.ResolveBrush(key);

        if (brush is SolidColorBrush solid)
        {
            return new Border
            {
                Width = 168,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(8),
                Background = solid,
                CornerRadius = new CornerRadius(6),
                ToolTip = $"{key} = {solid.Color}",
                Child = new TextBlock
                {
                    Text = $"{key}\n{solid.Color}",
                    Foreground = ThemeManager.ResolveBrush("Brush.Text.Primary"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
        }

        return new Border
        {
            Width = 168,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = ThemeManager.ResolveBrush("Brush.Error"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = $"{key} — 未定义",
                Foreground = ThemeManager.ResolveBrush("Brush.Error"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            },
        };
    }

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

            BuildColorSamples();
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