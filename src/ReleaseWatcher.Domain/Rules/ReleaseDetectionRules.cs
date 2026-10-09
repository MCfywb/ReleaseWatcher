using ReleaseWatcher.Domain.Entities;
using ReleaseWatcher.Domain.Enums;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Domain.Rules;

/// <summary>
/// Release 检测的判定结果。
/// </summary>
/// <param name="Kind">结果类别。</param>
/// <param name="Reason">面向用户的中文原因说明。</param>
public readonly record struct DetectionOutcome(DetectionOutcomeKind Kind, string Reason)
{
    /// <summary>是否应提示用户有可更新的版本。</summary>
    public bool ShouldNotify => Kind == DetectionOutcomeKind.UpdateAvailable;
}

/// <summary>
/// 检测结果的分类。
/// </summary>
public enum DetectionOutcomeKind
{
    /// <summary>存在比已知版本更新的 Release。</summary>
    UpdateAvailable = 0,

    /// <summary>已知的最新版本，没有更新。</summary>
    UpToDate = 1,

    /// <summary>首次见到该仓库，没有"上次版本"可比，视为已是最新。</summary>
    NoBaseline = 2,

    /// <summary>候选为草稿，不对外发布，忽略。</summary>
    IgnoredDraft = 3,

    /// <summary>候选为预发布版，但用户策略只接受正式版。</summary>
    IgnoredPrerelease = 4,

    /// <summary>候选版本号无法解析，无法比较；已忽略。</summary>
    IgnoredUnparsableTag = 5,

    /// <summary>没有候选 Release（仓库为空或全部被过滤）。</summary>
    NoCandidate = 6,
}

/// <summary>
/// Release 检测的纯领域规则。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么规则放在 Domain 而不是 Application：</b>
/// "预发布版算不算更新"是业务规则本身，与从 GitHub 还是本地缓存取到数据无关。
/// 放在领域层可以用最廉价的单元测试覆盖边界，且不会随数据源更换而失效。
/// </para>
/// <para>
/// <b>本类型不碰 I/O。</b>没有 HTTP、文件、注册表依赖（Domain 层零引用，AGENTS.md 1.2），
/// 输入来自 <see cref="Release"/> 与"上次已知版本"，输出是 <see cref="DetectionOutcome"/>。
/// </para>
/// <para>
/// 依据：<c>DECISIONS.md</c> D-002（双检测模式均以单仓库 releases 为单位）、
/// D-012（只认稳定资产地址）、D-027（一级导航四区）。
/// </para>
/// </remarks>
public static class ReleaseDetectionRules
{
    /// <summary>
    /// 判断 <paramref name="candidate"/> 相对 <paramref name="knownVersion"/> 是否构成更新。
    /// </summary>
    /// <param name="candidate">本次检测到的候选 Release；为空表示无可选版本。</param>
    /// <param name="knownVersion">上次已知版本号；为空表示首次检测该仓库。</param>
    /// <param name="channel">用户的预发布接收策略。</param>
    /// <returns>
    /// 判定结果，含面向用户的中文原因。
    /// </returns>
    /// <remarks>
    /// 判定顺序固定为：空候选 → 草稿 → 预发布策略 → 版本可解析性 → 版本比较。
    /// 这个顺序保证降级信息最具体：用户看到"该版本是草稿"而不是笼统的"已是最新"。
    /// </remarks>
    public static DetectionOutcome Evaluate(
        Release? candidate,
        SemanticVersion? knownVersion,
        PrereleaseChannel channel)
    {
        // channel 是枚举值类型，不会为 null，无需 ArgumentNullException 校验（否则触发 CA2264）。

        if (candidate is null)
        {
            return new DetectionOutcome(DetectionOutcomeKind.NoCandidate, "未获取到任何 Release");
        }

        // 草稿对外不可见，先于一切版本判断
        if (candidate.IsDraft)
        {
            return new DetectionOutcome(
                DetectionOutcomeKind.IgnoredDraft,
                $"「{candidate.TagName}」是草稿，未公开发布");
        }

        // 预发布策略：只接受正式版时，预发布一律忽略
        if (channel == PrereleaseChannel.StableOnly && candidate.IsPrerelease)
        {
            return new DetectionOutcome(
                DetectionOutcomeKind.IgnoredPrerelease,
                $"「{candidate.TagName}」是预发布版本，当前设置只接收正式版");
        }

        // 版本号解析不出版本号 → 无法比较，降级为忽略而非误报
        if (candidate.Version is null)
        {
            return new DetectionOutcome(
                DetectionOutcomeKind.IgnoredUnparsableTag,
                $"「{candidate.TagName}」不是可识别的版本号，无法比较");
        }

        // 无基线可比：首次见到该仓库，不提示更新
        if (knownVersion is null)
        {
            return new DetectionOutcome(
                DetectionOutcomeKind.NoBaseline,
                $"首次记录「{candidate.TagName}」，后续以此为基准");
        }

        // 关键：严格大于。等于不算更新（重复检测会反复命中同一版本）
        if (candidate.Version > knownVersion)
        {
            return new DetectionOutcome(
                DetectionOutcomeKind.UpdateAvailable,
                $"发现新版本 {candidate.Version}（当前 {knownVersion}）");
        }

        return new DetectionOutcome(
            DetectionOutcomeKind.UpToDate,
            $"已是最新版本 {knownVersion}");
    }

    /// <summary>
    /// 在候选集合中挑选"最新且应纳入检测"的那一个。
    /// </summary>
    /// <param name="candidates">同一仓库的候选 Release 集合。</param>
    /// <param name="channel">用户的预发布接收策略。</param>
    /// <returns>
    /// 最新的合格候选；没有合格候选时返回 <c>null</c>。
    /// </returns>
    /// <remarks>
    /// 过滤规则与 <see cref="Evaluate"/> 保持一致（去草稿、按通道去预发布、去不可解析版本），
    /// 然后按版本比较取最大；版本相同则取发布时间较晚者（同一版本号的重复发布）。
    /// 集合为空时返回 <c>null</c>，由 <see cref="Evaluate"/> 产出 <c>NoCandidate</c>。
    /// </remarks>
    public static Release? SelectLatest(
        IEnumerable<Release>? candidates,
        PrereleaseChannel channel)
    {
        if (candidates is null)
        {
            return null;
        }

        Release? best = null;

        foreach (var candidate in candidates)
        {
            if (candidate is null || candidate.IsDraft)
            {
                continue;
            }

            if (channel == PrereleaseChannel.StableOnly && candidate.IsPrerelease)
            {
                continue;
            }

            if (candidate.Version is null)
            {
                continue;
            }

            if (best is null)
            {
                best = candidate;
                continue;
            }

            var versionComparison = candidate.Version.CompareTo(best.Version);

            if (versionComparison > 0)
            {
                best = candidate;
            }
            else if (versionComparison == 0 && candidate.PublishedAt > best.PublishedAt)
            {
                // 同版本号重复发布：取较晚发布的那个
                best = candidate;
            }
        }

        return best;
    }
}