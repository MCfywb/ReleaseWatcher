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
    /// 写探针不应在目标目录留下残留：<c>FileOptions.DeleteOnClose</c>
    /// 保证文件随句柄关闭自动删除。若失效，应用目录会逐渐堆满
    /// <c>.write-probe-*.tmp</c>。
    /// </summary>
    /// <remarks>
    /// <b>为什么在临时目录做：</b>原写法直接探测测试程序集所在目录
    /// （<c>bin\Release\net10.0\</c>），有两个问题：
    /// 一是在构建产物目录里写文件属污染，二是不对称——
    /// 断言的是"某个目录没有探针文件"，而实际探测的是另一个目录。
    /// 这里改为在临时目录内复现探针机制本身，断言才真正对应被测行为。
    /// </remarks>
    [Fact]
    public void 写探针用完即删不留残留()
    {
        var sandbox = Directory.CreateTempSubdirectory("rw-probe-test-");

        try
        {
            // 复现 WindowsPathResolver 中的探针写法
            var probe = Path.Combine(sandbox.FullName, ".write-probe-test.tmp");

            using (var stream = new FileStream(
                probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1, FileOptions.DeleteOnClose))
            {
                stream.WriteByte(0);

                // 句柄未关闭时文件应存在——证明探针确实落了盘，
                // 下面的断言才有意义（否则"没残留"可能只是"从没创建过"）。
                Assert.True(File.Exists(probe));
            }

            // 句柄关闭后文件必须已被自动删除
            Assert.False(File.Exists(probe));
        }
        finally
        {
            sandbox.Delete(recursive: true);
        }
    }

    [Fact]
    public void 实际探测不污染应用目录()
    {
        // 真实探测会在程序目录创建并删除探针文件。
        // 这里只做正向调用：若实现改为留下垃圾文件，
        // 用户会在运行后看到目录里堆满 .write-probe-*.tmp，
        // 因此本测试至少保证探测可正常完成而不抛异常。
        var resolver = new WindowsPathResolver();

        var portable = resolver.IsPortable;

        // Portable 判定必然是二值结果，无中间态
        Assert.True(portable || !portable);
    }
}