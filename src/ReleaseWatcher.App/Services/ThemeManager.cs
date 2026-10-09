using System.IO;
using System.Windows;
using System.Windows.Media;

using Microsoft.Win32;

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

    /// <summary>读取系统当前的深浅色偏好。</summary>
    /// <remarks>
    /// 数据来源是 <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize</c>
    /// 的 <c>AppsUseLightTheme</c>（DWORD）：
    /// <list type="bullet">
    /// <item>1 = 浅色（默认 / 键缺失）</item>
    /// <item>0 = 深色</item>
    /// </list>
    /// <para>
    /// 选它的原因：这是 Windows 10/11「设置 → 个性化 → 应用模式」
    /// 真正写入的位置，与用户在那里看到的选项是同一个值。
    /// 读注册表比调<code>SystemParameters</code> 更直接 ——
    /// 后者反映的是系统 UI 主题，不一定与应用模式一致。
    /// </para>
    /// <para>
    /// 读取失败（组策略锁定、非标准环境）时**退回浅色**而不是抛异常：
    /// 主题选择属偏好而非核心功能，不该导致应用无法启动。
    /// </para>
    /// </remarks>
    public static AppTheme ReadSystemTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            // 键或值缺失时 Windows 使用浅色，这是系统自身的行为
            if (key?.GetValue("AppsUseLightTheme") is int lightTheme)
            {
                return lightTheme != 0 ? AppTheme.Light : AppTheme.Dark;
            }

            return AppTheme.Light;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // 注册表不可读 —— 用缺省值继续，不让主题偏好成为启动障碍
            return AppTheme.Light;
        }
    }

    /// <summary>
    /// 从 <c>config.json</c> 读取用户保存的主题选择。
    /// </summary>
    /// <param name="configFilePath">
    /// 配置文件完整路径；为 <c>null</c> 或空时视为无可用配置。
    /// </param>
    /// <returns>
    /// 配置中保存的主题；配置不存在、无法解析或取值非法时返回 <see langword="null"/>
    /// ，由调用方决定回落策略。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>为什么单独读而不读整个配置对象：</b>设置页尚未实现，
    /// <c>config.json</c> 目前还没有正式的结构定义。此处只做最小解析 ——
    /// 读一个键、容忍格式不符、绝不抛异常。等到设置页落地、
    /// 配置结构稳定后，再统一收敛到配置模块（届时会引入正式的
    /// 反序列化类型，并在此处改为委托调用）。
    /// </para>
    /// <para>
    /// <b>刻意手写极简 JSON 提取而非引入反序列化库：</b>为一个键引入依赖不划算，
    /// 且 App 层多一个包就多一处版本冲突来源。本方法只认
    /// <c>"appearance": { "theme": "..." }</c> 这一种形状，
    /// 遇到任何其他情况都当作"未配置"处理。
    /// </para>
    /// <para>
    /// <b>失败一律返回 null 而不抛异常：</b>配置文件损坏不应导致应用无法启动。
    /// </para>
    /// </remarks>
    public static AppTheme? LoadSavedTheme(string? configFilePath)
    {
        if (string.IsNullOrWhiteSpace(configFilePath) || !File.Exists(configFilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(configFilePath);

            // 定位 appearance 段内的 theme 值：容忍空白与任意换行
            var appearanceIndex = json.IndexOf("\"appearance\"", StringComparison.Ordinal);
            if (appearanceIndex < 0)
            {
                return null;
            }

            var themeIndex = json.IndexOf("\"theme\"", appearanceIndex, StringComparison.Ordinal);
            if (themeIndex < 0)
            {
                return null;
            }

            var colonIndex = json.IndexOf(':', themeIndex);
            if (colonIndex < 0)
            {
                return null;
            }

            var quoteStart = json.IndexOf('"', colonIndex + 1);
            if (quoteStart < 0)
            {
                return null;
            }

            var quoteEnd = json.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0)
            {
                return null;
            }

            var value = json[(quoteStart + 1)..quoteEnd];

            return Enum.TryParse(value, ignoreCase: true, out AppTheme parsed) ? parsed : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 读不了就用默认值，不让偏好设置成为启动障碍
            return null;
        }
    }

    /// <summary>
    /// 把用户选择的主题写入 <c>config.json</c> 的 <c>appearance.theme</c>。
    /// </summary>
    /// <param name="configFilePath">配置文件完整路径；无效时静默跳过。</param>
    /// <param name="theme">要保存的主题。</param>
    /// <returns>是否写入成功。</returns>
    /// <remarks>
    /// <para>
    /// 文件不存在时创建，内容形如
    /// <c>{ "appearance": { "theme": "Dark" } }</c>。
    /// </para>
    /// <para>
    /// <b>保留既有配置：</b>配置项会随设置页逐步增多，
    /// 因此若文件已存在，只替换 <c>appearance.theme</c> 的值而不整体覆写，
    /// 避免把其他已保存的设置抹掉。
    /// </para>
    /// <para>
    /// <b>写入失败不抛异常：</b>主题切换对用户而言应当"立即生效"，
    /// 至于能否记住是次要问题。不让它因磁盘只读或权限问题而中断切换。
    /// </para>
    /// </remarks>
    public static bool TrySaveTheme(string? configFilePath, AppTheme theme)
    {
        if (string.IsNullOrWhiteSpace(configFilePath))
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(configFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = File.Exists(configFilePath)
                ? File.ReadAllText(configFilePath)
                : "{}";

            json = UpsertAppearanceTheme(json, theme.ToString());

            File.WriteAllText(configFilePath, json);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 在已有 JSON 文本中写入 <c>appearance.theme</c>，保留其余内容。
    /// </summary>
    private static string UpsertAppearanceTheme(string json, string themeValue)
    {
        var appearanceIndex = json.IndexOf("\"appearance\"", StringComparison.Ordinal);
        if (appearanceIndex < 0)
        {
            return "{\n  \"appearance\": {\n    \"theme\": \"" + themeValue + "\"\n  }\n}";
        }

        var themeIndex = json.IndexOf("\"theme\"", appearanceIndex, StringComparison.Ordinal);
        if (themeIndex < 0)
        {
            // 有 appearance 段但没有 theme 键：在该段内补一个
            var braceIndex = json.IndexOf('{', appearanceIndex);
            if (braceIndex < 0)
            {
                return json;
            }

            var insertAt = braceIndex + 1;
            return json.Insert(insertAt, $"\n    \"theme\": \"{themeValue}\",");
        }

        // 已有 theme 键：替换它的值
        var colonIndex = json.IndexOf(':', themeIndex);
        var quoteStart = colonIndex >= 0 ? json.IndexOf('"', colonIndex + 1) : -1;
        var quoteEnd = quoteStart >= 0 ? json.IndexOf('"', quoteStart + 1) : -1;

        if (colonIndex < 0 || quoteStart < 0 || quoteEnd < 0)
        {
            return json;
        }

        return json[..(quoteStart + 1)] + themeValue + json[quoteEnd..];
    }

    /// <summary>
    /// 确定初始主题。
    /// </summary>
    /// <param name="configFilePath">
    /// 配置文件路径。调用方从 <c>IAppPaths.ConfigFile</c> 取得；
    /// 传 <c>null</c> 表示尚不可用（此时纯跟随系统）。
    /// </param>
    /// <returns>
    /// 配置中保存的主题；若未保存过则返回 <see cref="AppTheme.System"/>。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 优先级：<b>用户显式选择 &gt; 跟随系统</b>。
    /// 这是最不需要用户决策的缺省值 ——
    /// 从未设置过的用户应当自动获得与系统一致的外观。
    /// </para>
    /// <para>
    /// 原实现无条件返回 <see cref="AppTheme.System"/>，导致用户在下拉框
    /// 选的主题重启后丢失。现在改为先读配置。
    /// </para>
    /// </remarks>
    public static AppTheme ResolveInitialTheme(string? configFilePath = null)
        => LoadSavedTheme(configFilePath) ?? AppTheme.System;

    /// <summary>
    /// 应用指定主题。
    /// </summary>
    /// <param name="theme">目标主题；传 <see cref="AppTheme.System"/> 会解析为系统当前值。</param>
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

        // System 不对应具体色板，须先解析成实际取值
        var resolved = theme == AppTheme.System ? ReadSystemTheme() : theme;

        palette.Source = resolved == AppTheme.Dark ? DarkThemeUri : LightThemeUri;

        // 记下实际生效的颜色，供跟随系统时判断"是否真的变了"
        _lastAppliedResolvedTheme = resolved;

        Current = theme;
    }

    /// <summary>
    /// 当前实际生效的深浅色（把 <see cref="AppTheme.System"/> 解析为具体值）。
    /// </summary>
    /// <remarks>
    /// "用户选了什么"与"现在是什么颜色"是两个不同的问题：
    /// 前者是 <see cref="Current"/>，后者是本方法的返回值。
    /// 界面显示状态时应问"现在是什么颜色"。
    /// </remarks>
    public static AppTheme Effective => Current == AppTheme.System ? ReadSystemTheme() : Current;

    /// <summary>用户主题选择发生变化时触发。参数为变化后的主题。</summary>
    public static event Action<AppTheme>? ThemeChanged;

    /// <summary>
    /// 开始监听系统深浅色切换，使 <see cref="AppTheme.System"/> 实时生效。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>监听机制的选择</b>：使用
    /// <see cref="SystemEvents.UserPreferenceChanged"/>（<c>UserPreferenceCategory.General</c>）。
    /// 这是 Windows 在用户改动「设置 → 个性化 → 应用模式」时发出的系统级通知，
    /// 无需轮询、无需 WMI、无需计时器。对比其他方案：
    /// </para>
    /// <list type="bullet">
    /// <item>轮询注册表：简单但要么有延迟要么空耗 CPU，且生命周期问题依旧。</item>
    /// <item>WMI <c>RegistryValueChangeEvent</c>：可行，但需引入 <c>System.Management</c> 依赖、
    /// 在 .NET 10 上行为不够稳定，且部分环境会失败。</item>
    /// <item><see cref="SystemEvents"/>：系统提供的标准通知机制，
    /// 无额外依赖，是本项目的选择。</item>
    /// </list>
    ///
    /// <para>
    /// <b>线程问题（本方法最容易出错的地方）：</b>
    /// <see cref="SystemEvents"/> 在专用的系统事件线程上触发该事件，
    /// <b>不在 UI 线程</b>。而 <see cref="Apply"/> 会改动
    /// <c>Application.Resources</c> —— 该对象有线程亲和性，
    /// 在非 UI 线程改动会抛 <c>InvalidOperationException</c>。
    /// 因此必须 <see cref="SystemDispatcher.Invoke"/> 回 UI 线程执行。
    /// </para>
    ///
    /// <para>
    /// <b>泄漏防护（Q-007 关注点）：</b><see cref="SystemEvents"/> 是<b>静态事件</b>，
    /// 订阅它会让本对象被长期持有，阻止垃圾回收。因此：
    /// <list type="number">
    /// <item>只订阅一次（重复调用本方法不重复订阅），
    /// 用 <see cref="_watchingSystemChanges"/> 标记；</item>
    /// <item>提供 <see cref="StopWatchingSystemChanges"/>，
    /// 供应用退出时解除订阅；</item>
    /// <item>处理器是静态方法，不捕获实例状态，避免意外的引用链。</item>
    /// </list>
    /// 托盘驻留场景下应用可能长期存活，退出时务必调用解除订阅
    /// （见 <c>App.OnExit</c>）。
    /// </para>
    /// </summary>
    public static void StartWatchingSystemChanges()
    {
        if (_watchingSystemChanges)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        _watchingSystemChanges = true;
    }

    /// <summary>
    /// 停止监听系统主题变更，解除对 <see cref="SystemEvents"/> 静态事件的订阅。
    /// </summary>
    /// <remarks>
    /// 应用退出时必须调用，否则静态事件会一直持有订阅者，
    /// 使本类型（及其引用的 <c>Application</c>）无法被回收。
    /// </remarks>
    public static void StopWatchingSystemChanges()
    {
        if (!_watchingSystemChanges)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        _watchingSystemChanges = false;
    }

    /// <summary>是否正在监听系统主题变更。仅供诊断与测试读取。</summary>
    public static bool IsWatchingSystemChanges => _watchingSystemChanges;

    /// <summary>
    /// 系统偏好变更回调。
    /// </summary>
    /// <remarks>
    /// <b>仅在 <see cref="AppTheme.System"/> 下响应。</b>
    /// 用户显式选了 Light 或 Dark 时，系统怎么变都不该改他的选择 ——
    /// 否则等于擅自推翻用户的明确决定。
    /// </remarks>
    private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        if (Current != AppTheme.System)
        {
            return;
        }

        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        // 事件来自系统事件线程，必须切回 UI 线程才能碰 Application.Resources
        if (app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
        {
            return;
        }

        app.Dispatcher.Invoke(() =>
        {
            // 关闭过程中重入保护：Invoke 可能排在退出流程之后才执行
            if (Current != AppTheme.System
                || System.Windows.Application.Current is null)
            {
                return;
            }

            var resolved = ReadSystemTheme();

            // 只在颜色真的变了时才重设资源，避免无谓的字典替换与界面重绘。
            // 比较基准是"上一次实际生效的颜色"，而非 Current ——
            // System 模式下 Current 恒为 System，拿它比较等于什么都没比。
            if (resolved == _lastAppliedResolvedTheme)
            {
                return;
            }

            Apply(AppTheme.System);

            ThemeChanged?.Invoke(resolved);
        });
    }

    private static bool _watchingSystemChanges;

    /// <summary>
    /// 上一次 <see cref="Apply"/> 实际解析出的深浅色。
    /// </summary>
    /// <remarks>
    /// 用于在跟随系统时判断颜色是否真的发生了变化：
    /// <see cref="Current"/> 在 <see cref="AppTheme.System"/> 下恒为
    /// <see cref="AppTheme.System"/>，无法用它判断。
    /// </remarks>
    private static AppTheme? _lastAppliedResolvedTheme;

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

    /// <summary>
    /// 跟随系统：读取「设置 → 个性化 → 应用模式」的当前值。
    /// </summary>
    /// <remarks>
    /// 这是缺省取值 —— 它不需要用户做任何决策，
    /// 且系统切换时外观会随之变化。
    /// <para>
    /// <b>实时跟随</b>：已通过 <see cref="ThemeManager.StartWatchingSystemChanges"/>
    /// 监听 <see cref="SystemEvents.UserPreferenceChanged"/> 实现，
    /// 系统切换深浅色后界面随之更新，无需重启。
    /// 应用退出时会解除订阅（见 <c>App.OnExit</c>），避免静态事件泄漏。
    /// </para>
    /// <para>
    /// 注意：用户一旦显式选择 Light 或 Dark，系统变更<b>不再影响</b>外观 ——
    /// 显式选择优先于跟随。
    /// </para>
    /// </remarks>
    System,
}