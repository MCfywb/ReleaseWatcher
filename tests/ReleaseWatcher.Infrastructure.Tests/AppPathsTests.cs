using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Infrastructure.Tests.Fakes;
using ReleaseWatcher.Infrastructure.Windows;

namespace ReleaseWatcher.Infrastructure.Tests;

/// <summary>
/// <see cref="AppPaths"/> 的分支行为测试。
/// </summary>
/// <remarks>
/// <para>
/// 被测对象是「路径组合策略」，因此断言全部是纯字符串比较，
/// 不需要真实文件系统 —— 可在任何机器上重复得到相同结果。
/// </para>
/// <para>
/// <b>为什么必须用假实现：</b>真实实现靠「程序目录是否可写」判定 Portable。
/// 测试机上程序目录几乎必然可写，安装版分支永远走不到，
/// 等于该分支无测试覆盖。见 <see cref="FakeWindowsPathResolver"/>。
/// </para>
/// <para>
/// 路径规则依据：<c>DECISIONS.md</c> D-016（目录分离）、
/// D-032（下载目录经 Known Folder）、D-022（语言包随程序发布）。
/// </para>
/// </remarks>
public sealed class AppPathsTests
{
    [Fact]
    public void Portable模式_数据目录与程序目录同源()
    {
        var paths = new AppPaths(FakeWindowsPathResolver.Portable());

        Assert.True(paths.IsPortable);
        Assert.Equal(
            WindowsPathResolver.NormalizeDirectory(@"D:\Tools\ReleaseWatcher"),
            paths.DataDirectory);
    }

    [Fact]
    public void 安装版模式_数据目录落在LocalAppData下()
    {
        var paths = new AppPaths(FakeWindowsPathResolver.Installed());

        Assert.False(paths.IsPortable);
        Assert.Equal(
            WindowsPathResolver.NormalizeDirectory(
                @"C:\Users\tester\AppData\Local\ReleaseWatcher"),
            paths.DataDirectory);
    }

    /// <summary>
    /// 程序目录不可写时把数据写进 ProgramFiles 会直接失败并要求管理员权限，
    /// 因此安装版必须改道 —— 这是 D-016 的核心诉求。
    /// </summary>
    [Fact]
    public void 安装版模式_数据目录不得落在程序目录下()
    {
        var resolver = FakeWindowsPathResolver.Installed();
        var paths = new AppPaths(resolver);

        var programDirectory =
            WindowsPathResolver.NormalizeDirectory(resolver.GetAppDirectory());

        Assert.DoesNotContain(
            programDirectory,
            paths.DataDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 两种模式_派生子目录都在数据目录下(bool portable)
    {
        var resolver = portable
            ? FakeWindowsPathResolver.Portable()
            : FakeWindowsPathResolver.Installed();

        var paths = new AppPaths(resolver);

        AssertChildOf(paths.ConfigFile, paths.DataDirectory);
        AssertChildOf(paths.DatabaseFile, paths.DataDirectory);
        AssertChildOf(paths.LogDirectory, paths.DataDirectory);
        AssertChildOf(paths.CacheDirectory, paths.DataDirectory);
        AssertChildOf(paths.RuntimeDirectory, paths.DataDirectory);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 两种模式_语言目录都随程序目录(bool portable)
    {
        var resolver = portable
            ? FakeWindowsPathResolver.Portable()
            : FakeWindowsPathResolver.Installed();

        var paths = new AppPaths(resolver);

        // 语言包是只读程序资源，与用户数据分离；D-022 决定它随程序发布。
        AssertChildOf(paths.LanguageDirectory, paths.AppDirectory);
    }

    /// <summary>
    /// 下载目录必须原样透传平台解析结果，不得自行拼接 ——
    /// 用户名会变，Downloads 还可能被重定向到其他磁盘或 OneDrive（D-032）。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 两种模式_下载目录原样透传平台解析结果(bool portable)
    {
        var resolver = portable
            ? FakeWindowsPathResolver.Portable()
            : FakeWindowsPathResolver.Installed();

        var paths = new AppPaths(resolver);

        Assert.Equal(resolver.GetDownloadsDirectory(), paths.DownloadDirectory);
    }

    /// <summary>
    /// 所有路径都会参与后续文件 IO 与界面显示，
    /// 空路径会让问题推迟到运行时才暴露，因此在此拦住。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 两种模式_所有路径均非空(bool portable)
    {
        var resolver = portable
            ? FakeWindowsPathResolver.Portable()
            : FakeWindowsPathResolver.Installed();

        var paths = new AppPaths(resolver);

        var all = new[]
        {
            paths.AppDirectory, paths.DataDirectory, paths.ConfigFile,
            paths.DatabaseFile, paths.LogDirectory, paths.CacheDirectory,
            paths.RuntimeDirectory, paths.LanguageDirectory, paths.DownloadDirectory,
        };

        Assert.All(all, p => Assert.False(string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>
    /// 真实实现是 DI 的注册目标，缺少无参构造会在应用启动时才炸，
    /// 不如在这里拦住。
    /// </summary>
    /// <summary>
    /// 所有目录属性必须统一带尾部分隔符。
    /// </summary>
    /// <remarks>
    /// 曾因 Portable 分支直接沿用解析器返回值而破约：
    /// 若解析器返回不带尾分隔符的路径，<c>DataDirectory</c> 就没有分隔符，
    /// 与安装版分支的输出格式不一致，下游拼接与比较都会踩坑。
    /// 契约因此定为「由 <c>AppPaths</c> 统一规范化」，与解析器实现无关。
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 两种模式_所有目录属性均带尾部分隔符(bool portable)
    {
        var resolver = portable
            ? FakeWindowsPathResolver.Portable()
            : FakeWindowsPathResolver.Installed();

        var paths = new AppPaths(resolver);
        var separator = Path.DirectorySeparatorChar.ToString();

        var directories = new[]
        {
            paths.AppDirectory, paths.DataDirectory,
            paths.LogDirectory, paths.CacheDirectory,
            paths.RuntimeDirectory, paths.LanguageDirectory,
        };

        Assert.All(directories, d => Assert.EndsWith(separator, d));
    }

    /// <summary>
    /// 与上一条对称：命名文件的属性**不**应带尾部分隔符，
    /// 否则拼文件名会得到 <c>config.json\next</c> 之类的错误路径。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 两种模式_文件属性不带尾部分隔符(bool portable)
    {
        var resolver = portable
            ? FakeWindowsPathResolver.Portable()
            : FakeWindowsPathResolver.Installed();

        var paths = new AppPaths(resolver);
        var separator = Path.DirectorySeparatorChar.ToString();

        Assert.DoesNotContain(separator, Path.GetFileName(paths.ConfigFile));
        Assert.DoesNotContain(separator, Path.GetFileName(paths.DatabaseFile));

        // 文件必须真的落在数据目录里，而不是被规范化成目录。
        AssertChildOf(paths.ConfigFile, paths.DataDirectory);
        AssertChildOf(paths.DatabaseFile, paths.DataDirectory);
    }

    [Fact]
    public void 无参构造可用_供DI容器调用()
    {
        var paths = new AppPaths();

        Assert.NotNull(paths);
        Assert.False(string.IsNullOrWhiteSpace(paths.AppDirectory));
    }

    [Fact]
    public void 构造时传入空解析器_应立即失败()
    {
        Assert.Throws<ArgumentNullException>(() => new AppPaths(null!));
    }

    private static void AssertChildOf(string child, string parent)
    {
        Assert.StartsWith(parent, child, StringComparison.OrdinalIgnoreCase);
    }
}