using ReleaseWatcher.App.ViewModels;
using ReleaseWatcher.Application.Services;
using ReleaseWatcher.Domain.Entities;
using ReleaseWatcher.Domain.Rules;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.App.Tests;

/// <summary>
/// <see cref="RepositoryRowViewModel"/> 携带 Release 历史与最新项的构造验证。
/// </summary>
public class RepositoryRowViewModelTests
{
    private static RepositoryRef Repo()
        => RepositoryRef.TryParse("dotnet/runtime", out var r)
            ? r!
            : throw new InvalidOperationException("测试数据构造失败");

    [Fact]
    public void Construct_WithReleases_CarriesFullHistoryAndLatest()
    {
        // 中栏需要完整历史、右栏需要知道"最新合格"项，二者都必须透传。
        var releases = new List<Release>
        {
            Release.FromTag(Repo(), "v1.0.0", title: "1.0.0"),
            Release.FromTag(Repo(), "v1.1.0", title: "1.1.0"),
        };

        var result = new RepositoryCheckResult(
            Repo(),
            releases[0],
            new DetectionOutcome(DetectionOutcomeKind.UpdateAvailable, "有新版本"),
            null);

        var row = new RepositoryRowViewModel(result, releases);

        Assert.Equal(2, row.Releases.Count);
        Assert.Same(releases[0], row.LatestRelease);
        Assert.Equal("v1.0.0", row.TagText);
    }

    [Fact]
    public void CreateError_WithoutReleases_KeepsEmptyHistory()
    {
        // 错误行 / 无 Release 的仓库：Releases 应为空、LatestRelease 为 null，
        // 让中栏/右栏显示空态而非引用到不存在的对象。
        var row = RepositoryRowViewModel.CreateError(Repo(), "检测失败");

        Assert.Empty(row.Releases);
        Assert.Null(row.LatestRelease);
    }

    [Fact]
    public void Construct_SingleArg_KeepsLatestButEmptyHistory()
    {
        // 单参构造（不需要历史的场景，如概览摘要）仍应透传 LatestRelease，
        // 只是不携带完整历史。
        var latest = Release.FromTag(Repo(), "v2.0.0");
        var result = new RepositoryCheckResult(
            Repo(),
            latest,
            new DetectionOutcome(DetectionOutcomeKind.UpToDate, "无候选"),
            null);

        var row = new RepositoryRowViewModel(result);

        Assert.Empty(row.Releases);
        Assert.Same(latest, row.LatestRelease);
    }
}
