using System.Windows;
using System.Windows.Media;

namespace ReleaseWatcher.App.Services;

/// <summary>
/// 主题（浅色 / 深色）切换。
/// </summary>
/// <remarks>
/// <para>
/// <b>切换的实现方式</b>：替换 <c>Theme.xaml</c> <strong>内部</strong>的色板槽位
/// （<c>Colors.Light.xaml</c> / <c>Colors.Dark.xaml</c>），
/// 字体、间距、控件样式三层始终在位。
/// </para>
///
/// <para>
/// <b>不要改成替换 Theme.xaml 本身。</b>
/// 曾这样实现过，后果是 Typography / Spacing / Controls 三个字典随
/// Theme.xaml 一同消失，所有控件样式失效，界面退回系统默认（Aero）外观。
/// 而颜色键此时仍能正确解析（替换进去的 Colors 本身是好的），
/// 于是现象表现为"深色下 CheckBox 文字是黑的""ComboBox 下拉面板是白的" ——
/// 两种颜色各自都对，缺的只是承载样式的字典，极易误判为配色问题。
/// 详见 <c>Theme.xaml</c> 头部注释与 <c>DECISIONS.md</c> D-046。
/// </para>
///
/// <para>
/// <b>为什么必须用 DynamicResource：</b>
/// 控件样式里凡涉及颜色的都写<code>DynamicResource</code>。
/// 字典被替换后，WPF 会对已存在的元素重新求值，画面随之更新。
/// 若写成 <c>StaticResource</c>，值在解析时就固定了，切换不会生效。
/// </para>
///
/// <para>
/// <b>关于画刷别名：</b>曾尝试用"无后缀键指向带后缀键"的方式做别名，
/// 但 <c>SolidColorBrush.Color</c> 通过 DynamicResource 间接取值时，
/// 画刷自身的 <c>Opacity</c> 不会被别名继承 ——
/// hover / selected / pressed 这三类叠加层会退化成不透明色块。
/// 因此改用"整体替换色板字典"，无别名、无继承问题。
/// </para>
///
/// <para>
/// <b>命名空间陷阱：</b>本项目存在 <c>ReleaseWatcher.Application</c>
/// <strong>命名空间</strong>（对应 Application 项目），它会遮蔽
/// <c>System.Windows.Application</c>。因此凡写
/// <c>Application.Current</c> 都会报
/// <c>error CS0234: 命名空间"ReleaseWatcher.Application"中不存在类型或命名空间名"Current"</c>。
/// 必须写全名 <c>System.Windows.Application.Current</c>。
/// 同样的坑已在 <c>App.xaml.cs</c> 出现过。
/// </para>
///
/// <para>
/// <b>归属说明：</b>本类属App 层（UI 基础设施），不涉及业务，
/// 因此不放入 Application 或 Infrastructure（D-003 分层）。
/// </para>
/// </remarks>
public static class ThemeManager
{
    /// <summary>
    /// 色板槽位在 <c>Theme.xaml</c> 的 MergedDictionaries 中的下标。
    /// </summary>
    /// <remarks>
    /// 依赖 <c>Theme.xaml</c> 把 Colors.Light.xaml 放在第一个位置。
    /// 改动合并顺序时必须同步改这里，否则主题切换会替换错字典。
    /// </remarks>
    private const int ColorPaletteIndex = 0;

    private const string ThemeEntryPath =
        "/ReleaseWatcher.App;component/Resources/Theme/Theme.xaml";

    private static readonly Uri LightThemeUri =
        new("/ReleaseWatcher.App;component/Resources/Theme/Colors.Light.xaml", UriKind.Relative);

    private static readonly Uri DarkThemeUri =
        new("/ReleaseWatcher.App;component/Resources/Theme/Colors.Dark.xaml", UriKind.Relative);

    /// <summary>当前生效的主题。</summary>
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    /// <summary>
    /// 确定初始主题。
    /// </summary>
    /// <remarks>
    /// 原方案未定义深色主题，本轮补齐（见 Colors.Dark.xaml 头部说明）。
    /// <para>
    /// 当前**固定返回浅色**，不是偷懒的实现：
    /// "跟随系统"需要读取系统配色偏好，而设置项尚未实现
    /// （<c>config.json</c> 的 <c>appearance.theme</c> 属Settings 页范围）。
    /// 在此凭空引入一套系统偏好探测，会产生一个无处持久化、
    /// 下次启动就失效的行为 —— 反而更难排查。
    /// </para>
    /// <para>
    /// 实现设置页后，此处改为读 <c>config.json</c>。
    /// </para>
    /// </remarks>
    public static AppTheme ResolveInitialTheme() => AppTheme.Light;

    /// <summary>
    /// 应用指定主题。
    /// </summary>
    /// <param name="theme">目标主题。</param>
    /// <exception cref="InvalidOperationException">
    /// 应用资源中找不到 <c>Theme.xaml</c>，或其内部缺少色板槽位 ——
    /// 通常意味着 <c>App.xaml</c> 未引用它，或合并顺序被改动。
    /// </exception>
    public static void Apply(AppTheme theme)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            // 应用已退出（如托盘关闭流程中），无需切换。
            return;
        }

        var palette = ResolvePaletteSlot(app);

        palette.Source = theme == AppTheme.Dark ? DarkThemeUri : LightThemeUri;

        Current = theme;
    }

    /// <summary>
    /// 定位 <c>Theme.xaml</c> 内部的色板槽位。
    /// </summary>
    /// <remarks>
    /// 逐层向下找到 Theme.xaml 那一层，再取它的第
    /// <see cref="ColorPaletteIndex"/> 项。
    /// <para>
    /// 之所以要向下钻，而不是直接用
    /// <c>App.Resources.MergedDictionaries[0]</c>：
    /// 后者正是 Theme.xaml 本身，替换它会把其余三个字典一并带走
    /// （见类型注释中记录的真实故障）。
    /// </para>
    /// </remarks>
    private static ResourceDictionary ResolvePaletteSlot(System.Windows.Application app)
    {
        foreach (var dictionary in app.Resources.MergedDictionaries)
        {
            if (dictionary.Source?.ToString() != ThemeEntryPath)
            {
                continue;
            }

            if (dictionary.MergedDictionaries.Count > ColorPaletteIndex)
            {
                return dictionary.MergedDictionaries[ColorPaletteIndex];
            }

            throw new InvalidOperationException(
                $"Theme.xaml 的合并字典数量不足：预期至少 {ColorPaletteIndex + 1} 个，"
                + $"实际 {dictionary.MergedDictionaries.Count} 个。"
                + "请确认 Colors.Light.xaml 仍排在 Theme.xaml 的第一位。");
        }

        throw new InvalidOperationException(
            $"在 Application.Resources 中未找到 {ThemeEntryPath}。"
            + "请检查 App.xaml 是否引用了 Theme.xaml。");
    }

    /// <summary>
    /// 读取当前主题下的语义色，主要供绘制代码与测试使用。
    /// </summary>
    /// <param name="resourceKey">语义键，例如 <c>Brush.Accent</c>。</param>
    /// <returns>对应画刷；未定义时返回 <see langword="null"/>。</returns>
    public static Brush? ResolveBrush(string resourceKey)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return null;
        }

        // Application.TryFindResource 会逐层穿透 MergedDictionaries，
        // 因此无论色板在第几层都能找到。
        return app.TryFindResource(resourceKey) as Brush;
    }

    /// <summary>
    /// 列出当前已加载的设计系统字典，供诊断与测试使用。
    /// </summary>
    /// <remarks>
    /// 该方法的存在本身就说明一个教训：样式字典是否真的加载了，
    /// 不能靠"颜色对不对"推断 —— 必须能把加载清单打出来看。
    /// </remarks>
    public static IReadOnlyList<string> DescribeLoadedDictionaries()
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();

        foreach (var top in app.Resources.MergedDictionaries)
        {
            names.Add(top.Source?.ToString() ?? "(内联)");

            foreach (var inner in top.MergedDictionaries)
            {
                names.Add("  └ " + (inner.Source?.ToString() ?? "(内联)"));
            }
        }

        return names;
    }
}

/// <summary>可用主题。</summary>
public enum AppTheme
{
    /// <summary>浅色。原方案唯一定义的主题。</summary>
    Light,

    /// <summary>深色。本轮补齐，按 Apple HIG 夜间模式惯例。</summary>
    Dark,
}