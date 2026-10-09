namespace ReleaseWatcher.Domain.ValueObjects;

/// <summary>
/// GitHub 仓库引用（<c>owner/name</c>）的值对象。
/// </summary>
/// <remarks>
/// 检测以"仓库"为单位组织（D-002 双检测模式均围绕单个仓库的 releases 展开），
/// 因此仓库标识是检测流程的天然聚合键，值得成为值对象：
/// 可作字典键去重，也天然约束"owner 与 name 都不能为空"。
/// </remarks>
public sealed class RepositoryRef : IEquatable<RepositoryRef>
{
    private RepositoryRef(string owner, string name)
    {
        Owner = owner;
        Name = name;
    }

    /// <summary>仓库所有者（用户或组织）。</summary>
    public string Owner { get; }

    /// <summary>仓库名称。</summary>
    public string Name { get; }

    /// <summary>
    /// 由 <c>owner/name</c> 文本构造仓库引用。
    /// </summary>
    /// <param name="ownerOrSlug">
    /// 形如 <c>dotnet/runtime</c> 的 <c>owner/name</c>；也接受 <c>https://github.com/owner/name</c>
    /// 形式（取路径末两段），这是用户在设置页粘贴的常见形态。
    /// </param>
    /// <param name="reference">构造成功时输出。</param>
    /// <returns>能否得到非空的 owner 与 name。</returns>
    /// <remarks>
    /// 不抛异常，与 <see cref="SemanticVersion.TryParse"/> 保持同一风格：
    /// 用户输入无效是正常业务情况，由调用方决定如何提示。
    /// </remarks>
    public static bool TryParse(string? ownerOrSlug, out RepositoryRef? reference)
    {
        reference = null;

        if (string.IsNullOrWhiteSpace(ownerOrSlug))
        {
            return false;
        }

        var text = ownerOrSlug.Trim().TrimEnd('/');

        // 允许粘贴完整 URL：取 host 之后的路径末两段
        var schemeIndex = text.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
        {
            var afterScheme = text[(schemeIndex + 3)..];
            var firstSlash = afterScheme.IndexOf('/');
            if (firstSlash < 0)
            {
                return false;
            }

            text = afterScheme[(firstSlash + 1)..];
        }

        var segments = text.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // 去掉可能的 .git 后缀后再判断段数
        if (segments.Length == 2 && segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            segments[1] = segments[1][..^4];
        }

        if (segments.Length != 2)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(segments[0]) || string.IsNullOrWhiteSpace(segments[1]))
        {
            return false;
        }

        reference = new RepositoryRef(segments[0], segments[1]);
        return true;
    }

    /// <inheritdoc />
    public bool Equals(RepositoryRef? other)
        => other is not null
           && string.Equals(Owner, other.Owner, StringComparison.OrdinalIgnoreCase)
           && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RepositoryRef);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(
            Owner.ToLowerInvariant(),
            Name.ToLowerInvariant());

    /// <inheritdoc />
    public override string ToString() => $"{Owner}/{Name}";
}