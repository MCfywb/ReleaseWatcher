using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Domain.Tests;

/// <summary>
/// <see cref="SemanticVersion"/> 的解析与优先级比较测试。
/// </summary>
/// <remarks>
/// 比较规则依据 SemVer 2.0 规范：<c>major.minor.patch</c> 逐段数值比较；
/// 相同时预发布版<b>低于</b>正式版；两者都有预发布标识时按点分段的"数字段优先"规则比较；
/// 构建元数据不参与比较。
/// </remarks>
public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("1", 1, 0, 0)]
    [InlineData("release-1.2.3", 1, 2, 3)]
    [InlineData("Release/1.2.3", 1, 2, 3)]
    [InlineData("releases/2.0.0", 2, 0, 0)]
    [InlineData("rel-3.4.5", 3, 4, 5)]
    [InlineData("1.2.3-beta.1", 1, 2, 3)]
    [InlineData("1.2.3+sha.abc", 1, 2, 3)]
    [InlineData("v2.0.0-rc.1+build.5", 2, 0, 0)]
    [InlineData("1.2.3-beta.1+exp.sha.5114f85", 1, 2, 3)]
    [InlineData("1.0.0-alpha", 1, 0, 0)]
    [InlineData("  1.5.0  ", 1, 5, 0)]
    public void TryParse_ValidTags_ReturnExpectedCoreVersion(
        string tag, int expectedMajor, int expectedMinor, int expectedPatch)
    {
        var parsed = SemanticVersion.TryParse(tag, out var version);

        Assert.True(parsed);
        Assert.NotNull(version);
        Assert.Equal(expectedMajor, version!.Major);
        Assert.Equal(expectedMinor, version.Minor);
        Assert.Equal(expectedPatch, version.Patch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nightly")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("release-")]
    [InlineData("20241015")]
    [InlineData("1.2.3.4")]
    [InlineData("-1.2.3")]
    [InlineData("1.-2.3")]
    [InlineData("1.2.x")]
    [InlineData("v1.2.3-")]
    [InlineData("v1.2.3+")]
    public void TryParse_InvalidTags_ReturnFalse(string? tag)
    {
        var parsed = SemanticVersion.TryParse(tag, out var version);

        Assert.False(parsed);
        Assert.Null(version);
    }

    [Fact]
    public void TryParse_DateLikeTag_IsRejected()
    {
        // 防回归：日期串在形式上是合法的单段数字，若放行会被当作巨大版本号，
        // 导致检测永远判定"有更新"。见 SemanticVersion.TryParseSegment 的说明。
        foreach (var dateTag in new[] { "20241015", "202410151234", "20260101" })
        {
            var parsed = SemanticVersion.TryParse(dateTag, out var version);

            Assert.False(parsed, $"{dateTag} 应被识别为日期串而非版本号");
            Assert.Null(version);
        }
    }

    [Fact]
    public void TryParse_DateTagWithPrefix_IsAlsoRejected()
    {
        Assert.False(SemanticVersion.TryParse("v20241015", out _));
    }

    [Fact]
    public void TryParse_LargeButPlausibleMajor_IsAccepted()
    {
        // 防护只拦"长到像日期"的单段，不应误伤合法的高位版本号
        Assert.True(SemanticVersion.TryParse("9999999.0.0", out _));
    }

    [Fact]
    public void TryParse_PrereleaseTag_SetsPrereleaseAndFlag()
    {
        SemanticVersion.TryParse("v1.0.0-beta.2", out var version);

        Assert.NotNull(version);
        Assert.Equal("beta.2", version!.Prerelease);
        Assert.True(version.IsPrerelease);
    }

    [Fact]
    public void TryParse_StableTag_IsNotPrerelease()
    {
        SemanticVersion.TryParse("v1.0.0", out var version);

        Assert.NotNull(version);
        Assert.Null(version!.Prerelease);
        Assert.False(version.IsPrerelease);
    }

    [Fact]
    public void TryParse_BuildMetadata_IsCapturedButNotNull()
    {
        SemanticVersion.TryParse("1.0.0+build.9", out var version);

        Assert.NotNull(version);
        Assert.Equal("build.9", version!.Build);
    }

    [Theory]
    // 主版本优先
    [InlineData("2.0.0", "1.9.9")]
    // 次版本
    [InlineData("1.3.0", "1.2.9")]
    // 修订号
    [InlineData("1.2.4", "1.2.3")]
    // 预发布低于正式版
    [InlineData("1.0.0", "1.0.0-alpha")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    // 预发布段数多者更高
    [InlineData("1.0.0-alpha.2", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1.1", "1.0.0-alpha.1")]
    // SemVer 2.0：数字标识优先级**低于**非数字标识，故 alpha > 2
    [InlineData("1.0.0-alpha", "1.0.0-2")]
    [InlineData("1.0.0-alpha", "1.0.0-1")]
    // 同为数字段时按数值比（10 > 2），注意这与上一条的"段类型"规则不同
    [InlineData("1.0.0-alpha.10", "1.0.0-alpha.2")]
    public void CompareTo_HigherVersion_ReturnsGreater(string higher, string lower)
    {
        SemanticVersion.TryParse(higher, out var left);
        SemanticVersion.TryParse(lower, out var right);

        Assert.NotNull(left);
        Assert.NotNull(right);
        Assert.True(left! > right!);
        Assert.True(right! < left!);
    }

    [Fact]
    public void CompareTo_BuildMetadata_IsIgnoredInPrecedence()
    {
        // 构建元数据不参与优先级比较：以下三组均应判定为相等
        SemanticVersion.TryParse("1.0.0", out var plain);
        SemanticVersion.TryParse("1.0.0+aaa", out var withAaa);
        SemanticVersion.TryParse("1.0.0+zzz", out var withZzz);

        Assert.NotNull(plain);
        Assert.NotNull(withAaa);
        Assert.NotNull(withZzz);

        Assert.Equal(0, plain!.CompareTo(withAaa!));
        Assert.Equal(0, plain!.CompareTo(withZzz!));
        Assert.Equal(0, withAaa!.CompareTo(withZzz!));

        // 相等意味着既不"大于"也不"小于"
        Assert.False(plain! > withAaa!);
        Assert.False(plain! < withAaa!);
    }

    [Fact]
    public void CompareTo_SameVersion_ReportsEqual()
    {
        SemanticVersion.TryParse("v1.2.3", out var left);
        SemanticVersion.TryParse("1.2.3", out var right);

        Assert.NotNull(left);
        Assert.NotNull(right);
        Assert.Equal(0, left!.CompareTo(right!));
        Assert.False(left > right!);
        Assert.True(left >= right!);
        Assert.True(left <= right!);
    }

    [Fact]
    public void Equals_SameCoreAndPrerelease_IsTrue()
    {
        SemanticVersion.TryParse("v1.2.3", out var left);
        SemanticVersion.TryParse("1.2.3", out var right);

        Assert.NotNull(left);
        Assert.NotNull(right);
        Assert.Equal(left, right);
        Assert.Equal(left!.GetHashCode(), right!.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentBuildMetadata_IsFalse()
    {
        // 值相等含构建元数据 —— 二者是不同的值对象实例。
        // 注意与 CompareTo 区分：优先级比较忽略构建元数据，值相等则不忽略。
        SemanticVersion.TryParse("1.0.0+aaa", out var left);
        SemanticVersion.TryParse("1.0.0+zzz", out var right);

        Assert.NotNull(left);
        Assert.NotNull(right);
        Assert.NotEqual(left, right);
        Assert.Equal(0, left!.CompareTo(right!)); // 但优先级相同
    }

    [Fact]
    public void Equals_SameBuildMetadata_IsTrue()
    {
        SemanticVersion.TryParse("1.0.0+sha.abc", out var left);
        SemanticVersion.TryParse("v1.0.0+sha.abc", out var right);

        Assert.NotNull(left);
        Assert.NotNull(right);
        Assert.Equal(left, right);
    }

    [Fact]
    public void Equals_DifferentPrerelease_IsFalse()
    {
        SemanticVersion.TryParse("1.0.0-beta.1", out var left);
        SemanticVersion.TryParse("1.0.0-beta.2", out var right);

        Assert.NotNull(left);
        Assert.NotNull(right);
        Assert.NotEqual(left, right);
    }

    [Fact]
    public void CompareTo_Null_ReturnsPositive()
    {
        SemanticVersion.TryParse("1.0.0", out var version);

        Assert.NotNull(version);
        Assert.Equal(1, version!.CompareTo(null));
    }

    [Fact]
    public void GreaterThan_NullLeft_ReturnsFalse()
    {
        SemanticVersion? left = null;
        SemanticVersion.TryParse("1.0.0", out var right);

        Assert.False(left > right!);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.1", "1.2.3-beta.1")]
    [InlineData("1.2.3+sha.abc", "1.2.3+sha.abc")]
    [InlineData("v1.0.0-rc.1+build.5", "1.0.0-rc.1+build.5")]
    public void ToString_ReturnsCanonicalText(string tag, string expected)
    {
        SemanticVersion.TryParse(tag, out var version);

        Assert.NotNull(version);
        Assert.Equal(expected, version!.ToString());
    }
}