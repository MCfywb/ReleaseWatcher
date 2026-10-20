using System.Net;
using System.Text;

using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Domain.Exceptions;
using ReleaseWatcher.Domain.ValueObjects;
using ReleaseWatcher.Infrastructure.GitHub;

namespace ReleaseWatcher.Infrastructure.Tests;

/// <summary>
/// <see cref="GitHubReleaseFeed"/> 的响应映射与错误翻译验证。
/// </summary>
/// <remarks>
/// 用伪 <see cref="HttpMessageHandler"/> 截断网络：既不依赖外网，
/// 也能把"GitHub 返回了什么"这一唯一变量固定下来。
/// 真实网络连通性属于用户验收范畴，不在单元测试里断言。
/// </remarks>
public class GitHubReleaseFeedTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class StubTokenStore : ITokenStore
    {
        private string? _token;

        public string? TokenToReturn
        {
            get => _token;
            init => _token = value;
        }

        public bool HasToken => !string.IsNullOrEmpty(_token);

        public string? Read() => _token;

        public void Save(string token) => _token = token;

        public void Clear() => _token = null;
    }

    private static RepositoryRef Repo()
        => RepositoryRef.TryParse("dotnet/runtime", out var r)
            ? r!
            : throw new InvalidOperationException("测试数据构造失败");

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task GetReleasesAsync_MapsFieldsAndAssets()
    {
        const string body = """
        [
          {
            "tag_name": "v8.0.11",
            "name": "8.0.11",
            "draft": false,
            "prerelease": false,
            "published_at": "2024-05-08T10:00:00Z",
            "body": "修复若干问题",
            "html_url": "https://github.com/dotnet/runtime/releases/tag/v8.0.11",
            "assets": [
              {
                "name": "runtime.zip",
                "browser_download_url": "https://github.com/dotnet/runtime/releases/download/v8.0.11/runtime.zip",
                "size": 12000000,
                "download_count": 42
              },
              {
                "name": "坏地址.zip",
                "browser_download_url": "",
                "size": 1,
                "download_count": 0
              }
            ]
          }
        ]
        """;

        var handler = new StubHandler(_ => Json(body));
        var feed = new GitHubReleaseFeed(new HttpClient(handler), new StubTokenStore());

        var releases = await feed.GetReleasesAsync(Repo());

        var release = Assert.Single(releases);
        Assert.Equal("v8.0.11", release.TagName);
        Assert.Equal("8.0.11", release.Title);
        Assert.False(release.IsDraft);
        Assert.False(release.IsPrerelease);
        Assert.Equal("修复若干问题", release.Notes);
        Assert.NotNull(release.Version);

        // 地址非法的资产应被跳过，而不是让整个仓库的取数失败。
        var asset = Assert.Single(release.Assets);
        Assert.Equal("runtime.zip", asset.Name);
        Assert.Equal(12000000, asset.SizeInBytes);
        Assert.Equal(42, asset.DownloadCount);
    }

    [Fact]
    public async Task GetReleasesAsync_SendsRequiredHeadersAndTokenWhenPresent()
    {
        var handler = new StubHandler(_ => Json("[]"));
        var tokenStore = new StubTokenStore { TokenToReturn = "ghp_example" };
        var feed = new GitHubReleaseFeed(new HttpClient(handler), tokenStore);

        await feed.GetReleasesAsync(Repo());

        var request = Assert.Single(handler.Requests);

        // GitHub 缺 User-Agent 会直接 403。
        Assert.Contains(request.Headers.UserAgent, ua => ua.Product?.Name == "ReleaseWatcher");
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/vnd.github+json");
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("ghp_example", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task GetReleasesAsync_WithoutToken_OmitsAuthorization()
    {
        var handler = new StubHandler(_ => Json("[]"));
        var feed = new GitHubReleaseFeed(new HttpClient(handler), new StubTokenStore());

        await feed.GetReleasesAsync(Repo());

        // 匿名也能访问公开仓库，不该硬性要求 Token。
        Assert.Null(Assert.Single(handler.Requests).Headers.Authorization);
    }

    [Fact]
    public async Task GetReleasesAsync_NotFound_ExplainsRepositoryOrToken()
    {
        var feed = new GitHubReleaseFeed(
            new HttpClient(new StubHandler(_ => Json("{}", HttpStatusCode.NotFound))),
            new StubTokenStore());

        var ex = await Assert.ThrowsAsync<ReleaseFeedException>(
            () => feed.GetReleasesAsync(Repo()));

        Assert.Contains("不存在", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReleasesAsync_RateLimited_MentionsResetTime()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{}"),
        };
        response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", "1800000000");

        var feed = new GitHubReleaseFeed(
            new HttpClient(new StubHandler(_ => response)),
            new StubTokenStore());

        var ex = await Assert.ThrowsAsync<ReleaseFeedException>(
            () => feed.GetReleasesAsync(Repo()));

        Assert.Contains("速率", ex.Message, StringComparison.Ordinal);
        Assert.Contains("重置", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReleasesAsync_NetworkFailure_BecomesFeedException()
    {
        var feed = new GitHubReleaseFeed(
            new HttpClient(new ThrowingHandler()),
            new StubTokenStore());

        var ex = await Assert.ThrowsAsync<ReleaseFeedException>(
            () => feed.GetReleasesAsync(Repo()));

        Assert.Contains("代理", ex.Message, StringComparison.Ordinal);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new HttpRequestException("无法连接到远程服务器");
    }
}
