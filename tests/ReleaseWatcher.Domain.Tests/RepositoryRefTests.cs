using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Domain.Tests;

/// <summary>
/// <see cref="RepositoryRef"/> 解析与值相等测试。
/// </summary>
public class RepositoryRefTests
{
    [Theory]
    [InlineData("dotnet/runtime", "dotnet", "runtime")]
    [InlineData("MCfywb/ReleaseWatcher", "MCfywb", "ReleaseWatcher")]
    [InlineData("owner/name/", "owner", "name")]
    [InlineData("owner/name.git", "owner", "name")]
    [InlineData("  owner/name  ", "owner", "name")]
    // 用户粘贴完整 URL 是常见形态，应容忍
    [InlineData("https://github.com/dotnet/runtime", "dotnet", "runtime")]
    [InlineData("https://github.com/dotnet/runtime/", "dotnet", "runtime")]
    [InlineData("http://github.com/owner/name", "owner", "name")]
    [InlineData("https://github.com/owner/name.git", "owner", "name")]
    public void TryParse_ValidSlugs_ReturnOwnerAndName(string slug, string owner, string name)
    {
        var parsed = RepositoryRef.TryParse(slug, out var reference);

        Assert.True(parsed);
        Assert.NotNull(reference);
        Assert.Equal(owner, reference!.Owner);
        Assert.Equal(name, reference.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("runtime")]          // 缺 owner
    [InlineData("owner/name/extra")] // 段数过多
    [InlineData("https://github.com/")]
    [InlineData("owner/")]
    [InlineData("/name")]
    [InlineData("/")]
    public void TryParse_InvalidSlugs_ReturnFalse(string? slug)
    {
        var parsed = RepositoryRef.TryParse(slug, out var reference);

        Assert.False(parsed);
        Assert.Null(reference);
    }

    [Fact]
    public void Equals_IsCaseInsensitive()
    {
        // GitHub 的 owner 与仓库名本身大小写不敏感，用作字典键时不能因大小写重复
        RepositoryRef.TryParse("Owner/Name", out var first);
        RepositoryRef.TryParse("owner/name", out var second);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first, second);
        Assert.Equal(first!.GetHashCode(), second!.GetHashCode());
    }

    [Fact]
    public void Equality_AllowsUseAsDictionaryKey()
    {
        RepositoryRef.TryParse("dotnet/runtime", out var upper);
        RepositoryRef.TryParse("DotNet/Runtime", out var lower);

        var map = new Dictionary<RepositoryRef, string> { [upper!] = "found" };

        Assert.True(map.TryGetValue(lower!, out var value));
        Assert.Equal("found", value);
    }

    [Fact]
    public void NotEquals_DifferentOwner_IsNotEqual()
    {
        RepositoryRef.TryParse("dotnet/runtime", out var first);
        RepositoryRef.TryParse("other/runtime", out var second);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ToString_ReturnsOwnerSlashName()
    {
        RepositoryRef.TryParse("https://github.com/dotnet/runtime", out var reference);

        Assert.NotNull(reference);
        Assert.Equal("dotnet/runtime", reference!.ToString());
    }
}