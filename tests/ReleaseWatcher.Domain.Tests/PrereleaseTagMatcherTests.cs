using ReleaseWatcher.Domain.Entities;
using ReleaseWatcher.Domain.Rules;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Domain.Tests;

/// <summary>
/// 预发布标识词识别与它在 Release 上的生效验证。
/// </summary>
/// <remarks>
/// 这条规则的存在理由：真实仓库把预发布写在 tag 文本里，而不只是 SemVer 的
/// <c>-alpha.1</c>。<c>v1.2.3-preview</c> 在 SemVer 里是正式版，
/// 若不额外识别，它会被当成正式版纳入"是否有更新"的判定。
/// </remarks>
public class PrereleaseTagMatcherTests
{
    private static RepositoryRef Repo()
        => RepositoryRef.TryParse("dotnet/runtime", out var r)
            ? r!
            : throw new InvalidOperationException("测试数据构造失败");

    [Theory]
    // 用户点名的几类
    [InlineData("v1.2.3-preview")]
    [InlineData("v1.2.3-preview1")]
    [InlineData("1.2.3-pre.2")]
    [InlineData("v1.2.3-rc2")]
    [InlineData("1.2.3 Release Candidate")]
    [InlineData("2.0.0-RC1")]
    // 同族常见写法
    [InlineData("v1.0.0-alpha")]
    [InlineData("v1.0.0-beta3")]
    public void Matches_PreReleaseTags(string tag)
    {
        Assert.True(PrereleaseTagMatcher.ContainsPrereleaseKeyword(tag));
    }

    [Theory]
    // 后接字母 → 不是独立标识词，不应误判
    [InlineData("v1.0.0-pressure")]
    [InlineData("v1.0.0-preflight")]
    [InlineData("1.0.0-betamax")]
    [InlineData("1.0.0-alphabet")]
    // 正常正式版
    [InlineData("v8.0.10")]
    [InlineData("2024.10.1")]
    // 构建通道名不等于"预发布版本"：本项目用 nightly 表示"tag 不可解析"，
    // 收录它会把"无法解析"改判成"仅预发布"，改变既有语义
    [InlineData("nightly")]
    [InlineData("20241015")]
    [InlineData("")]
    [InlineData(null)]
    public void DoesNotMatch_StableOrIncidentalTags(string? tag)
    {
        Assert.False(PrereleaseTagMatcher.ContainsPrereleaseKeyword(tag));
    }

    [Fact]
    public void Release_WithKeywordTag_IsPrerelease_EvenWhenVersionLooksStable()
    {
        // 关键场景：tag 不符合 SemVer 的预发布写法（空格分隔），
        // 版本号解析为正式版甚至解析失败，只能靠 tag 文本识别。
        var release = Release.FromTag(Repo(), "1.2.3 Release Candidate", title: "候选版");

        Assert.True(release.IsPrerelease);
    }

    [Fact]
    public void Release_WithStableTag_StaysStable()
    {
        var release = Release.FromTag(Repo(), "v8.0.10");

        Assert.False(release.IsPrerelease);
    }

    [Fact]
    public void Release_WithSemVerPrereleaseTag_StillPrerelease()
    {
        // 原有的 SemVer 判定不能被这次改动破坏
        var release = Release.FromTag(Repo(), "v9.0.0-preview.3");

        Assert.True(release.IsPrerelease);
    }

    [Fact]
    public void KeywordApplies_WhenVersionLooksStable_EvenIfDataSourceSaysFalse()
    {
        // GitHub 的 prerelease 勾选常常没人点，但 tag 文本写了 Release Candidate。
        // 版本本身解析为正式版/解析不出 → 按 tag 文本判为预发布（用户 2026-10-21 要求）。
        var release = Release.FromTag(Repo(), "1.2.3 Release Candidate", isPrerelease: false);

        Assert.True(release.IsPrerelease);
    }

    [Fact]
    public void KeywordDoesNotOverride_DataSourceDecision_WhenVersionAlreadyPrerelease()
    {
        // 版本本身就是 SemVer 预发布时，是否算预发布由数据源决定 ——
        // 这是既有约定（Create_ExplicitPrerelease_OverridesVersionInference），
        // 关键词兜底不得反过来推翻它。
        var release = Release.FromTag(Repo(), "v1.2.3-preview.2", isPrerelease: false);

        Assert.False(release.IsPrerelease);
    }
}
