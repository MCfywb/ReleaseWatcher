using System.Net;

using ReleaseWatcher.Application.Abstractions;

namespace ReleaseWatcher.Infrastructure.Proxy;

/// <summary>
/// 按当前代理设置提供 HTTP 调用器；设置一变即重建底层处理器。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不直接用注册好的 <see cref="HttpClient"/>：</b>
/// <see cref="HttpClientHandler"/> 的 <c>UseProxy</c> / <c>Proxy</c> 在构造时
/// 就固定了，用户在设置页改了代理之后不会生效，除非重建。
/// 本类持有"当前调用器"，配置变更时重建一次 —— 重建的是轻量的处理器与
/// <see cref="HttpClient"/>，连接池随之重建，对本工具的低频请求完全可接受。
/// </para>
/// <para>
/// <b>三种模式落到 HTTP 上：</b>
/// <list type="bullet">
/// <item>跟随系统：<c>UseProxy=true</c> 且 <c>Proxy=null</c>，
/// 由 .NET 在 Windows 上解析系统代理，含 PAC 与 WPAD 自动配置（D-001）。</item>
/// <item>直连：<c>UseProxy=false</c>，完全不走代理。</item>
/// <item>自定义：<c>Proxy=new WebProxy(server)</c>。</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ConfigurableHttpInvoker : IDisposable
{
    private readonly IProxySettings _settings;
    private readonly Lock _gate = new();
    private HttpMessageInvoker? _current;

    private readonly Func<ProxySettings, HttpMessageInvoker> _factory;

    /// <summary>构造并按当前设置建立第一个调用器。</summary>
    /// <param name="settings">代理配置来源。</param>
    public ConfigurableHttpInvoker(IProxySettings settings)
        : this(settings, Create)
    {
    }

    /// <summary>构造并用自定义工厂建立调用器（测试用：可塞入伪 handler 截断网络）。</summary>
    /// <param name="settings">代理配置来源。</param>
    /// <param name="factory">按配置产出调用器的工厂。</param>
    internal ConfigurableHttpInvoker(IProxySettings settings, Func<ProxySettings, HttpMessageInvoker> factory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(factory);

        _settings = settings;
        _factory = factory;
        _current = factory(settings.Current);

        _settings.Changed += (_, _) => Rebuild();
    }

    /// <summary>当前应当使用的调用器。</summary>
    public HttpMessageInvoker Current => Volatile.Read(ref _current)!;

    /// <inheritdoc />
    public void Dispose()
    {
        var previous = Interlocked.Exchange(ref _current, null);
        previous?.Dispose();
    }

    /// <summary>按配置新建处理器与客户端。</summary>
    internal static HttpMessageInvoker Create(ProxySettings settings)
    {
        // 手工作业模式没填地址时按跟随系统处理，避免"配了却连不上"
        var effective = settings.IsUsable ? settings : ProxySettings.Default;

        HttpClientHandler handler;

        switch (effective.Mode)
        {
            case ProxyMode.Direct:
                handler = new HttpClientHandler { UseProxy = false };
                break;

            case ProxyMode.Manual:
                handler = new HttpClientHandler
                {
                    UseProxy = true,
                    Proxy = new WebProxy(effective.ProxyServer!),
                };
                break;

            default:
                // Proxy 留空 = 由 .NET 解析系统代理（Windows 上含 PAC / WPAD）
                handler = new HttpClientHandler { UseProxy = true };
                break;
        }

        return new HttpClient(handler)
        {
            // GitHub 请求本身很快；给足时间容纳慢速代理链路，
            // 超时由调用方转成"检测失败"，不会挂住界面
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    private void Rebuild()
    {
        var replacement = _factory(_settings.Current);

        lock (_gate)
        {
            var previous = Interlocked.Exchange(ref _current, replacement);
            previous?.Dispose();
        }
    }
}
