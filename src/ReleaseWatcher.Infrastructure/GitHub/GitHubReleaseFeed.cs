using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Application.Exceptions;
using ReleaseWatcher.Domain.Entities;
using ReleaseWatcher.Domain.Exceptions;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Infrastructure.GitHub;

/// <summary>
/// 通过 GitHub REST API 取回仓库 Release 列表。
/// </summary>
/// <remarks>
/// <para>
/// <b>选型见 <c>DECISIONS.md</c> D-053</b>：用BCL 自带的
/// <see cref="HttpClient"/> + <see cref="JsonDocument"/>，不引入 Octokit。
/// 本应用只需要"某仓库的 Release 列表"这一个窄接口，
/// 而 GitHub 特有的细节（User-Agent、速率限制头、API 版本头）
/// 本来就得自己处理才可控。
/// </para>
/// <para>
/// <b>系统代理：</b>本类不设置 <c>HttpClientHandler.Proxy</c>，
/// 由组合根传入的 handler 决定；保留默认（读取系统代理，含 PAC/WPAD）
/// 即可满足 D-001 的代理要求。
/// </para>
/// <para>
/// <b>Token：</b>可选。未配置时走匿名调用（公开仓库 60 次/小时），
/// 配置后带 <c>Authorization</c> 头，限额与私有仓库能力都更高
/// （见 <c>AGENTS.md</c> §1.4：Token 只从 <see cref="ITokenStore"/> 取，绝不落日志）。
/// </para>
/// <para>
/// <b>分页：</b>按 <c>per_page=100</c> 逐页取，直到某页返回数不足一页。
/// 设页数上限以防接口行为异常时无限翻页。
/// </para>
/// </remarks>
public sealed class GitHubReleaseFeed : IReleaseFeed
{
    private const string ApiBase = "https://api.github.com";
    private const string ApiVersion = "2022-11-28";
    private const int PageSize = 100;
    private const int MaxPages = 10;

    /// <summary>GitHub 要求所有请求带 User-Agent，否则返回 403。</summary>
    private const string UserAgent = "ReleaseWatcher/0.1";

    private readonly HttpClient _client;
    private readonly ITokenStore _tokenStore;

    /// <summary>构造客户端。</summary>
    /// <param name="client">已配置好的 HTTP 客户端（代理、超时由组合根决定）。</param>
    /// <param name="tokenStore">Token 来源；读不到时按匿名调用处理。</param>
    public GitHubReleaseFeed(HttpClient client, ITokenStore tokenStore)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(tokenStore);

        _client = client;
        _tokenStore = tokenStore;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Release>> GetReleasesAsync(
        RepositoryRef repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var releases = new List<Release>();

        for (var page = 1; page <= MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = await GetPageAsync(repository, page, cancellationToken);
            releases.AddRange(batch);

            if (batch.Count < PageSize)
            {
                break;
            }
        }

        return releases;
    }

    /// <summary>取回单页 Release。</summary>
    private async Task<IReadOnlyList<Release>> GetPageAsync(
        RepositoryRef repository,
        int page,
        CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}/repos/{Uri.EscapeDataString(repository.Owner)}"
                  + $"/{Uri.EscapeDataString(repository.Name)}/releases"
                  + $"?per_page={PageSize}&page={page}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request);

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ReleaseFeedException(
                $"访问 GitHub 失败：{ex.Message}。请检查网络或系统代理设置。", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReleaseFeedException("访问 GitHub 超时。", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await BuildErrorAsync(response, repository);
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                return ParseReleases(document.RootElement, repository);
            }
            catch (JsonException ex)
            {
                throw new ReleaseFeedException(
                    $"GitHub 返回的内容无法解析：{ex.Message}", ex);
            }
        }
    }

    /// <summary>按 GitHub 约定设置请求头。</summary>
    private void ApplyHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        // Token 是可选的：没有它也能访问公开仓库（限额较低）。
        // 读取失败不应阻断检测 —— 降级为匿名并继续。
        var token = TryReadToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    /// <summary>取Token；凭据系统不可用时返回 <c>null</c> 而不抛异常。</summary>
    private string? TryReadToken()
    {
        try
        {
            return _tokenStore.Read();
        }
        catch (TokenStorageException)
        {
            // 凭据读不到只意味着降级为匿名调用，不该让整次检测失败。
            return null;
        }
    }

    /// <summary>把 HTTP 状态翻译成用户能理解的原因。</summary>
    private static async Task<ReleaseFeedException> BuildErrorAsync(
        HttpResponseMessage response,
        RepositoryRef repository)
    {
        var remaining = ReadHeader(response, "X-RateLimit-Remaining");
        var reset = ReadHeader(response, "X-RateLimit-Reset");

        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "GitHub Token 无效或已过期，请到设置页更新或清除 Token。",

            HttpStatusCode.NotFound =>
                $"仓库 {repository} 不存在，或为私有仓库且未配置有效 Token。",

            HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                when remaining == "0" =>
                $"GitHub 请求速率已用尽" + FormatResetTime(reset) + "。可稍后重试，或配置 Token 提高限额。",

            HttpStatusCode.Forbidden =>
                "GitHub 拒绝了本次请求：可能触发了速率限制，或 Token 权限不足。",

            _ => $"GitHub 返回 {(int)response.StatusCode} {response.ReasonPhrase}。",
        };

        // 响应体可能含更具体的原因，但绝不回显原文（可能包含 URL 等敏感信息），
        // 只在调试需要时由调用方自行记录。
        await Task.CompletedTask;

        return new ReleaseFeedException(message);
    }

    private static string? ReadHeader(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()
            : null;

    private static string FormatResetTime(string? resetUnixSeconds)
        => long.TryParse(resetUnixSeconds, out var seconds)
            ? $"（将于 {DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime():HH:mm:ss} 重置）"
            : string.Empty;

    /// <summary>把 JSON 数组映射为领域 Release。</summary>
    /// <remarks>
    /// 用 <see cref="JsonDocument"/> 逐字段取而非生成 DTO：
    /// 字段少、形状固定，且避免为省几行代码引入反射式反序列化开销。
    /// </remarks>
    private static IReadOnlyList<Release> ParseReleases(JsonElement root, RepositoryRef repository)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Release>();
        }

        var releases = new List<Release>();

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var tag = GetString(item, "tag_name");
            if (string.IsNullOrWhiteSpace(tag))
            {
                // 无 tag 的条目无法构成 Release 实体（领域不变量），跳过。
                continue;
            }

            releases.Add(Release.FromTag(
                repository,
                tag,
                GetString(item, "name"),
                GetBoolean(item, "draft") ?? false,
                GetBoolean(item, "prerelease"),
                GetDateTimeOffset(item, "published_at"),
                ParseAssets(item, repository),
                GetUri(item, "html_url"),
                GetString(item, "body")));
        }

        return releases;
    }

    /// <summary>解析资产列表；跳过地址非法的条目而非让整仓库失败。</summary>
    private static IReadOnlyList<ReleaseAsset> ParseAssets(JsonElement release, RepositoryRef repository)
    {
        if (!release.TryGetProperty("assets", out var assetsElement)
            || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ReleaseAsset>();
        }

        var assets = new List<ReleaseAsset>();

        foreach (var asset in assetsElement.EnumerateArray())
        {
            var name = GetString(asset, "name");
            var downloadUri = GetUri(asset, "browser_download_url");

            if (string.IsNullOrWhiteSpace(name) || downloadUri is null || !downloadUri.IsAbsoluteUri)
            {
                continue;
            }

            assets.Add(ReleaseAsset.Create(
                name,
                downloadUri,
                GetInt64(asset, "size") ?? 0,
                (int)Math.Clamp(GetInt64(asset, "download_count") ?? 0, 0, int.MaxValue)));
        }

        return assets;
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? GetBoolean(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static long? GetInt64(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
           && value.TryGetDateTimeOffset(out var parsed)
            ? parsed
            : null;

    private static Uri? GetUri(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
           && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri)
            ? uri
            : null;
}
