namespace ReleaseWatcher.Infrastructure.Windows;

/// <summary>
/// <see cref="Application.Abstractions.IAppPaths"/> 的实现：路径组合策略。
/// </summary>
/// <remarks>
/// <para>
/// 路径规则见 <c>MAP.md</c> 第 4 节，决策依据见
/// <c>DECISIONS.md</c> D-016（目录分离）与 D-032（Known Folder 下载目录）。
/// </para>
///
/// <para>
/// 本类<strong>只做路径组合</strong>：与操作系统交互的部分
/// （Known Folder 查询、可写性探测）全部委托
/// <see cref="IWindowsPathResolver"/>。这样安装版分支才能在测试中覆盖 ——
/// 测试机上程序目录几乎必然可写，只靠真实探测永远走不到安装版分支。
/// </para>
///
/// <para><b>状态：</b>Portable 分支已实测（2026-10-09，见 <c>RUNBOOK.md</c>）；
/// 安装版分支由单元测试覆盖，实际安装后仍需人工验证。</para>
/// </remarks>
public sealed class AppPaths : Application.Abstractions.IAppPaths
{
    /// <summary>数据目录名（安装版下位于 %LocalAppData% 下）。</summary>
    internal const string AppDirectoryName = "ReleaseWatcher";

    public string AppDirectory { get; }

    public string DataDirectory { get; }

    public string ConfigFile { get; }

    public string DatabaseFile { get; }

    public string LogDirectory { get; }

    public string CacheDirectory { get; }

    public string RuntimeDirectory { get; }

    public string LanguageDirectory { get; }

    public string DownloadDirectory { get; }

    /// <summary>当前是否为 Portable 部署（数据与程序同目录）。</summary>
    public bool IsPortable { get; }

    /// <summary>由 DI 容器调用，使用真实Windows 平台实现。</summary>
    public AppPaths()
        : this(new WindowsPathResolver())
    {
    }

    /// <summary>由 DI 容器调用，注入路径解析器。</summary>
    /// <param name="resolver">平台路径解析器；传入假实现即可测试安装版分支。</param>
    public AppPaths(IWindowsPathResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        // 统一在此规范化一次：AppPaths 的契约是「所有目录属性均以分隔符结尾」。
        // 否则解析器实现之间会不一致 —— 有的补了尾分隔符，有的没补，
        // 而 Portable 分支的 DataDirectory 直接沿用 AppDirectory，
        // 会把解析器的差异泄露到下游所有路径拼接中。
        AppDirectory = WindowsPathResolver.NormalizeDirectory(resolver.GetAppDirectory());
        IsPortable = resolver.IsPortable;

        // D-016：Portable 下数据与程序同目录；
        // 安装版下程序目录通常不可写，数据必须转到 %LocalAppData%。
        DataDirectory = IsPortable
            ? AppDirectory
            : WindowsPathResolver.NormalizeDirectory(
                Path.Combine(resolver.GetLocalAppData(), AppDirectoryName));

        ConfigFile = Path.Combine(DataDirectory, "config.json");
        DatabaseFile = Path.Combine(DataDirectory, "app.db");

        LogDirectory = DirectoryUnder(DataDirectory, "logs");
        CacheDirectory = DirectoryUnder(DataDirectory, "cache");
        RuntimeDirectory = DirectoryUnder(DataDirectory, "runtime");

        // 语言包是程序资源，始终与程序目录同级（发布时复制到 Languages/，D-022）。
        // 与数据目录分离的语义在两种部署形态下都保持一致。
        LanguageDirectory = DirectoryUnder(AppDirectory, "Languages");

        // D-032：下载目录必须经 Known Folder 解析，不得硬编码用户路径。
        DownloadDirectory = resolver.GetDownloadsDirectory();
    }

    /// <summary>
    /// 拼出一个子目录，并统一补上尾部分隔符。
    /// </summary>
    /// <remarks>
    /// 刻意不直接用 <c>Path.Combine</c>：那样得到的子目录不带尾分隔符，
    /// 而根目录带 —— 同一类型内出现两种格式，下游拼接与字符串比较都容易出错。
    /// 约定：**命名目录的属性一律以分隔符结尾，命名文件的不带。**
    /// </remarks>
    private static string DirectoryUnder(string parent, string name)
        => WindowsPathResolver.NormalizeDirectory(Path.Combine(parent, name));
}