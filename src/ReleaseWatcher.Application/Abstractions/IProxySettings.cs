namespace ReleaseWatcher.Application.Abstractions;

/// <summary>
/// 代理使用方式。</summary>
public enum ProxyMode
{
    /// <summary>跟随系统设置（Windows 的代理与 PAC/WPAD 自动配置）。</summary>
    System,

    /// <summary>直连，忽略系统代理。用于系统代理不可用或企业策略强制代理的场景。</summary>
    Direct,

    /// <summary>使用用户手工填写的代理服务器。</summary>
    Manual,
}

/// <summary>代理配置快照。</summary>
/// <param name="Mode">代理使用方式。</param>
/// <param name="ProxyServer">手工代理地址，形如 <c>host:port</c>；仅 <see cref="ProxyMode.Manual"/> 使用。</param>
public sealed record ProxySettings(ProxyMode Mode, string? ProxyServer)
{
    /// <summary>默认：跟随系统。</summary>
    public static ProxySettings Default { get; } = new(ProxyMode.System, null);

    /// <summary>是否配置完整（手工模式必须有地址）。</summary>
    public bool IsUsable
        => Mode != ProxyMode.Manual || !string.IsNullOrWhiteSpace(ProxyServer);
}

/// <summary>系统代理现状（只读展示用）。</summary>
/// <param name="AutoDetect">系统是否启用了 WPAD 自动探测。</param>
/// <param name="AutoConfigUrl">系统的 PAC 脚本地址，可为空。</param>
/// <param name="ProxyServer">系统配置的代理地址，可为空。</param>
/// <param name="ProxyBypass">系统的绕过列表，可为空。</param>
public sealed record SystemProxyStatus(
    bool AutoDetect,
    string? AutoConfigUrl,
    string? ProxyServer,
    string? ProxyBypass)
{
    /// <summary>是否检测到任何形式的系统代理配置。</summary>
    public bool HasConfiguration
        => AutoDetect || !string.IsNullOrWhiteSpace(AutoConfigUrl) || !string.IsNullOrWhiteSpace(ProxyServer);

    /// <summary>一句话描述，供设置页直接显示。</summary>
    public string Summary
    {
        get
        {
            if (!HasConfiguration)
            {
                return "系统未配置代理（直接连接）";
            }

            var parts = new List<string>(3);

            if (!string.IsNullOrWhiteSpace(ProxyServer))
            {
                parts.Add($"代理 {ProxyServer}");
            }

            if (!string.IsNullOrWhiteSpace(AutoConfigUrl))
            {
                parts.Add("PAC 自动配置");
            }

            if (AutoDetect)
            {
                parts.Add("WPAD 自动探测");
            }

            if (!string.IsNullOrWhiteSpace(ProxyBypass))
            {
                parts.Add("含绕过列表");
            }

            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// 代理配置：当前生效值、系统现状，以及变更通知。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是端口：</b>"用不用代理、连哪里"是取数链路必须知道的事，
/// 而"怎么读到 Windows 的代理设置"属于基础设施。二者分开后，
/// HTTP 客户端与界面都不必知道系统设置从哪儿来。
/// </para>
/// <para>
/// <b>变更通知：</b>用户改了代理后，正在使用的 HTTP 客户端要重建才能生效，
/// 因此这里提供事件，而不是让调用方反复轮询。
/// </para>
/// </remarks>
public interface IProxySettings
{
    /// <summary>当前生效的代理配置。</summary>
    ProxySettings Current { get; }

    /// <summary>读取系统代理现状；读取失败不应让界面崩溃，故实现须自行降级。</summary>
    SystemProxyStatus ReadSystemStatus();

    /// <summary>更新配置并持久化；无效配置（手工模式未填地址）应被拒绝。</summary>
    /// <param name="settings">新配置。</param>
    void Update(ProxySettings settings);

    /// <summary>配置变更时触发。</summary>
    event EventHandler? Changed;
}
