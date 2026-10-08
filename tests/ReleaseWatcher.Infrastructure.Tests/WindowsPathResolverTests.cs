using ReleaseWatcher.Infrastructure.Windows;

namespace ReleaseWatcher.Infrastructure.Tests;

/// <summary>
/// <see cref="WindowsPathResolver.NormalizeDirectory"/> 的测试。
/// </summary>
/// <remarks>
/// 虽名为内部方法，但它是全部路径拼接的基石：
/// 一旦尾部分隔符行为改变，<see cref="AppPaths"/> 的所有子路径拼接都会受影响，
/// 因此值得单独锁定。
/// </remarks>
public sealed class WindowsPathResolverTests
{
    [Fact]
    public void 缺少尾部分隔符时补上()
    {
        var result = WindowsPathResolver.NormalizeDirectory(@"C:\Tools\ReleaseWatcher");

        Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), result);
    }

    [Fact]
    public void 已有尾部分隔符时不重复追加()
    {
        var result = WindowsPathResolver.NormalizeDirectory(@"C:\Tools\ReleaseWatcher" + Path.DirectorySeparatorChar);

        Assert.DoesNotContain(
            new string(Path.DirectorySeparatorChar, 2),
            result,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// 相对路径会被展开成绝对路径，避免后续依赖当前工作目录。
    /// </summary>
    [Fact]
    public void 相对路径展开为绝对路径()
    {
        var result = WindowsPathResolver.NormalizeDirectory("sub");

        Assert.True(Path.IsPathFullyQualified(result));
    }

    [Fact]
    public void 实际解析器能给出非空程序目录与下载目录()
    {
        var resolver = new WindowsPathResolver();

        Assert.False(string.IsNullOrWhiteSpace(resolver.GetAppDirectory()));
        Assert.False(string.IsNullOrWhiteSpace(resolver.GetDownloadsDirectory()));
        Assert.False(string.IsNullOrWhiteSpace(resolver.GetLocalAppData()));
    }

    /// <summary>
    /// 写探针必须不留残file：<c>FileOptions.DeleteOnClose</c>
    /// 保证随句柄关闭自动删除。若失效，应用目录会逐渐堆满
    /// <c>.write-probe-*.tmp</c>。
    /// </summary>
    [Fact]
    public void 可写性探测不留残留文件()
    {
        var resolver = new WindowsPathResolver();
        var appDirectory = resolver.GetAppDirectory();

        // 执行两轮，若探针残留则第二次仍会留下文件
        _ = resolver.IsPortable;
        _ = resolver.IsPortable;

        var leftovers = Directory
            .EnumerateFiles(appDirectory, ".write-probe-*.tmp", SearchOption.TopDirectoryOnly)
            .ToArray();

        Assert.Empty(leftovers);
    }
}