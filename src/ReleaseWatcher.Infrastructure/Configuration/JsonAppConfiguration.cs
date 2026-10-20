using System.Text.Json;

using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Infrastructure.Configuration;

/// <summary>
/// 从 <c>config.json</c> 读取监控仓库列表与各仓库已知版本。
/// </summary>
/// <remarks>
/// <para>
/// <b>文件形状</b>（其余键由其他组件各管各的，互不干扰）：
/// <code>
/// {
///   "appearance": { "theme": "Dark" },
///   "repositories": [ "dotnet/runtime", "PowerShell/PowerShell" ],
///   "knownVersions": { "dotnet/runtime": "8.0.10" }
/// }
/// </code>
/// </para>
/// <para>
/// <b>为什么先落配置文件而不是直接上数据库：</b>本轮只做真实数据接线，
/// 配置文件的读写成本最低；等需要"下载记录""检测历史"等成规模数据时，
/// 再由 C2 的 SQLite 接管，界面与用例通过 <see cref="IAppConfiguration"/> 取值，不受影响。
/// </para>
/// <para>
/// <b>容错原则：</b>配置是用户可手改的文件，出错不应让应用起不来。
/// 文件缺失、JSON 非法、单个仓库条目无法解析 —— 一律跳过并按"没有配置"处理。
/// 另：Token绝不写进本文件（<c>AGENTS.md</c> §1.4），它走 <see cref="ITokenStore"/>。
/// </para>
/// </remarks>
public sealed class JsonAppConfiguration : IAppConfiguration
{
    private readonly Dictionary<RepositoryRef, SemanticVersion?> _knownVersions;

    /// <summary>构造并读取配置。</summary>
    /// <param name="configFilePath"><c>config.json</c> 的完整路径（来自 <c>IAppPaths.ConfigFile</c>）。</param>
    public JsonAppConfiguration(string configFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);

        var repositories = new List<RepositoryRef>();
        _knownVersions = new Dictionary<RepositoryRef, SemanticVersion?>();

        using var document = TryLoad(configFilePath);
        if (document is null)
        {
            MonitoredRepositories = repositories;
            return;
        }

        var root = document.RootElement;

        ReadRepositories(root, repositories);
        ReadKnownVersions(root);

        MonitoredRepositories = repositories;
    }

    /// <inheritdoc />
    public IReadOnlyList<RepositoryRef> MonitoredRepositories { get; }

    /// <inheritdoc />
    public SemanticVersion? GetKnownVersion(RepositoryRef repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return _knownVersions.TryGetValue(repository, out var version) ? version : null;
    }

    /// <summary>读取配置文件；缺失或非法时返回 <c>null</c>。</summary>
    private static JsonDocument? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            return JsonDocument.Parse(stream);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 用户手改的配置写坏了不该让应用崩溃：按"未配置"处理。
            return null;
        }
    }

    /// <summary>读取 <c>repositories</c> 数组，跳过无法解析与重复的条目。</summary>
    private static void ReadRepositories(JsonElement root, List<RepositoryRef> repositories)
    {
        if (!root.TryGetProperty("repositories", out var element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (RepositoryRef.TryParse(item.GetString(), out var repository) && repository is not null)
            {
                repositories.Add(repository);
            }
        }

        // RepositoryRef 的相等性忽略大小写，去重后列表才不会出现两条同一个仓库。
        var distinct = new List<RepositoryRef>(repositories.Count);
        foreach (var repository in repositories)
        {
            if (!distinct.Contains(repository))
            {
                distinct.Add(repository);
            }
        }

        repositories.Clear();
        repositories.AddRange(distinct);
    }

    /// <summary>读取 <c>knownVersions</c> 对象；值解析失败记为"无已知版本"而非丢弃键。</summary>
    private void ReadKnownVersions(JsonElement root)
    {
        if (!root.TryGetProperty("knownVersions", out var element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!RepositoryRef.TryParse(property.Name, out var repository) || repository is null)
            {
                continue;
            }

            SemanticVersion? version = null;
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                SemanticVersion.TryParse(property.Value.GetString(), out version);
            }

            // 键非法版本时存 null：界面显示"首次检测"，语义正确。
            _knownVersions[repository] = version;
        }
    }
}
