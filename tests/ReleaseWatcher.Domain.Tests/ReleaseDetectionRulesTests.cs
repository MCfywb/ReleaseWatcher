using ReleaseWatcher.Domain.Entities;
using ReleaseWatcher.Domain.Enums;
using ReleaseWatcher.Domain.Rules;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Domain.Tests;

/// <summary>
/// <see cref="ReleaseDetectionRules"/> 的判定与筛选测试。
/// </summary>
/// <remarks>
/// 这些测试锁定的核心不变量：
/// 严格大于才算更新（等于不提示）、草稿与不可解析 tag 一律忽略、
/// 降级原因必须具体可读（用户看到"是草稿"而非笼统的"已是最新"）。
/// </remarks>
public class ReleaseDetectionRulesTests
{
    private static RepositoryRef Repo() => RepositoryRef.TryParse("dotnet/runtime", out var r)
        ? r!
        : throw new InvalidOperationException("测试数据构造失败");

    private static SemanticVersion Version(string tag)
        => SemanticVersion.TryParse(tag, out var v) && v is not null
            ? v
            : throw new InvalidOperationException($"测试数据 {tag} 无法解析为版本号");

    [Fact]
    public void Evaluate_HigherVersion_ReportsUpdateAvailable()
    {
        var candidate = Release.FromTag(Repo(), "v1.3.0");

        var outcome = ReleaseDetectionRules.Evaluate(
            candidate, Version("1.2.0"), PrereleaseChannel.StableOnly);

        Assert.Equal(DetectionOutcomeKind.UpdateAvailable, outcome.Kind);
        Assert.True(outcome.ShouldNotify);
        Assert.Contains("1.3.0", outcome.Reason);
    }

    [Fact]
    public void Evaluate_SameVersion_ReportsUpToDate()
    {
        // 关键：严格大于。等于不算更新，否则重复检测会反复提示同一版本
        var candidate = Release.FromTag(Repo(), "v1.2.0");

        var outcome = ReleaseDetectionRules.Evaluate(
            candidate, Version("1.2.0"), PrereleaseChannel.StableOnly);

        Assert.Equal(DetectionOutcomeKind.UpToDate, outcome.Kind);
        Assert.False(outcome.ShouldNotify);
    }

    [Fact]
    public void Evaluate_LowerVersion_ReportsUpToDate()
    {
        // 降级到旧版本（用户切换到 release 分支）不算更新
        var candidate = Release.FromTag(Repo(), "v1.1.0");

        var outcome = ReleaseDetectionRules.Evaluate(
            candidate, Version("1.2.0"), PrereleaseChannel.StableOnly);

        Assert.Equal(DetectionOutcomeKind.UpToDate, outcome.Kind);
        Assert.False(outcome.ShouldNotify);
    }

    [Fact]
    public void Evaluate_NoKnownVersion_ReportsNoBaseline()
    {
        var candidate = Release.FromTag(Repo(), "v1.2.0");

        var outcome = ReleaseDetectionRules.Evaluate(candidate, null, PrereleaseChannel.StableOnly);

        Assert.Equal(DetectionOutcomeKind.NoBaseline, outcome.Kind);
        Assert.False(outcome.ShouldNotify);
        Assert.Contains("首次记录", outcome.Reason);
    }

    [Fact]
    public void Evaluate_NullCandidate_ReportsNoCandidate()
    {
        var outcome = ReleaseDetectionRules.Evaluate(
            null, Version("1.2.0"), PrereleaseChannel.StableOnly);

        Assert.Equal(DetectionOutcomeKind.NoCandidate, outcome.Kind);
        Assert.False(outcome.ShouldNotify);
    }

    [Fact]
    public void Evaluate_Draft_IsIgnored()
    {
        var draft = Release.FromTag(Repo(), "v2.0.0", isDraft: true);

        var outcome = ReleaseDetectionRules.Evaluate(
            draft, Version("1.2.0"), PrereleaseChannel.StableOnly);

        Assert.Equal(DetectionOutcomeKind.IgnoredDraft, outcome.Kind);
        Assert.False(outcome.ShouldNotify);
        Assert.Contains("草稿", outcome.Reason);
    }

    [Fact]
    public void Evaluate_PrereleaseWithStableOnly_IsIgnored()
    {
        var prerelease = Release.FromTag(Repo(), "v2.0.0-beta.1");

        var outcome = ReleaseDetectionRules.Evaluate(
            prerelease, Version("1.2.0"), PrereleaseChannel.StableOnly);

        Assert.Equal(DetectionOutcomeKind.IgnoredPrerelease, outcome.Kind);
        Assert.False(outcome.ShouldNotify);
    }

    [Fact]
    public void Evaluate_PrereleaseWithIncludeChannel_ReportsUpdate()
    {
        var prerelease = Release.FromTag(Repo(), "v2.0.0-beta.1");

        var outcome = ReleaseDetectionRules.Evaluate(
            prerelease, Version("1.2.0"), PrereleaseChannel.IncludePrerelease);

        Assert.Equal(DetectionOutcomeKind.UpdateAvailable, outcome.Kind);
        Assert.True(outcome.ShouldNotify);
    }

    [Fact]
    public void Evaluate_UnparsableTag_IsIgnored()
    {
        var nightly = Release.FromTag(Repo(), "nightly");

        var outcome = ReleaseDetectionRules.Evaluate(
            nightly, Version("1.2.0"), PrereleaseChannel.StableOnly);

        Assert.Equal(DetectionOutcomeKind.IgnoredUnparsableTag, outcome.Kind);
        Assert.False(outcome.ShouldNotify);
    }

    [Fact]
    public void Evaluate_DraftTakesPrecedenceOverPrerelease()
    {
        // 判定顺序固定：草稿先于预发布策略，保证原因最具体
        var candidate = Release.FromTag(
            Repo(), "v2.0.0-beta.1", isDraft: true, isPrerelease: true);

        var outcome = ReleaseDetectionRules.Evaluate(
            candidate, Version("1.2.0"), PrereleaseChannel.StableOnly);

        Assert.Equal(DetectionOutcomeKind.IgnoredDraft, outcome.Kind);
    }

    [Fact]
    public void SelectLatest_PicksHighestVersion()
    {
        var releases = new[]
        {
            Release.FromTag(Repo(), "v1.0.0"),
            Release.FromTag(Repo(), "v1.2.0"),
            Release.FromTag(Repo(), "v1.1.0"),
        };

        var latest = ReleaseDetectionRules.SelectLatest(releases, PrereleaseChannel.StableOnly);

        Assert.NotNull(latest);
        Assert.Equal("v1.2.0", latest!.TagName);
    }

    [Fact]
    public void SelectLatest_ExcludesDrafts()
    {
        var releases = new[]
        {
            Release.FromTag(Repo(), "v1.0.0"),
            Release.FromTag(Repo(), "v9.0.0", isDraft: true),
        };

        var latest = ReleaseDetectionRules.SelectLatest(releases, PrereleaseChannel.StableOnly);

        Assert.NotNull(latest);
        Assert.Equal("v1.0.0", latest!.TagName);
    }

    [Fact]
    public void SelectLatest_ExcludesPrereleaseWhenStableOnly()
    {
        var releases = new[]
        {
            Release.FromTag(Repo(), "v1.0.0"),
            Release.FromTag(Repo(), "v2.0.0-beta.1"),
        };

        var latest = ReleaseDetectionRules.SelectLatest(releases, PrereleaseChannel.StableOnly);

        Assert.NotNull(latest);
        Assert.Equal("v1.0.0", latest!.TagName);
    }

    [Fact]
    public void SelectLatest_IncludesPrereleaseWhenRequested()
    {
        var releases = new[]
        {
            Release.FromTag(Repo(), "v1.0.0"),
            Release.FromTag(Repo(), "v2.0.0-beta.1"),
        };

        var latest = ReleaseDetectionRules.SelectLatest(releases, PrereleaseChannel.IncludePrerelease);

        Assert.NotNull(latest);
        Assert.Equal("v2.0.0-beta.1", latest!.TagName);
    }

    [Fact]
    public void SelectLatest_ExcludesUnparsableTags()
    {
        var releases = new[]
        {
            Release.FromTag(Repo(), "nightly"),
            Release.FromTag(Repo(), "v1.0.0"),
        };

        var latest = ReleaseDetectionRules.SelectLatest(releases, PrereleaseChannel.StableOnly);

        Assert.NotNull(latest);
        Assert.Equal("v1.0.0", latest!.TagName);
    }

    [Fact]
    public void SelectLatest_SameVersionDifferentTime_PicksLaterPublish()
    {
        var earlier = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var version = Version("1.0.0");

        var releases = new[]
        {
            Release.Create(Repo(), "v1.0.0", version: version, publishedAt: earlier),
            Release.Create(Repo(), "v1.0.0-republish", version: version, publishedAt: later),
        };

        var latest = ReleaseDetectionRules.SelectLatest(releases, PrereleaseChannel.StableOnly);

        Assert.NotNull(latest);
        Assert.Equal(later, latest!.PublishedAt);
    }

    [Fact]
    public void SelectLatest_AllFiltered_ReturnsNull()
    {
        var releases = new[]
        {
            Release.FromTag(Repo(), "nightly"),
            Release.FromTag(Repo(), "v1.0.0-beta.1"),
        };

        var latest = ReleaseDetectionRules.SelectLatest(releases, PrereleaseChannel.StableOnly);

        Assert.Null(latest);
    }

    [Fact]
    public void SelectLatest_EmptyOrNull_ReturnsNull()
    {
        Assert.Null(ReleaseDetectionRules.SelectLatest([], PrereleaseChannel.StableOnly));
        Assert.Null(ReleaseDetectionRules.SelectLatest(null, PrereleaseChannel.StableOnly));
    }

    [Fact]
    public void ShouldNotify_OnlyTrueForUpdateAvailable()
    {
        // 防回归：ShouldNotify 是 ShouldNotify 判定唯一的真值来源
        foreach (DetectionOutcomeKind kind in Enum.GetValues<DetectionOutcomeKind>())
        {
            var outcome = new DetectionOutcome(kind, "原因");

            Assert.Equal(kind == DetectionOutcomeKind.UpdateAvailable, outcome.ShouldNotify);
        }
    }
}