using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Application.Services;
using ReleaseWatcher.Domain.Entities;
using ReleaseWatcher.Domain.Enums;
using ReleaseWatcher.Domain.Exceptions;
using ReleaseWatcher.Domain.Rules;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Application.Tests;

/// <summary>
/// <see cref="ReleaseDetectionService"/> 的用例编排测试。
/// </summary>
/// <remarks>
/// 领域规则本身由 Domain.Tests 覆盖，此处只测应用层自己的职责：
/// 取数、调用规则、把结果整理成 UI 可用的形状，以及对边界情况的处理。
/// </remarks>
public class ReleaseDetectionServiceTests
{
    private static RepositoryRef Repo(string slug = "dotnet/runtime")
        => RepositoryRef.TryParse(slug, out var reference)
            ? reference!
            : throw new InvalidOperationException($"测试数据 {slug} 无法解析");

    private static SemanticVersion Version(string tag)
        => SemanticVersion.TryParse(tag, out var version) && version is not null
            ? version
            : throw new InvalidOperationException($"测试数据 {tag} 无法解析为版本号");

    /// <summary>
    /// 构造测试用 Release。
    /// </summary>
    /// <remarks>
    /// 方法名<b>刻意不叫 <c>Release</c></b>：那会与 <see cref="Release"/> 实体同名，
    /// 使方法内的 <c>Release.FromTag(...)</c> 与方法自身冲突，报 CS0119
    /// （同 D-051 记录的"名字与被遮蔽类型同名"陷阱，本项目已第三次遇到）。
    /// </remarks>
    private static Release Rel(string tag, bool isDraft = false, bool isPrerelease = false)
        => Release.FromTag(Repo(), tag, isDraft: isDraft, isPrerelease: isPrerelease);

    private static ReleaseDetectionService ServiceWith(params Release[] releases)
        => new(new FakeReleaseFeed(releases));

    // ---------- CheckAsync：取数与委托 ----------

    [Fact]
    public async Task CheckAsync_WithNewerRelease_ReportsUpdate()
    {
        var service = ServiceWith(Rel("v1.0.0"), Rel("v1.1.0"));

        var result = await service.CheckAsync(Repo(), Version("1.0.0"));

        Assert.Equal(DetectionOutcomeKind.UpdateAvailable, result.Outcome.Kind);
        Assert.True(result.ShouldNotify);
        Assert.Equal("1.1.0", result.LatestVersionText);
    }

    [Fact]
    public async Task CheckAsync_WithSameVersion_ReportsUpToDate()
    {
        var service = ServiceWith(Rel("v1.2.0"));

        var result = await service.CheckAsync(Repo(), Version("1.2.0"));

        Assert.Equal(DetectionOutcomeKind.UpToDate, result.Outcome.Kind);
        Assert.False(result.ShouldNotify);
    }

    [Fact]
    public async Task CheckAsync_FirstTime_ReportsNoBaseline()
    {
        var service = ServiceWith(Rel("v1.2.0"));

        var result = await service.CheckAsync(Repo(), knownVersion: null);

        Assert.Equal(DetectionOutcomeKind.NoBaseline, result.Outcome.Kind);
        Assert.False(result.ShouldNotify);
    }

    [Fact]
    public async Task CheckAsync_PassesRepositoryToFeed()
    {
        var feed = new FakeReleaseFeed([Rel("v1.0.0")]);
        var service = new ReleaseDetectionService(feed);

        await service.CheckAsync(Repo("owner/repo"), Version("0.9.0"));

        Assert.Equal(1, feed.CallCount);
        Assert.Equal("owner/repo", feed.LastRequestedRepository?.ToString());
    }

    [Fact]
    public async Task CheckAsync_PropagatesFeedException()
    {
        // "取不到数据"必须与"确实没有"区分开：吞掉异常会让网络故障显示成"暂无更新"
        var feed = new FakeReleaseFeed
        {
            ThrowOnCall = new ReleaseFeedException("网络不可达"),
        };
        var service = new ReleaseDetectionService(feed);

        await Assert.ThrowsAsync<ReleaseFeedException>(
            () => service.CheckAsync(Repo(), Version("1.0.0")));
    }

    [Fact]
    public void Constructor_WithNullFeed_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ReleaseDetectionService(null!));

    [Fact]
    public async Task CheckAsync_WithNullRepository_Throws()
    {
        var service = ServiceWith(Rel("v1.0.0"));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.CheckAsync(null!, Version("1.0.0")));
    }

    // ---------- 空与全过滤场景 ----------

    [Fact]
    public async Task CheckAsync_EmptyFeed_ReportsNoCandidate()
    {
        var service = ServiceWith();

        var result = await service.CheckAsync(Repo(), Version("1.0.0"));

        Assert.Equal(DetectionOutcomeKind.NoCandidate, result.Outcome.Kind);
        Assert.Null(result.LatestRelease);
        Assert.Null(result.LatestVersionText);
    }

    [Fact]
    public async Task CheckAsync_OnlyDrafts_ExplainsDraftNotEmpty()
    {
        // 仓库明明有发布记录，不能笼统报"暂无 Release"——
        // 那会让用户误以为数据源出了问题
        var service = ServiceWith(
            Rel("v2.0.0", isDraft: true),
            Rel("v1.0.0", isDraft: true));

        var result = await service.CheckAsync(Repo(), Version("1.0.0"));

        Assert.Equal(DetectionOutcomeKind.IgnoredDraft, result.Outcome.Kind);
        Assert.Contains("草稿", result.Outcome.Reason);
    }

    [Fact]
    public async Task CheckAsync_OnlyPrereleaseWithStableOnly_ExplainsChannel()
    {
        var service = ServiceWith(
            Rel("v2.0.0-beta.1", isPrerelease: true));

        var result = await service.CheckAsync(Repo(), Version("1.0.0"));

        Assert.Equal(DetectionOutcomeKind.IgnoredPrerelease, result.Outcome.Kind);
        Assert.Contains("只接收正式版", result.Outcome.Reason);
    }

    [Fact]
    public async Task CheckAsync_OnlyUnparsableTags_ExplainsTagProblem()
    {
        var service = ServiceWith(Rel("nightly"));

        var result = await service.CheckAsync(Repo(), Version("1.0.0"));

        Assert.Equal(DetectionOutcomeKind.IgnoredUnparsableTag, result.Outcome.Kind);
    }

    [Fact]
    public async Task CheckAsync_MixedFilters_TakesLatestQualifying()
    {
        // 草稿版本号更高也不该被选中
        var service = ServiceWith(
            Rel("v1.0.0"),
            Rel("v1.1.0", isDraft: true),
            Rel("v1.2.0-beta.1", isPrerelease: true));

        var result = await service.CheckAsync(Repo(), Version("1.0.0"));

        Assert.Equal(DetectionOutcomeKind.UpToDate, result.Outcome.Kind);
        Assert.Equal("1.0.0", result.LatestVersionText);
    }

    [Fact]
    public async Task CheckAsync_IncludePrereleaseChannel_PicksPrerelease()
    {
        var service = ServiceWith(
            Rel("v1.0.0"),
            Rel("v2.0.0-beta.1", isPrerelease: true));

        var result = await service.CheckAsync(
            Repo(), Version("1.0.0"), PrereleaseChannel.IncludePrerelease);

        Assert.Equal(DetectionOutcomeKind.UpdateAvailable, result.Outcome.Kind);
        Assert.Equal("2.0.0-beta.1", result.LatestVersionText);
    }

    // ---------- Evaluate：离线的判定入口 ----------

    [Fact]
    public void Evaluate_WithNullReleases_ReportsNoCandidate()
    {
        var service = ServiceWith();

        var result = service.Evaluate(Repo(), null, Version("1.0.0"));

        Assert.Equal(DetectionOutcomeKind.NoCandidate, result.Outcome.Kind);
    }

    [Fact]
    public void Evaluate_DoesNotTouchFeed()
    {
        // Evaluate 的意义之一就是"对已缓存数据重复判定而不重新请求"
        var feed = new FakeReleaseFeed([Rel("v2.0.0")]);
        var service = new ReleaseDetectionService(feed);

        var result = service.Evaluate(Repo(), [Rel("v2.0.0")], Version("1.0.0"));

        Assert.Equal(DetectionOutcomeKind.UpdateAvailable, result.Outcome.Kind);
        Assert.Equal(0, feed.CallCount);
    }

    [Fact]
    public void Evaluate_CarriesRepositoryAndKnownVersion()
    {
        var service = ServiceWith(Rel("v2.0.0"));

        var result = service.Evaluate(Repo("owner/repo"), [Rel("v2.0.0")], Version("1.0.0"));

        Assert.Equal("owner/repo", result.Repository.ToString());
        Assert.Equal("1.0.0", result.KnownVersion?.ToString());
    }

    [Fact]
    public void Evaluate_WithNullRepository_Throws()
    {
        var service = ServiceWith();

        Assert.Throws<ArgumentNullException>(
            () => service.Evaluate(null!, [], Version("1.0.0")));
    }

    // ---------- RepositoryCheckResult ----------

    [Fact]
    public void Result_WithNoLatestRelease_HasNoVersionText()
    {
        var result = new RepositoryCheckResult(
            Repo(),
            null,
            new DetectionOutcome(DetectionOutcomeKind.NoCandidate, "无"),
            null);

        Assert.Null(result.LatestVersionText);
        Assert.False(result.ShouldNotify);
    }

    [Fact]
    public void Result_ShouldNotify_MirrorsOutcome()
    {
        foreach (DetectionOutcomeKind kind in Enum.GetValues<DetectionOutcomeKind>())
        {
            var result = new RepositoryCheckResult(
                Repo(), null, new DetectionOutcome(kind, "原因"), null);

            Assert.Equal(kind == DetectionOutcomeKind.UpdateAvailable, result.ShouldNotify);
        }
    }

    [Fact]
    public async Task Result_CarriesLatestReleaseWithAssets()
    {
        // UI 需要一并拿到资产信息，不应反手再去问 feed 要数据。
        //
        // 用 FromTag（会从 tag 解析版本号）而非 Create ——
        // Create 不做解析，未显式传 version 时 Version 为 null，
        // 会被 SelectLatest 当作"版本不可解析"过滤掉（见下一条用例）。
        var asset = ReleaseAsset.Create(
            "app-setup.exe", new Uri("https://example.com/app-setup.exe"), 2048);

        var release = Release.FromTag(Repo(), "v2.0.0", assets: [asset]);
        var service = new ReleaseDetectionService(new FakeReleaseFeed([release]));

        var result = await service.CheckAsync(Repo(), Version("1.0.0"));

        Assert.NotNull(result.LatestRelease);
        Assert.Single(result.LatestRelease!.Assets);
        Assert.Equal("app-setup.exe", result.LatestRelease.Assets[0].Name);
    }

    [Fact]
    public async Task CheckAsync_ReleaseWithoutParsedVersion_IsFiltered()
    {
        // 防回归：Release.Create 不从 tag 解析版本号，未显式传 version 时
        // Version 为 null，会被规则判定为"版本不可解析"而过滤。
        // 两个工厂方法（Create / FromTag）在这一点上行为不同，
        // 误用会让 Release 静默消失 —— 不是抛异常，而是查不到任何更新。
        var release = Release.Create(Repo(), "v2.0.0");

        Assert.Null(release.Version);

        var service = new ReleaseDetectionService(new FakeReleaseFeed([release]));
        var result = await service.CheckAsync(Repo(), Version("1.0.0"));

        Assert.Equal(DetectionOutcomeKind.IgnoredUnparsableTag, result.Outcome.Kind);
        Assert.Null(result.LatestRelease);
    }
}
