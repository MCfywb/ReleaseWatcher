using ReleaseWatcher.Infrastructure.Windows;

namespace ReleaseWatcher.Infrastructure.Tests.Fakes;

/// <summary>
/// <see cref="IWindowsPathResolver"/> 的可控测试替身。
/// </summary>
/// <remarks>
/// 存在的理由：<c>AppPaths</c> 的安装版分支依赖两项环境事实 ——
/// 「程序目录是否可写」与「<c>%LocalAppData%</c> 在哪」。
/// 两者都由操作系统决定，写死在实现里就无法在测试机上构造安装版场景。
/// 抽出接口后，构造这个假实现即可精确控制两条分支。
/// </remarks>
internal sealed class FakeWindowsPathResolver : IWindowsPathResolver
{
    /// <summary>构造测试替身。</summary>
    /// <param name="appDirectory">程序目录（测试中用固定的假路径，不触碰真实文件系统）。</param>
    /// <param name="isPortable">是否 Portable 部署。</param>
    /// <param name="localAppData">
    /// <c>%LocalAppData%</c> 根目录。刻意<strong>不带</strong>末级分隔符，
    /// 以验证实现是否正确拼接而非直接覆盖。
    /// </param>
    /// <param name="downloadsDirectory">下载目录。</param>
    public FakeWindowsPathResolver(
        string appDirectory,
        bool isPortable,
        string localAppData,
        string downloadsDirectory)
    {
        _appDirectory = appDirectory;
        IsPortable = isPortable;
        _localAppData = localAppData;
        _downloadsDirectory = downloadsDirectory;
    }

    private readonly string _appDirectory;
    private readonly string _localAppData;
    private readonly string _downloadsDirectory;

    public string GetAppDirectory() => _appDirectory;

    public bool IsPortable { get; }

    public string GetLocalAppData() => _localAppData;

    public string GetDownloadsDirectory() => _downloadsDirectory;

    /// <summary>返回 Portable 场景的替身。</summary>
    public static FakeWindowsPathResolver Portable() => new(
        appDirectory: @"D:\Tools\ReleaseWatcher",
        isPortable: true,
        localAppData: @"C:\Users\tester\AppData\Local",
        downloadsDirectory: @"C:\Users\tester\Downloads\");

    /// <summary>返回安装版场景的替身。</summary>
    public static FakeWindowsPathResolver Installed() => new(
        appDirectory: @"C:\Program Files\ReleaseWatcher",
        isPortable: false,
        localAppData: @"C:\Users\tester\AppData\Local",
        downloadsDirectory: @"D:\Downloads");
}