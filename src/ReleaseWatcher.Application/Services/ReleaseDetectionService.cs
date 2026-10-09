using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Domain.Entities;
using ReleaseWatcher.Domain.Enums;
using ReleaseWatcher.Domain.Exceptions;
using ReleaseWatcher.Domain.Rules;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Application.Services;

/// <summary>
/// 单个仓库的 Release 检测用例。
/// </summary>
/// <remarks>
/// <para>
/// <b>职责边界：编排，不判断。</b>
/// 具体"什么算更新"的规则全部在 <see cref="ReleaseDetectionRules"/>（Domain 层），
/// 本类只负责取数、调用规则、把结果整理成 UI 直接可用的形状。
/// 这样规则能被最廉价的单元测试覆盖，也不会随数据来源更换而失效。
/// </para>
/// <para>
/// <b>为何不直接返回 <see cref="DetectionOutcome"/>：</b>
/// 界面需要的不只是"是否要提示"，还包括"最新版本是哪个""有哪些资产"。
/// 本用例返回 <see cref="RepositoryCheckResult"/>，把这些一次带齐，
/// 避免 UI 反手再去问 feed 要数据（那会让用例失去编排价值）。
/// </para>
/// <para>
/// <b>并发：</b>本类无状态，<see cref="CheckAsync"/> 可安全并发调用。
/// 每次调用只依赖入参与注入的 feed。
/// </para>
/// </remarks>
public sealed class ReleaseDetectionService
{
    private readonly IReleaseFeed _feed;

    /// <summary>构造检测服务。</summary>
    /// <param name="feed">Release 数据来源。</param>
    public ReleaseDetectionService(IReleaseFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);

        _feed = feed;
    }

    /// <summary>
    /// 检测单个仓库是否存在比已知版本更新的 Release。
    /// </summary>
    /// <param name="repository">目标仓库。</param>
    /// <param name="knownVersion">
    /// 上次已知版本号；为 <c>null</c> 表示首次检测该仓库。
    /// </param>
    /// <param name="channel">预发布接收策略。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>检测结果，含最新 Release 与面向用户的原因说明。</returns>
    /// <exception cref="ReleaseFeedException">
    /// 数据来源不可达 —— 由 <see cref="IReleaseFeed"/> 抛出，本方法不吞掉。
    /// </exception>
    public async Task<RepositoryCheckResult> CheckAsync(
        RepositoryRef repository,
        SemanticVersion? knownVersion,
        PrereleaseChannel channel = PrereleaseChannel.StableOnly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var releases = await _feed.GetReleasesAsync(repository, cancellationToken);

        return Evaluate(repository, releases, knownVersion, channel);
    }

    /// <summary>
    /// 对已取回的 Release 列表执行检测，不涉及I/O。
    /// </summary>
    /// <param name="repository">目标仓库，用于填充结果。</param>
    /// <param name="releases">原始 Release 列表。</param>
    /// <param name="knownVersion">上次已知版本号；<c>null</c> 表示首次检测。</param>
    /// <param name="channel">预发布接收策略。</param>
    /// <returns>检测结果。</returns>
    /// <remarks>
    /// <b>与 <see cref="CheckAsync"/> 的分工：</b>本方法把"取数"与"判定"分开，
    /// 因此可以在不碰网络的前提下用固定数据测试判定逻辑，
    /// 也便于 UI 层对<em>已缓存</em>的数据重复判定而不重新请求。
    /// </remarks>
    public RepositoryCheckResult Evaluate(
        RepositoryRef repository,
        IReadOnlyList<Release>? releases,
        SemanticVersion? knownVersion,
        PrereleaseChannel channel = PrereleaseChannel.StableOnly)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (releases is null || releases.Count == 0)
        {
            return new RepositoryCheckResult(
                repository,
                null,
                new DetectionOutcome(DetectionOutcomeKind.NoCandidate, "该仓库暂无 Release"),
                knownVersion);
        }

        var latest = ReleaseDetectionRules.SelectLatest(releases, channel);

        // 全部候选都被过滤掉（草稿 / 预发布 / 版本不可解析）时，
        // SelectLatest 返回 null。此时不能笼统报"暂无 Release"，
        // 而应说明"有 Release 但都不满足当前策略"。
        var outcome = latest is null
            ? ExplainAllFiltered(releases, channel)
            : ReleaseDetectionRules.Evaluate(latest, knownVersion, channel);

        return new RepositoryCheckResult(repository, latest, outcome, knownVersion);
    }

    /// <summary>
    /// 当所有候选都被过滤掉时，生成一条具体的原因说明。
    /// </summary>
    /// <remarks>
    /// 不返回笼统的"暂无 Release"：仓库里明明有发布记录，
    /// 告诉用户"暂无"会让人以为是数据源出了问题。
    /// 这里按被过滤的主因给出一句话，让用户知道下一步该改什么设置。
    /// </remarks>
    private static DetectionOutcome ExplainAllFiltered(
        IReadOnlyList<Release> releases,
        PrereleaseChannel channel)
    {
        var anyDraft = releases.Any(r => r.IsDraft);
        var anyPrerelease = releases.Any(r => r.IsPrerelease);
        var anyUnparsable = releases.Any(r => r.Version is null);

        if (anyDraft && releases.All(r => r.IsDraft))
        {
            return new DetectionOutcome(
                DetectionOutcomeKind.IgnoredDraft, "该仓库目前只有草稿，未公开发布");
        }

        if (channel == PrereleaseChannel.StableOnly && anyPrerelease)
        {
            return new DetectionOutcome(
                DetectionOutcomeKind.IgnoredPrerelease,
                "该仓库目前只有预发布版本，当前设置只接收正式版");
        }

        if (anyUnparsable)
        {
            return new DetectionOutcome(
                DetectionOutcomeKind.IgnoredUnparsableTag,
                "该仓库的 Release 均非可识别的版本号，无法比较");
        }

        return new DetectionOutcome(DetectionOutcomeKind.NoCandidate, "没有符合条件的 Release");
    }
}

/// <summary>
/// 单仓库检测结果。
/// </summary>
/// <param name="Repository">被检测的仓库。</param>
/// <param name="LatestRelease">
/// 筛选出的最新合格 Release；无合格候选时为 <c>null</c>。
/// </param>
/// <param name="Outcome">判定结果与面向用户的原因。</param>
/// <param name="KnownVersion">本次比较所依据的已知版本号，可为 <c>null</c>。</param>
/// <remarks>
/// 设计为只读记录：检测结果一旦产生就不应被修改，
/// 避免 UI 多次读取时拿到不一致的内容。
/// </remarks>
public readonly record struct RepositoryCheckResult(
    RepositoryRef Repository,
    Release? LatestRelease,
    DetectionOutcome Outcome,
    SemanticVersion? KnownVersion)
{
    /// <summary>是否应提示用户有可更新的版本。</summary>
    public bool ShouldNotify => Outcome.ShouldNotify;

    /// <summary>
    /// 最新版本号文本；无合格候选时为 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 供界面直接绑定显示，避免在XAML 里反复取 <c>LatestRelease.Version</c>
    /// 并处理其中的空值分支。
    /// </remarks>
    public string? LatestVersionText => LatestRelease?.Version?.ToString();
}