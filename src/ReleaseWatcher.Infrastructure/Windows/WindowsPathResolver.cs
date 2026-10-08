using System.IO;
using System.Runtime.InteropServices;

namespace ReleaseWatcher.Infrastructure.Windows;

/// <summary>
/// <see cref="IWindowsPathResolver"/> 的真实实现，负责与操作系统交互。
/// </summary>
/// <remarks>
/// 本类是唯一允许调用 Windows API 的地方，便于测试通过假实现替换。
/// </remarks>
public sealed class WindowsPathResolver : IWindowsPathResolver
{
    /// <summary>FOLDERID_Downloads。</summary>
    private const string DownloadsKnownFolderGuid =
        "{374DE290-123F-4565-9164-39C4925E467B}";

    public string GetAppDirectory()
    {
        var fromProcess = Path.GetDirectoryName(
            Environment.ProcessPath ?? string.Empty);

        var directory = NormalizeDirectory(
            string.IsNullOrWhiteSpace(fromProcess) ? AppContext.BaseDirectory : fromProcess);

        return directory;
    }

    /// <summary>
    /// 判断当前是否以 Portable 模式运行 —— 判据是程序目录是否可写。
    /// </summary>
    /// <remarks>
    /// Portable 版的定义是"数据与程序同目录"（D-016）。
    /// 开发态与 U 盘拷贝必然可写 → Portable；
    /// 装在 <c>%ProgramFiles%</c> 下不可写 → 数据必须转到 <c>%LocalAppData%</c>。
    ///
    /// <para>
    /// 写探针用 <see cref="FileOptions.DeleteOnClose"/>：
    /// 文件随句柄关闭自动删除，无需手动清理，也不会残留垃圾文件。
    /// </para>
    /// </remarks>
    public bool IsPortable
    {
        get
        {
            var appDirectory = GetAppDirectory();

            try
            {
                var probe = Path.Combine(appDirectory, $".write-probe-{Guid.NewGuid():N}.tmp");

                using var stream = new FileStream(
                    probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 1, FileOptions.DeleteOnClose);

                stream.WriteByte(0);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // 不可写 = 安装版语义
                return false;
            }
        }
    }

    public string GetLocalAppData()
        => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// 通过 Windows Known Folder API 解析真实的 Downloads 路径。
    /// </summary>
    /// <remarks>
    /// 不可硬编码 <c>C:\Users\&lt;User&gt;\Downloads</c>：
    /// 用户名可能变化，Downloads 也可能被重定向到其他磁盘或 OneDrive
    /// （<c>DECISIONS.md</c> D-032）。
    /// </remarks>
    public string GetDownloadsDirectory()
    {
        try
        {
            var result = SHGetKnownFolderPath(
                DownloadsKnownFolderGuid, 0, IntPtr.Zero, out IntPtr pathPtr);

            if (result == 0 && pathPtr != IntPtr.Zero)
            {
                try
                {
                    var path = Marshal.PtrToStringUni(pathPtr);
                    return NormalizeDirectory(path ?? FallbackDownloadsDirectory());
                }
                finally
                {
                    // SHGetKnownFolderPath 用 CoTaskMem 分配，
                    // 必须成对释放，否则泄漏原生内存。
                    Marshal.FreeCoTaskMem(pathPtr);
                }
            }
        }
        catch (DllNotFoundException)
        {
            // 非 Windows 平台或缺少 shell32.dll —— 走回退。
        }
        catch (EntryPointNotFoundException)
        {
            // 极老的系统缺少该导出 —— 走回退。
        }

        return FallbackDownloadsDirectory();
    }

    private static string FallbackDownloadsDirectory()
        => NormalizeDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads"));

    internal static string NormalizeDirectory(string path)
    {
        var full = Path.GetFullPath(path);

        // 保持尾部分隔符，让调用方拼接时可读，也避免漏掉分隔符。
        return full.EndsWith(Path.DirectorySeparatorChar)
            ? full
            : full + Path.DirectorySeparatorChar;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPWStr)] string rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);
}