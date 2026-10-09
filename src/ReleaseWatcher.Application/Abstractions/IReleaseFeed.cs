using ReleaseWatcher.Domain.Entities;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Application.Abstractions;

/// <summary>
/// Release 数据来源端口。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么端口定义在应用层而非基础设施层：</b>
/// 依赖倒置 —— 检测用例只知道自己需要"某仓库的 Release 列表"，
/// 不关心它来自 GitHub REST API、本地缓存还是测试替身。
/// 真实实现放<code>ReleaseWatcher.Infrastructure</code>，
/// 由 <c>AppServices</c> 注册（依据 <c>DECISIONS.md</c> D-003 / D-037）。
/// </para>
/// <para>
/// <b>D-002 的约束：</b>两种检测模式（固定版本检测 / 最新版本检测）
/// 都以单个仓库的 releases 列表为单位，因此端口的粒度是"仓库"，
/// 不是"版本号"。
/// </para>
/// <para>
/// <b>实现方必须遵守的约定：</b>
/// <list type="number">
/// <item>返回的 <see cref="Release.Version"/> 允许为 <c>null</c>
/// —— tag 无法解析为版本号是正常业务情况（见 <see cref="SemanticVersion"/>）。</item>
/// <item><b>不要在实现里过滤草稿或按通道筛选</b>：那些是
/// <c>ReleaseDetectionRules</c> 的职责，实现方只负责如实取回原始列表。
/// 在此过滤会让规则测试失去意义。</item>
/// <item>网络失败应抛异常而非返回空集合 —— "取不到"与"确实没有"是不同语义，
/// 由用例分别处理。</item>
/// </list>
/// </para>
/// </remarks>
public interface IReleaseFeed
{
    /// <summary>
    /// 取回指定仓库的 Release 列表。
    /// </summary>
    /// <param name="repository">目标仓库。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该仓库的 Release 集合，按发布时间倒序为佳；空集合表示该仓库暂无 Release。</returns>
    /// <exception cref="Domain.Exceptions.ReleaseFeedException">
    /// 数据来源不可达或返回了无法解析的内容。
    /// </exception>
    Task<IReadOnlyList<Release>> GetReleasesAsync(
        RepositoryRef repository,
        CancellationToken cancellationToken = default);
}