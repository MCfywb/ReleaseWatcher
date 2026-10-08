namespace ReleaseWatcher.Application.Abstractions;

/// <summary>
/// 应用运行时路径的统一提供者。
/// </summary>
/// <remarks>
/// <para>
/// 路径解析**必须**经由此接口，禁止各模块自行拼接路径字符串。
/// 原因见 <c>DECISIONS.md</c> D-016 与 <c>MAP.md</c> 第 4 节：
/// </para>
/// <list type="bullet">
/// <item>安装版与 Portable 的数据位置规则不同（AppData vs EXE 同级），
/// 自行拼接会在两种发布形态间产生行为差异。</item>
/// <item>数据目录与下载目录必须分离 —— 配置/数据库不应落在程序目录，
/// 下载也不应堆在程序目录。</item>
/// <item>Windows Known Folder 解析（如 Downloads）需要 API 调用，
/// 硬编码 <c>C:\Users\&lt;User&gt;\Downloads</c> 会因用户名与重定向而失效
/// （D-032）。</item>
/// </list>
///
/// <para><b>路径格式契约：</b>命名<b>目录</b>的属性一律以路径分隔符结尾，
/// 命名<b>文件</b>的属性不带。这样拼接子路径时无需判断是否要补分隔符，
/// 也不易出现 <c>C:\a\b</c> 与 <c>C:\a\b\</c> 混用导致的比较失败。</para>
///
/// <para>
/// 具体实现位于 <c>ReleaseWatcher.Infrastructure/Windows</c>，
/// 由 <c>ReleaseWatcher.App/AppServices.cs</c> 注册。
/// </para>
/// </remarks>
public interface IAppPaths
{
    /// <summary>程序所在目录。只读资源（EXE、Languages）所在处。</summary>
    string AppDirectory { get; }

    /// <summary>
    /// 用户数据所在目录。安装版与 Portable 的取值规则不同，
    /// 见类型注释与 <c>MAP.md</c> 第 4 节。
    /// </summary>
    string DataDirectory { get; }

    /// <summary>配置文件 <c>config.json</c> 的完整路径。</summary>
    string ConfigFile { get; }

    /// <summary>SQLite 数据库 <c>app.db</c> 的完整路径。</summary>
    string DatabaseFile { get; }

    /// <summary>日志目录。</summary>
    string LogDirectory { get; }

    /// <summary>缓存目录。</summary>
    string CacheDirectory { get; }

    /// <summary>运行时目录（Managed aria2 等）。</summary>
    string RuntimeDirectory { get; }

    /// <summary>
    /// 语言包目录（<c>Languages/</c>）。与数据目录**必须分离**：
    /// 语言包属程序资源，用户数据属用户数据。
    /// </summary>
    string LanguageDirectory { get; }

    /// <summary>
    /// 默认下载目录。通过 Windows Known Folder API 解析，
    /// 不硬编码用户路径（D-032）。
    /// </summary>
    string DownloadDirectory { get; }
}