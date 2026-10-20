using ReleaseWatcher.Domain.ValueObjects;
using ReleaseWatcher.Infrastructure.Configuration;

namespace ReleaseWatcher.Infrastructure.Tests;

/// <summary>
/// <see cref="JsonAppConfiguration"/> 的读取与容错验证。
/// </summary>
/// <remarks>
/// 配置文件是用户可手改的文件：任何一处写坏都不该让应用起不来。
/// 这些用例锁定的正是"宁可少一条，也不要崩"。
/// </remarks>
public class JsonAppConfigurationTests : IDisposable
{
    private readonly string _directory;
    private readonly string _configPath;

    public JsonAppConfigurationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "rw-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _configPath = Path.Combine(_directory, "config.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响测试结论。
        }
    }

    private void WriteConfig(string json) => File.WriteAllText(_configPath, json);

    [Fact]
    public void MissingFile_YieldsEmptyConfiguration()
    {
        var configuration = new JsonAppConfiguration(_configPath);

        Assert.Empty(configuration.MonitoredRepositories);
    }

    [Fact]
    public void MalformedJson_DoesNotThrow()
    {
        WriteConfig("{ this is not json ");

        var configuration = new JsonAppConfiguration(_configPath);

        Assert.Empty(configuration.MonitoredRepositories);
    }

    [Fact]
    public void ReadsRepositoriesAndKnownVersions()
    {
        WriteConfig("""
        {
          "appearance": { "theme": "Dark" },
          "repositories": [ "dotnet/runtime", "https://github.com/PowerShell/PowerShell" ],
          "knownVersions": { "dotnet/runtime": "8.0.10", "PowerShell/PowerShell": "7.4.6" }
        }
        """);

        var configuration = new JsonAppConfiguration(_configPath);

        Assert.Equal(2, configuration.MonitoredRepositories.Count);

        var runtime = RepositoryRef.TryParse("dotnet/runtime", out var r) ? r! : throw new InvalidOperationException();
        var powershell = RepositoryRef.TryParse("PowerShell/PowerShell", out var p) ? p! : throw new InvalidOperationException();

        // 完整 URL 形态也应被接受（用户粘贴的常见形式）。
        Assert.Contains(powershell, configuration.MonitoredRepositories);
        Assert.Equal("8.0.10", configuration.GetKnownVersion(runtime)?.ToString());
        Assert.Equal("7.4.6", configuration.GetKnownVersion(powershell)?.ToString());
    }

    [Fact]
    public void SkipsInvalidEntriesAndDeduplicates()
    {
        WriteConfig("""
        {
          "repositories": [ "dotnet/runtime", "不是仓库", "DOTNET/RUNTIME" ],
          "knownVersions": { "不是仓库": "1.0.0", "dotnet/runtime": "不是版本号" }
        }
        """);

        var configuration = new JsonAppConfiguration(_configPath);

        // 无法解析的条目跳过；大小写不同的同一个仓库只留一条。
        Assert.Single(configuration.MonitoredRepositories);

        var runtime = RepositoryRef.TryParse("dotnet/runtime", out var r) ? r! : throw new InvalidOperationException();

        // 键合法但版本号不可解析 → 记为"无已知版本"，而不是丢掉这个仓库。
        Assert.Null(configuration.GetKnownVersion(runtime));
    }

    [Fact]
    public void UnknownRepository_HasNoKnownVersion()
    {
        WriteConfig("""{ "repositories": [ "dotnet/runtime" ] }""");

        var configuration = new JsonAppConfiguration(_configPath);
        var other = RepositoryRef.TryParse("microsoft/vscode", out var o) ? o! : throw new InvalidOperationException();

        Assert.Null(configuration.GetKnownVersion(other));
    }
}
