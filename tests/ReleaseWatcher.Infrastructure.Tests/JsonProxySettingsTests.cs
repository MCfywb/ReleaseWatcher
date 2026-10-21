using System.Text.Json;

using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Infrastructure.Configuration;

namespace ReleaseWatcher.Infrastructure.Tests;

/// <summary>
/// <see cref="JsonProxySettings"/> 的持久化、容错与事件验证。
/// </summary>
/// <remarks>
/// 重点在两处容易出事的地方：<b>不能破坏 config.json 里其他组件的键</b>
/// （主题与分栏比例都存在同一个文件里），以及<b>写坏配置不该让应用带着
/// 非法状态启动</b>。
/// </remarks>
public class JsonProxySettingsTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public JsonProxySettingsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "rw-proxy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "config.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响测试结论
        }
    }

    [Fact]
    public void MissingFile_DefaultsToSystemMode()
    {
        var settings = new JsonProxySettings(_path);

        Assert.Equal(ProxyMode.System, settings.Current.Mode);
    }

    [Fact]
    public void Update_PersistsAndReloads()
    {
        var settings = new JsonProxySettings(_path);

        settings.Update(new ProxySettings(ProxyMode.Manual, "127.0.0.1:7890"));

        var reloaded = new JsonProxySettings(_path);
        Assert.Equal(ProxyMode.Manual, reloaded.Current.Mode);
        Assert.Equal("127.0.0.1:7890", reloaded.Current.ProxyServer);
    }

    [Fact]
    public void Update_PreservesOtherSectionsInConfigFile()
    {
        // 主题与分栏比例都存在同一个文件里，写代理不能把它们抹掉
        File.WriteAllText(_path, """
        {
          "appearance": { "theme": "Dark" },
          "repositories": [ "dotnet/runtime" ],
          "layout": { "reposColumns": [ 0.3, 0.3, 0.4 ] }
        }
        """);

        new JsonProxySettings(_path).Update(new ProxySettings(ProxyMode.Direct, null));

        using var document = JsonDocument.Parse(File.ReadAllText(_path));
        var root = document.RootElement;

        Assert.Equal("Dark", root.GetProperty("appearance").GetProperty("theme").GetString());
        Assert.Equal("dotnet/runtime", root.GetProperty("repositories")[0].GetString());
        Assert.Equal(0.3, root.GetProperty("layout").GetProperty("reposColumns")[0].GetDouble(), 3);

        // 连续两次写入也不该互相吃掉
        new JsonProxySettings(_path).Update(new ProxySettings(ProxyMode.Manual, "10.0.0.1:1080"));

        using var second = JsonDocument.Parse(File.ReadAllText(_path));
        Assert.Equal("Dark", second.RootElement.GetProperty("appearance").GetProperty("theme").GetString());
        Assert.Equal("Manual", second.RootElement.GetProperty("proxy").GetProperty("mode").GetString());
    }

    [Fact]
    public void Update_RejectsManualModeWithoutServer()
    {
        var settings = new JsonProxySettings(_path);

        Assert.Throws<ArgumentException>(() => settings.Update(new ProxySettings(ProxyMode.Manual, "  ")));
        Assert.Equal(ProxyMode.System, settings.Current.Mode);
    }

    [Fact]
    public void Reload_UnusableConfig_FallsBackToSystem()
    {
        // 配置写坏了不该让应用带着"手工模式却没有地址"的非法状态启动
        File.WriteAllText(_path, """{ "proxy": { "mode": "Manual" } }""");

        var settings = new JsonProxySettings(_path);

        Assert.Equal(ProxyMode.System, settings.Current.Mode);
    }

    [Fact]
    public void Reload_MalformedJson_FallsBackToSystem()
    {
        File.WriteAllText(_path, "{ not json");

        Assert.Equal(ProxyMode.System, new JsonProxySettings(_path).Current.Mode);
    }

    [Fact]
    public void Update_RaisesChanged()
    {
        var settings = new JsonProxySettings(_path);
        var raised = 0;
        settings.Changed += (_, _) => raised++;

        settings.Update(new ProxySettings(ProxyMode.Direct, null));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void ReadSystemStatus_DoesNotThrow()
    {
        // 真实读一次 WinHTTP：目的是证明 P/Invoke 在本机可用且失败也不会抛
        var settings = new JsonProxySettings(_path);

        var status = settings.ReadSystemStatus();

        Assert.NotNull(status);
        Assert.NotNull(status.Summary);
    }
}
