namespace ReleaseWatcher.Infrastructure.Windows;

/// <summary>
/// Windows 平台路径与部署形态探测的抽象。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由：<see cref="AppPaths"/> 的安装版路径依赖两项环境事实 ——
/// <see cref="GetLocalAppData"/> 与 <see cref="IsPortable"/>。
/// 两者都与具体机器相关，若写死在 <c>AppPaths</c> 内就无法在测试中
/// 构造"安装版"场景（测试机上程序目录几乎必然可写，
/// 只会走到 Portable 分支）。
/// </para>
/// <para>
/// 抽出接口后，测试可传入假实现，从而覆盖安装版与 Portable 两条路径。
/// 该接口属基础设施内部契约，**不放在 Application 层**
/// —— Application 层不应知道 Windows 平台细节（D-003）。
/// </para>
/// </remarks>
public interface IWindowsPathResolver
{
    /// <summary>程序所在目录（含尾部分隔符）。</summary>
    string GetAppDirectory();

    /// <summary>
    /// 当前是否为 Portable 部署。
    /// Portable 表示数据与程序同目录（D-016）。
    /// </summary>
    bool IsPortable { get; }

    /// <summary>本地应用数据根目录（<c>%LocalAppData%</c>），不含末级分隔符。</summary>
    string GetLocalAppData();

    /// <summary>
    /// 下载目录。通过 Windows Known Folder API 解析，不硬编码用户路径（D-032）。
    /// </summary>
    string GetDownloadsDirectory();
}