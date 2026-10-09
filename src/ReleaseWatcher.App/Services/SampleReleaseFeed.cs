using System.Collections.Concurrent;

using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Domain.Entities;
using ReleaseWatcher.Domain.Exceptions;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.App.Services;

/// <summary>
/// <b>演示用</b>内存 Release 数据源。
/// </summary>
/// <remarks>
/// <para>
/// <b>这是临时实现，不是产品代码。</b>
/// 真实实现应放在 <c>ReleaseWatcher.Infrastructure/GitHub/</c>
/// （调用 GitHub REST API），本类的存在只为让界面能在
/// 真实数据源就绪前跑通绑定与判定逻辑。
/// 替换时只需在 <c>AppServices</c> 改一行注册，界面代码不动。
/// </para>
/// <para>
/// <b>为何放在 App 层而非 Infrastructure：</b>
/// 它不是真实基础设施，只是演示替身。放进 Infrastructure 会让人
/// 误以为已有 GitHub 接入 —— 该目录目前仍应保持为空，
/// 直到真正的 API 客户端落地。
/// </para>
/// <para>
/// <b>样例数据刻意覆盖各种边界情形</b>（有更新 / 已是最新 / 仅预发布 /
/// 仅草稿 / tag 不可解析 / 无任何 Release），让界面能在一次运行中
/// 把每种状态都展示出来，便于验收时核对各状态的表现是否正确。
/// </para>
/// </remarks>
public sealed class SampleReleaseFeed : IReleaseFeed
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<Release>> _releases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 构造样例数据源并预置样例仓库。
    /// </summary>
    public SampleReleaseFeed()
    {
        Seed();
    }

    /// <summary>样例数据中预置的仓库列表，供界面展示与"立即检查"使用。</summary>
    /// <remarks>
    /// 这些是真实存在的公开项目，但版本号与发布时间为示意值，
    /// <b>不代表它们的真实发布记录</b>。
    /// </remarks>
    public static IReadOnlyList<RepositoryRef> SampleRepositories { get; } =
    [
        ParseRef("dotnet", "runtime"),
        ParseRef("dotnet", "aspnetcore"),
        ParseRef("microsoft", "vscode"),
        ParseRef("PowerShell", "PowerShell"),
        ParseRef("github", "gitignore"),
    ];

    /// <summary>
    /// 各样例仓库的"上次已知版本"。
    /// </summary>
    /// <remarks>
    /// 刻意让有的仓库有已知版本、有的为 <c>null</c>（首次检测），
    /// 以覆盖 <see cref="RepositoryRef"/> 两种起始状态。
    /// </remarks>
    public static IReadOnlyDictionary<RepositoryRef, SemanticVersion?> KnownVersions { get; } =
        new Dictionary<RepositoryRef, SemanticVersion?>
        {
            // 有已知版本 → 可判定"是否有更新"
            [ParseRef("dotnet", "runtime")] = ParseVersion("8.0.10"),
            [ParseRef("dotnet", "aspnetcore")] = ParseVersion("8.0.10"),
            [ParseRef("PowerShell", "PowerShell")] = ParseVersion("7.4.6"),

            // null = 首次检测 → 应显示"首次检测到发布"
            [ParseRef("microsoft", "vscode")] = null,

            // 有已知版本，但仓库内无任何 Release → 应显示"暂无 Release"
            [ParseRef("github", "gitignore")] = ParseVersion("1.0.0"),
        };

    /// <inheritdoc />
    /// <remarks>
    /// 本实现<b>不</b>抛 <see cref="ReleaseFeedException"/>——
    /// 内存数据不会"不可达"。该异常留给真实的网络实现。
    /// </remarks>
    public Task<IReadOnlyList<Release>> GetReleasesAsync(
        RepositoryRef repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        cancellationToken.ThrowIfCancellationRequested();

        // 未预置的仓库返回空集合而非抛异常：
        // "该仓库确实没有 Release"是正常状态，不该让界面报错。
        var releases = _releases.TryGetValue(repository.ToString(), out var found)
            ? found
            : Array.Empty<Release>();

        return Task.FromResult(releases);
    }

    /// <summary>按文本解析仓库引用，供静态初始化使用。</summary>
    private static RepositoryRef ParseRef(string owner, string name)
    {
        if (!RepositoryRef.TryParse($"{owner}/{name}", out var reference) || reference is null)
        {
            throw new InvalidOperationException(
                $"样例仓库 {owner}/{name} 无法解析 —— 这是代码错误，不是数据问题。");
        }

        return reference;
    }

    /// <summary>按文本解析版本号，供静态初始化使用。</summary>
    private static SemanticVersion? ParseVersion(string text)
    {
        if (!SemanticVersion.TryParse(text, out var version))
        {
            throw new InvalidOperationException(
                $"样例版本号 {text} 无法解析 —— 这是代码错误，不是数据问题。");
        }

        return version;
    }

    /// <summary>
    /// 构造一个样例 Release。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="Release.FromTag"/> 而非 <see cref="Release.Create"/>：
    /// 由 tag 文本解析版本号正是真实数据源的用法
    /// （GitHub 给的就是 tag），可顺带验证解析环节。
    /// </remarks>
    private static Release Sample(
        RepositoryRef repository,
        string tag,
        string title,
        int daysAgo,
        bool isDraft = false,
        bool? isPrerelease = null,
        int assetCount = 0)
    {
        return Release.FromTag(
            repository,
            tag,
            title,
            isDraft,
            isPrerelease,
            DateTimeOffset.Now.AddDays(-daysAgo),
            BuildAssets(repository, tag, assetCount));
    }

    /// <summary>为样例 Release 构造若干资产地址。</summary>
    /// <remarks>
    /// 用真实形态的稳定地址（GitHub 的 release download 路径），
    /// 让界面上展示的下载项不是凭空捏造的字符串。
    /// </remarks>
    private static IReadOnlyList<ReleaseAsset> BuildAssets(
        RepositoryRef repository,
        string tag,
        int count)
    {
        if (count <= 0)
        {
            return Array.Empty<ReleaseAsset>();
        }

        var result = new List<ReleaseAsset>(count);

        for (var i = 1; i <= count; i++)
        {
            var fileName = $"release-{tag}-asset{i}.zip";

            result.Add(ReleaseAsset.Create(
                fileName,
                new Uri(
                    $"https://github.com/{repository.Owner}/{repository.Name}/releases/download/{tag}/{fileName}"),
                // 示意大小与下载次数：让界面能显示"xx.x MB"
                sizeInBytes: 12_000_000L + (i * 3_500_000L),
                downloadCount: 1200 - (i * 137)));
        }

        return result;
    }

    /// <summary>
    /// 预置样例数据。
    /// </summary>
    /// <remarks>
    /// 每个仓库对应一种界面状态，便于一次运行验收全部表现：
    /// <list type="bullet">
    /// <item><c>dotnet/runtime</c>：有更新，且最新版本是预发布
    /// → 当前策略只收正式版，应显示"有更新"但取到的是正式版。</item>
    /// <item><c>dotnet/aspnetcore</c>：已知版本已是最新 → 应显示"已是最新"。</item>
    /// <item><c>microsoft/vscode</c>：首次检测（无已知版本）
    /// → 应显示"首次检测到发布"。</item>
    /// <item><c>PowerShell/PowerShell</c>：仓库内只有预发布版本
    /// → 正式版策略下应被过滤，显示"只有预发布"。</item>
    /// <item><c>github/gitignore</c>：仓库内无任何 Release
    /// → 应显示"暂无 Release"。</item>
    /// </list>
    /// </remarks>
    private void Seed()
    {
        var runtime = ParseRef("dotnet", "runtime");
        _releases[runtime.ToString()] =
        [
            Sample(runtime, "v9.0.0-preview.3", "Preview 3",daysAgo: 2, isPrerelease: true, assetCount: 2),
            Sample(runtime, "v8.0.11", "8.0.11", daysAgo: 5, assetCount: 3),
            Sample(runtime, "v8.0.10", "8.0.10", daysAgo: 30, assetCount: 3),
            Sample(runtime, "v7.0.20", "7.0.20", daysAgo: 120, assetCount: 3),
        ];

        var aspnetcore = ParseRef("dotnet", "aspnetcore");
        _releases[aspnetcore.ToString()] =
        [
            Sample(aspnetcore, "v8.0.10", "8.0.10", daysAgo: 5, assetCount: 2),
            Sample(aspnetcore, "v8.0.9", "8.0.9", daysAgo: 28),
        ];

        var vscode = ParseRef("microsoft", "vscode");
        _releases[vscode.ToString()] =
        [
            Sample(vscode, "1.95.0", "October 2024", daysAgo: 1, assetCount: 4),
            Sample(vscode, "1.94.2", "September 2024", daysAgo: 40),
        ];

        // 只有预发布：正式版策略下全部候选会被过滤
        var powershell = ParseRef("PowerShell", "PowerShell");
        _releases[powershell.ToString()] =
        [
            Sample(powershell, "v7.5.0-preview.1", "Preview 1", daysAgo: 3, isPrerelease: true),
        ];

        // 刻意不放入 gitignore —— 演示"仓库确实没有 Release"这一状态。
        //
        // 另附一个 tag 不可解析的样例到 runtime 之外的仓库会污染其他状态，
        // 故把"tag 不可解析"的情形放在下面单独演示，不混入上面五个仓库。
        var unparsable = ParseRef("example", "unparsable-tag-demo");
        _releases[unparsable.ToString()] =
        [
            // tag不是版本号 → Release.Version 为 null，无法参与版本比较
            Sample(unparsable, "nightly", "Nightly build", daysAgo: 1),
        ];
    }
}