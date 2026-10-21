using System.Net;
using System.Net.Http;

using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Infrastructure.Proxy;

namespace ReleaseWatcher.Infrastructure.Tests;

/// <summary>
/// <see cref="ConfigurableHttpInvoker"/> 三种代理模式的映射验证。
/// </summary>
/// <remarks>
/// 这一层最容易出"看起来配了、其实没生效"的错：handler 的代理属性在构造时固定，
/// 而代理设置是运行中可改的。因此这里直接断言"每种模式造出来的 handler
/// 到底长什么样"。
/// </remarks>
public class ConfigurableHttpInvokerTests
{
    private static HttpClientHandler HandlerOf(ProxySettings settings)
    {
        using var invoker = ConfigurableHttpInvoker.Create(settings);

        // Create 返回的是 HttpClient，handler 是它内部的私有字段；
        // 取它需要反射，但这是唯一能验证"代理属性真的按模式设了"的办法。
        var field = typeof(HttpMessageInvoker).GetField(
            "_handler",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return Assert.IsType<HttpClientHandler>(field!.GetValue(invoker));
    }

    [Fact]
    public void SystemMode_UsesProxyWithoutExplicitProxy()
    {
        // Proxy 留空 = 由 .NET 在 Windows 上解析系统代理（含 PAC / WPAD）
        var handler = HandlerOf(ProxySettings.Default);

        Assert.True(handler.UseProxy);
        Assert.Null(handler.Proxy);
    }

    [Fact]
    public void DirectMode_DisablesProxy()
    {
        var handler = HandlerOf(new ProxySettings(ProxyMode.Direct, null));

        Assert.False(handler.UseProxy);
    }

    [Fact]
    public void ManualMode_UsesGivenServer()
    {
        var handler = HandlerOf(new ProxySettings(ProxyMode.Manual, "127.0.0.1:7890"));

        Assert.True(handler.UseProxy);

        var webProxy = Assert.IsType<WebProxy>(handler.Proxy);
        Assert.Equal("127.0.0.1:7890", webProxy.Address?.Authority);
    }

    [Fact]
    public void ManualModeWithoutServer_FallsBackToSystem()
    {
        // 配了却连不上是最糟的失败方式，宁可按跟随系统处理
        var handler = HandlerOf(new ProxySettings(ProxyMode.Manual, "   "));

        Assert.True(handler.UseProxy);
        Assert.Null(handler.Proxy);
    }

    [Fact]
    public void SettingsChange_ProducesNewInvoker()
    {
        var settings = new StubProxySettings();
        using var invoker = new ConfigurableHttpInvoker(settings, ConfigurableHttpInvoker.Create);

        var before = invoker.Current;

        settings.Update(new ProxySettings(ProxyMode.Direct, null));

        // 换了设置就必须是新的调用器，否则新设置不会生效
        Assert.NotSame(before, invoker.Current);
    }

    private sealed class StubProxySettings : IProxySettings
    {
        public ProxySettings Current { get; private set; } = ProxySettings.Default;

        public event EventHandler? Changed;

        public SystemProxyStatus ReadSystemStatus() => new(false, null, null, null);

        public void Update(ProxySettings settings)
        {
            Current = settings;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
