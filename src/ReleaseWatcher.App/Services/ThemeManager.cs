using System.Windows;
using System.Windows.Media;

namespace ReleaseWatcher.App.Services;

/// <summary>
/// 主题（浅色 / 深色）切换。
/// </summary>
/// <remarks>
/// <para>
/// <b>切换的实现方式</b>：替换 <see cref="Application.Resources"/> 中的
/// 颜色字典（<c>Colors.Light.xaml</c> / <c>Colors.Dark.xaml</c>），
/// 其余字典（字体、间距、控件样式）保持不变。
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
/// <b>为什么不用单个字典 + 两套 key：</b>
/// 曾尝试用「无后缀键指向 .Light 色值」的方式做别名，
/// 但 <c>SolidColorBrush.Color</c> 通过 DynamicResource 间接取值时，
/// 画刷自身的 <c>Opacity</c> 不会被别名继承 ——
/// hover / selected / pressed 这三类叠加层会退化成不透明色块。
/// 整体替换字典没有这个问题，故采用此方案。
/// </para>
///
/// <para>
/// <b>命名空间陷阱：</b>本项目存在 <c>ReleaseWatcher.Application</c>
/// <strong>命名空间</strong>（对应 Application 项目），它会遮蔽
/// <c>System.Windows.Application</c>。因此凡写
/// <c>Application.Current</c> 都会报
/// <c>error CS0234: 命名空间"ReleaseWatcher.Application"中不存在类型或命名空间名"Current"</c>。
/// 必须写全名 <c>Application.Current</c>。
/// 同样的坑已在 <c>App.xaml.cs</c> 出现过一次。
/// </para>
///
/// <para>
/// <b>归属说明：</b>本类属App 层（UI 基础设施），不涉及业务，
/// 因此不放入 Application 或 Infrastructure（D-003 分层）。
/// </para>
/// </remarks>
public static class ThemeManager
{
    /// <summary>颜色字典在 <c>Resources.MergedDictionaries</c> 中的下标。</summary>
    /// <remarks>
    /// 依赖顺序：<c>Theme.xaml</c> 把 Colors 放在第一个位置。
    /// 改动 <c>Theme.xaml</c> 的合并顺序时必须同步改这里。
    /// </remarks>
    private const int ColorDictionaryIndex = 0;

    /// <summary>浅色主题字典的 pack URI。</summary>
    private static readonly Uri LightThemeUri =
        new("/ReleaseWatcher.App;component/Resources/Theme/Colors.Light.xaml", UriKind.Relative);

    /// <summary>深色主题字典的 pack URI。</summary>
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
    /// 实现设置页后，此处改为读 <c>config.json</c>，
    /// 并在本轮确定的值之前仍以浅色作为缺省。
    /// </para>
    /// </remarks>
    public static AppTheme ResolveInitialTheme() => AppTheme.Light;

    /// <summary>
    /// 应用指定主题。
    /// </summary>
    /// <param name="theme">目标主题。</param>
    /// <exception cref="InvalidOperationException">
    /// 应用资源中找不到颜色字典 —— 通常意味着 <c>Theme.xaml</c> 的合并顺序被改动，
    /// 或 <c>App.xaml</c> 未引用它。
    /// </exception>
    public static void Apply(AppTheme theme)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            // 应用已退出（如托盘关闭流程中），无需切换。
            return;
        }

        var merged = app.Resources.MergedDictionaries;

        if (merged.Count <= ColorDictionaryIndex)
        {
            throw new InvalidOperationException(
                $"资源字典数量不足：预期至少 {ColorDictionaryIndex + 1} 个，实际 {merged.Count} 个。"
                + "请检查 App.xaml 是否引用了 Theme.xaml，以及 Theme.xaml 的合并顺序。");
        }

        merged[ColorDictionaryIndex] = new ResourceDictionary { Source = theme == AppTheme.Dark ? DarkThemeUri : LightThemeUri };

        Current = theme;
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

        // 逐层查找：Theme.xaml 把颜色并入了 App.Resources，
        // 因此 Application.TryFindResource 能穿透 MergedDictionaries。
        return app.TryFindResource(resourceKey) as Brush;
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