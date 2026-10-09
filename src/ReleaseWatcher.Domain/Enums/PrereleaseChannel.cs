namespace ReleaseWatcher.Domain.Enums;

/// <summary>
/// 用户对预发布版本的接收策略。
/// </summary>
/// <remarks>
/// 是否纳入检测<b>是产品决策，不是技术事实</b>，因此建模为领域枚举
/// 而非散落在检测代码里的布尔开关。用户设置最终会落在设置页（见 <c>NOW.md</c>
/// "主题选择未持久化"同类待办）。
/// </remarks>
public enum PrereleaseChannel
{
    /// <summary>只接受正式版：忽略全部预发布 Release。</summary>
    StableOnly = 0,

    /// <summary>
    /// 接受预发布版：正式版与预发布版都参与检测，
    /// 且预发布版之间按语义化版本规则比较（<c>1.0.0-beta.2 &gt; 1.0.0-beta.1</c>）。
    /// </summary>
    IncludePrerelease = 1,
}