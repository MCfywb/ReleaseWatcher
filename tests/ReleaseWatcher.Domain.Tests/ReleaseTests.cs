using ReleaseWatcher.Domain.Entities;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Domain.Tests;

/// <summary>
/// <see cref="Release"/> 与 <see cref="ReleaseAsset"/> 的构造不变量测试。
/// </summary>
public class ReleaseTests
{
    private static RepositoryRef Repo() => ReleaseWatcher.Domain.ValueObjects.RepositoryRef.TryParse("dotnet/runtime", out var r)
        ? r!
        : throw new InvalidOperationException("测试数据构造失败");

    [Fact]
    public void Create_WithTag_PopulatesProperties()
    {
        var publishedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        var release = Release.Create(
            Repo(),
            tagName: "v1.2.3",
            title: "Version 1.2.3",
            version: null,
            isDraft: false,
            isPrerelease: null,
            publishedAt: publishedAt);

        Assert.Equal("v1.2.3", release.TagName);
        Assert.Equal("Version 1.2.3", release.Title);
        Assert.False(release.IsDraft);
        Assert.False(release.IsPrerelease);
        Assert.Equal(publishedAt, release.PublishedAt);
        Assert.Empty(release.Assets);
    }

    [Fact]
    public void Create_WithoutTitle_FallsBackToTagName()
    {
        var release = Release.Create(Repo(), tagName: "v2.0.0");

        Assert.Equal("v2.0.0", release.Title);
    }

    [Fact]
    public void Create_WithoutTag_UsesVersionTextAsFallback()
    {
        SemanticVersion.TryParse("3.1.4", out var version);

        var release = Release.Create(Repo(), tagName: null, version: version);

        Assert.Equal("3.1.4", release.TagName);
    }

    [Fact]
    public void Create_WithoutTagAndVersion_Throws()
    {
        // 领域不变量：一个 Release 必须能被指认，两者皆空即不可接受
        Assert.Throws<ArgumentException>(() => Release.Create(Repo(), tagName: "   "));
    }

    [Fact]
    public void Create_WithNullRepository_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Release.Create(null!, "v1.0.0"));
    }

    [Fact]
    public void Create_TrimsWhitespaceAroundTag()
    {
        var release = Release.Create(Repo(), tagName: "  v1.0.0  ");

        Assert.Equal("v1.0.0", release.TagName);
    }

    [Fact]
    public void Create_IsPrereleaseNull_InfersFromVersion()
    {
        SemanticVersion.TryParse("v1.0.0-beta.1", out var version);

        var release = Release.Create(Repo(), "v1.0.0-beta.1", version: version);

        Assert.True(release.IsPrerelease);
    }

    [Fact]
    public void Create_ExplicitPrerelease_OverridesVersionInference()
    {
        // GitHub 的 prerelease 标记是权威来源，可能与 tag 能否解析出版本无关
        SemanticVersion.TryParse("v1.0.0-beta.1", out var version);

        var release = Release.Create(Repo(), "v1.0.0-beta.1", version: version, isPrerelease: false);

        Assert.False(release.IsPrerelease);
    }

    [Fact]
    public void FromTag_ParsesVersionFromTag()
    {
        var release = Release.FromTag(Repo(), "v3.4.5");

        Assert.NotNull(release.Version);
        Assert.Equal(3, release.Version!.Major);
        Assert.Equal(4, release.Version.Minor);
        Assert.Equal(5, release.Version.Patch);
    }

    [Fact]
    public void FromTag_UnparsableTag_LeavesVersionNullWithoutThrowing()
    {
        // nightly 等非版本 tag 是正常业务情况，应降级而非抛异常
        var release = Release.FromTag(Repo(), "nightly");

        Assert.Null(release.Version);
        Assert.Equal("nightly", release.TagName);
    }

    [Fact]
    public void Create_WithAssets_KeepsStableUrls()
    {
        // D-012：领域模型只承载稳定 Asset URL，不含临时下载链接
        var asset = ReleaseAsset.Create(
            "ReleaseWatcher-Setup.exe",
            new Uri("https://github.com/dotnet/runtime/releases/download/v1.0.0/app.exe"),
            sizeInBytes: 1024);

        var release = Release.Create(Repo(), "v1.0.0", assets: [asset]);

        Assert.Single(release.Assets);
        Assert.Equal("ReleaseWatcher-Setup.exe", release.Assets[0].Name);
        Assert.Equal(1024, release.Assets[0].SizeInBytes);
    }

    [Fact]
    public void Equality_IsBasedOnRepositoryAndTag()
    {
        var first = Release.FromTag(Repo(), "v1.0.0", title: "标题甲");
        var second = Release.FromTag(Repo(), "v1.0.0", title: "标题乙");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void NotEquals_DifferentTag_IsNotEqual()
    {
        var first = Release.FromTag(Repo(), "v1.0.0");
        var second = Release.FromTag(Repo(), "v1.0.1");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ToString_ReturnsRepositoryAtTag()
    {
        var release = Release.FromTag(Repo(), "v1.0.0");

        Assert.Equal("dotnet/runtime@v1.0.0", release.ToString());
    }

    [Fact]
    public void Create_WithNotes_PopulatesNotes()
    {
        // 发布说明是展示用内容，不参与版本比较，应原样透传。
        var release = Release.Create(Repo(), "v1.0.0", notes: "首发版本，含重大更新");

        Assert.Equal("首发版本，含重大更新", release.Notes);
    }

    [Fact]
    public void Create_WithoutNotes_DefaultsToEmptyString()
    {
        // 空字符串与 null 都归并为空字符串，调用方据此显示"无发布说明"而非空白格。
        var release = Release.Create(Repo(), "v1.0.0");

        Assert.Equal(string.Empty, release.Notes);
    }
}

/// <summary>
/// <see cref="ReleaseAsset"/> 构造校验测试。
/// </summary>
public class ReleaseAssetTests
{
    private static readonly Uri ValidUri = new("https://github.com/owner/repo/releases/download/v1.0.0/app.zip");

    [Fact]
    public void Create_ValidInput_PopulatesProperties()
    {
        var asset = ReleaseAsset.Create("app.zip", ValidUri, 2048, 42);

        Assert.Equal("app.zip", asset.Name);
        Assert.Equal(ValidUri, asset.DownloadUri);
        Assert.Equal(2048, asset.SizeInBytes);
        Assert.Equal(42, asset.DownloadCount);
    }

    [Fact]
    public void Create_UnknownCounts_DefaultToZero()
    {
        var asset = ReleaseAsset.Create("app.zip", ValidUri);

        Assert.Equal(0, asset.SizeInBytes);
        Assert.Equal(0, asset.DownloadCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() => ReleaseAsset.Create(name, ValidUri));
    }

    [Fact]
    public void Create_NullUri_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ReleaseAsset.Create("app.zip", null));
    }

    [Fact]
    public void Create_RelativeUri_Throws()
    {
        // 资产地址是下载链路入口，放进相对路径会让失败推迟到下载时刻
        Assert.Throws<ArgumentException>(
            () => ReleaseAsset.Create("app.zip", new Uri("/releases/app.zip", UriKind.Relative)));
    }

    [Fact]
    public void Equality_IsBasedOnNameAndUri()
    {
        var first = ReleaseAsset.Create("app.zip", ValidUri);
        var second = ReleaseAsset.Create("app.zip", ValidUri, 999, 999);

        Assert.Equal(first, second);
    }
}