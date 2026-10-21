namespace ReleaseWatcher.Domain.Rules;

/// <summary>
/// 从 tag 文本识别"预发布"标识词。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它：</b>真实仓库的预发布标识千奇百怪 —— 有的用 SemVer 的
/// <c>-alpha.1</c>，有的干脆写成 <c>v1.2.3-preview</c>、<c>v1.2.3-rc2</c>、
/// <c>1.2.3 Release Candidate</c>。后者在 SemVer 里解析为<b>正式版</b>，
/// 只看版本号会把它们当成正式版纳入"是否有更新"的判定。
/// </para>
/// <para>
/// <b>匹配规则：</b>关键词不区分大小写，且必须<b>后接非字母或字符串结尾</b>。
/// 这一条约束同时挡住了两类误判：
/// <list type="bullet">
/// <item><c>pressure</c> 含 "pre" 但后面跟字母 → 不算预发布；</item>
/// <item><c>preflight</c>、<c>betamax</c> 同理；而
/// <c>1.2.3-preview1</c>、<c>v1.2.3-rc2</c>、<c>2.0.0-beta</c>、
/// <c>Release Candidate</c> 都能正确命中。</item>
/// </list>
/// </para>
/// <para>
/// <b>与数据源标志的关系：</b>GitHub 自己也有 <c>prerelease</c> 标志。
/// 本判定的结果与它取<b>或</b>——任一为真即为预发布。这样即便维护者
/// 忘了勾选 GitHub 的预发布框，也不会让预发布版本混进正式版判定。
/// </para>
/// </remarks>
public static class PrereleaseTagMatcher
{
    /// <summary>
    /// 预发布标识词表。新增词只需改这里，且必须保持"短词放在长词之后不冲突"的原则
    /// ——匹配规则本身保证 <c>preview</c> 不会被 <c>pre</c> 抢先命中
    /// （<c>pre</c> 后面跟字母 <c>v</c>，不满足后接非字母）。
    /// </summary>
    /// <remarks>
    /// 刻意<b>不</b>收录 <c>nightly</c> / <c>snapshot</c> / <c>dev</c>：
    /// 它们更像"构建通道"而非"预发布版本"，而本项目用 <c>nightly</c> 表示
    /// "tag 不可解析"这一状态（见 <c>ReleaseDetectionRules</c> 的
    /// <c>IgnoredUnparsableTag</c>）。收录它会把"无法解析"改判成"仅预发布"，
    /// 改变既有语义。确有需要时再加。
    /// </remarks>
    private static readonly string[] Keywords =
    [
        "release candidate",
        "preview",
        "pre",
        "rc",
        "alpha",
        "beta",
    ];

    /// <summary>判断 tag 文本是否带有预发布标识。</summary>
    /// <param name="tag">tag 文本，可为空。</param>
    /// <returns>命中任一标识词则返回 <c>true</c>。</returns>
    public static bool ContainsPrereleaseKeyword(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        foreach (var keyword in Keywords)
        {
            var index = tag.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);

            while (index >= 0)
            {
                var after = index + keyword.Length;

                // 后接非字母或字符串结尾 → 才是独立标识词。
                // 这样 "preview" 不会被 "pressure" 误伤，"pre" 也不会抢在 "preview" 前面。
                if (after >= tag.Length || !char.IsLetter(tag[after]))
                {
                    return true;
                }

                index = tag.IndexOf(keyword, index + 1, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }
}
