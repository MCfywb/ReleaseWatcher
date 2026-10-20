using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Application.Abstractions;

/// <summary>
/// 应用配置（只读）：监控哪些仓库、各仓库的已知版本。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它：</b>检测列表要能列出"用户关心的仓库"，判定"是否有更新"
/// 又必须知道"用户当前装的是哪个版本"。两者都是用户状态，不是代码常量。
/// 在 SQLite 落地前（本轮只做 C1），它们由 <c>config.json</c> 承载；
/// 之后换持久化实现，界面与用例都不必改动。
/// </para>
/// <para>
/// <b>只读的原因：</b>当前尚无管理这些值的界面，写入路径没有调用方。
/// 待设置页支持增删仓库时再补 <c>Save</c>，避免现在就留下无人调用的接口。
/// </para>
/// <para>
/// <b>不在此保存 Token：</b>凭据走 <see cref="ITokenStore"/>，
/// 绝不并入明文配置文件（<c>AGENTS.md</c> §1.4）。
/// </para>
/// </remarks>
public interface IAppConfiguration
{
    /// <summary>被监控的仓库列表；为空表示尚未配置任何仓库。</summary>
    IReadOnlyList<RepositoryRef> MonitoredRepositories { get; }

    /// <summary>取某仓库的已知/已装版本。</summary>
    /// <param name="repository">目标仓库。</param>
    /// <returns>已知版本；未配置或无法解析时返回 <c>null</c>（界面显示"首次检测"）。</returns>
    SemanticVersion? GetKnownVersion(RepositoryRef repository);
}
