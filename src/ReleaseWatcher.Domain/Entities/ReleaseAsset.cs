namespace ReleaseWatcher.Domain.Entities;

/// <summary>
/// Release 的一个下载资产（安装包 / 压缩包 / 校验文件）。
/// </summary>
/// <remarks>
/// <b>只存稳定地址。</b>依据 <c>DECISIONS.md</c> D-012：
/// GitHub Asset 稳定 URL → 临时下载链接的解析属于下载时刻的事，
/// 临时链接<b>不长期保存</b>，也不进入领域模型与数据库。
/// </remarks>
public sealed class ReleaseAsset : IEquatable<ReleaseAsset>
{
    private ReleaseAsset(string name, Uri downloadUri, long sizeInBytes, int downloadCount)
    {
        Name = name;
        DownloadUri = downloadUri;
        SizeInBytes = sizeInBytes;
        DownloadCount = downloadCount;
    }

    /// <summary>资产文件名。</summary>
    public string Name { get; }

    /// <summary>稳定的 GitHub Asset 下载地址。</summary>
    public Uri DownloadUri { get; }

    /// <summary>文件大小（字节）；未知时为 0。</summary>
    public long SizeInBytes { get; }

    /// <summary>下载次数；未知时为 0。</summary>
    public int DownloadCount { get; }

    /// <summary>
    /// 创建资产。
    /// </summary>
    /// <param name="name">资产文件名。</param>
    /// <param name="downloadUri">稳定下载地址。</param>
    /// <param name="sizeInBytes">文件大小，未知传 0。</param>
    /// <param name="downloadCount">下载次数，未知传 0。</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> 为空，或 <paramref name="downloadUri"/> 不是绝对 URL。
    /// </exception>
    /// <remarks>
    /// 与 <see cref="ReleaseAsset"/> 的"要么合法要么报错"不同，
    /// 这里对地址做了绝对性校验：资产地址是下载链路的入口，
    /// 放进来一个相对路径会让后续失败推迟到下载时刻，代价更高。
    /// </remarks>
    public static ReleaseAsset Create(string? name, Uri? downloadUri, long sizeInBytes = 0, int downloadCount = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("资产名称不能为空。", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(downloadUri);

        if (!downloadUri.IsAbsoluteUri)
        {
            throw new ArgumentException("资产下载地址必须是绝对 URL。", nameof(downloadUri));
        }

        return new ReleaseAsset(name.Trim(), downloadUri, sizeInBytes, downloadCount);
    }

    /// <inheritdoc />
    public bool Equals(ReleaseAsset? other)
        => other is not null
           && string.Equals(Name, other.Name, StringComparison.Ordinal)
           && DownloadUri.Equals(other.DownloadUri);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ReleaseAsset);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, DownloadUri);

    /// <inheritdoc />
    public override string ToString() => Name;
}