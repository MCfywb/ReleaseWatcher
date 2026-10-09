using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Domain.Entities;

/// <summary>
/// 一个被监控仓库的已发现 Release —— Domain 层聚合根。
/// </summary>
/// <remarks>
/// <para>
/// 只承载"业务上需要判定的东西"：仓库、版本号、发布通道、草稿/预发布标记、
/// 发布时间、以及稳定资产地址。<b>不承载任何获取方式</b> ——
/// 数据来自 <c>/releases/latest</c> 还是 REST API，由 Infrastructure 决定（<c>D-002</c>）。
/// </para>
/// <para>
/// <b>关于资产地址（D-012）：</b>本类型只保存 GitHub 稳定 Asset URL；
/// 解析出的临时下载链接不进入领域模型，也不入库 —— 那是下载时刻的瞬时状态。
/// </para>
/// <para>
/// 实体不可变：检测流程每次产生一个新实例而不是修改既有实例，
/// 这样"上次已知版本"与"本次检测结果"不会互相污染。
/// </para>
/// </remarks>
public sealed class Release : IEquatable<Release>
{
    private Release(
        RepositoryRef repository,
        SemanticVersion? version,
        string tagName,
        string title,
        bool isDraft,
        bool isPrerelease,
        DateTimeOffset publishedAt,
        IReadOnlyList<ReleaseAsset> assets,
        Uri? htmlUrl)
    {
        Repository = repository;
        Version = version;
        TagName = tagName;
        Title = title;
        IsDraft = isDraft;
        IsPrerelease = isPrerelease;
        PublishedAt = publishedAt;
        Assets = assets;
        HtmlUrl = htmlUrl;
    }

    /// <summary>所属仓库标识。</summary>
    public RepositoryRef Repository { get; }

    /// <summary>
    /// 解析出的版本号。
    /// </summary>
    /// <remarks>
    /// 可能为 <c>null</c>：tag 命名不可解析（如 <c>nightly</c>、日期串）时无法比较版本。
    /// 这类 Release 仍可展示，但无法参与"是否更新"的判断 —— 降级处理由
    /// <see cref="Rules.ReleaseDetectionRules"/> 负责。
    /// </remarks>
    public SemanticVersion? Version { get; }

    /// <summary>原始 tag 文本，未经规范化，始终保留。</summary>
    public string TagName { get; }

    /// <summary>展示用标题；为空时调用方可退回 <see cref="TagName"/>。</summary>
    public string Title { get; }

    /// <summary>是否草稿。草稿不对外可见，检测流程应忽略。</summary>
    public bool IsDraft { get; }

    /// <summary>是否预发布。</summary>
    public bool IsPrerelease { get; }

    /// <summary>发布时间。</summary>
    public DateTimeOffset PublishedAt { get; }

    /// <summary>稳定资产地址列表（不含临时下载链接，见 D-012）。</summary>
    public IReadOnlyList<ReleaseAsset> Assets { get; }

    /// <summary>GitHub 上的发布页地址。</summary>
    public Uri? HtmlUrl { get; }

    /// <summary>
    /// 构造一个 Release 实例。
    /// </summary>
    /// <param name="repository">所属仓库引用。</param>
    /// <param name="tagName">原始 tag 文本；为空时以 <paramref name="version"/> 的规范文本兜底。</param>
    /// <param name="title">展示标题，可为空。</param>
    /// <param name="version">已解析的版本号，可为空。</param>
    /// <param name="isDraft">是否为草稿。</param>
    /// <param name="isPrerelease">是否为预发布；为 <c>null</c> 时由 <paramref name="version"/> 是否含预发布标识推断。</param>
    /// <param name="publishedAt">发布时间。</param>
    /// <param name="assets">稳定资产地址列表。</param>
    /// <param name="htmlUrl">发布页地址，可为空。</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="repository"/> 为空，或 <paramref name="tagName"/> 为空且无法由版本号兜底。
    /// </exception>
    /// <remarks>
    /// 这里对"必须有 tag"抛异常而非返回失败：Release 一旦存在就必须能被指认，
    /// 这是领域不变量；而"tag 解析不出版本号"不是不变量，所以留给 <c>Version</c> 为空。
    /// </remarks>
    public static Release Create(
        RepositoryRef repository,
        string? tagName,
        string? title = null,
        SemanticVersion? version = null,
        bool isDraft = false,
        bool? isPrerelease = null,
        DateTimeOffset? publishedAt = null,
        IReadOnlyList<ReleaseAsset>? assets = null,
        Uri? htmlUrl = null)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var effectiveTag = string.IsNullOrWhiteSpace(tagName)
            ? version?.ToString()
            : tagName.Trim();

        if (string.IsNullOrWhiteSpace(effectiveTag))
        {
            throw new ArgumentException(
                "Release 必须有 tag 名称或可解析的版本号，两者不能同时为空。",
                nameof(tagName));
        }

        return new Release(
            repository,
            version,
            effectiveTag!,
            string.IsNullOrWhiteSpace(title) ? effectiveTag! : title.Trim(),
            isDraft,
            isPrerelease ?? version?.IsPrerelease ?? false,
            publishedAt ?? default,
            assets ?? Array.Empty<ReleaseAsset>(),
            htmlUrl);
    }

    /// <summary>
    /// 便捷构造：先按文本解析版本号，再创建 Release。
    /// </summary>
    /// <remarks>
    /// 解析失败不抛异常，<c>Version</c> 留空 —— 这是真实 tag 命名千差万别时的正常降级。
    /// </remarks>
    public static Release FromTag(
        RepositoryRef repository,
        string? tagName,
        string? title = null,
        bool isDraft = false,
        bool? isPrerelease = null,
        DateTimeOffset? publishedAt = null,
        IReadOnlyList<ReleaseAsset>? assets = null,
        Uri? htmlUrl = null)
    {
        SemanticVersion.TryParse(tagName, out var version);

        return Create(
            repository,
            tagName,
            title,
            version,
            isDraft,
            isPrerelease,
            publishedAt,
            assets,
            htmlUrl);
    }

    /// <inheritdoc />
    public bool Equals(Release? other)
        => other is not null
           && Repository.Equals(other.Repository)
           && string.Equals(TagName, other.TagName, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Release);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Repository, TagName);

    /// <inheritdoc />
    public override string ToString() => $"{Repository}@{TagName}";
}