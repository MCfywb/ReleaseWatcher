using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ReleaseWatcher.App.Services;
using ReleaseWatcher.App.ViewModels;
using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Application.Services;
using ReleaseWatcher.Infrastructure.Configuration;
using ReleaseWatcher.Infrastructure.GitHub;
using ReleaseWatcher.Infrastructure.Security;
using ReleaseWatcher.Infrastructure.Windows;

namespace ReleaseWatcher.App;

/// <summary>
/// 应用组合根（Composition Root）。
/// </summary>
/// <remarks>
/// <para>
/// 整个应用**唯一**知道"哪个接口对应哪个实现"的地方，依据
/// <c>DECISIONS.md</c> D-037 与 D-003。
/// </para>
///
/// <para><b>为什么组合根放在 App 层：</b>
/// App 通过 <c>ProjectReference</c> 引用 Infrastructure 正是为了在此注册实现。
/// C# 项目引用是编译期的 —— 若要写
/// <c>services.AddSingleton&lt;IAppPaths, AppPaths&gt;()</c>，
/// App 必须能看见 Infrastructure 程序集。这是组合根的固有职责，不是分层污染。</para>
///
/// <para><b>架构约束：</b>
/// 本文件是<strong>唯一</strong>允许 <c>using ReleaseWatcher.Infrastructure.*</c>
/// 的文件。业务代码（View / ViewModel / 用例）必须通过 Application 层抽象获取实现，
/// 不得直接 using Infrastructure。
/// 该约束由 <c>CompositionRootTests</c> 自动守护。</para>
///
/// <para><b>注册顺序约定：</b>抽象在前、实现紧随，按层分组
/// （基础设施 → 应用 → UI）。</para>
/// </remarks>
public static class AppServices
{
    /// <summary>
    /// 注册应用全部依赖。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>传入的 <paramref name="services"/>，便于链式调用。</returns>
    public static IServiceCollection AddReleaseWatcher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddInfrastructure(services);
        AddApplication(services);
        AddUi(services);

        // 后续层在实现落地后于各自分组中注册，
        // 例如 AddPersistence() / AddGitHub()。
        return services;
    }

    /// <summary>注册基础设施层实现。</summary>
    private static void AddInfrastructure(IServiceCollection services)
    {
        // IAppPaths 必须是单例 —— 路径在进程生命周期内不应变化，
        // 且 Known Folder 查询有原生调用开销，不宜每次注入都解析。
        services.AddSingleton<IAppPaths, AppPaths>();

        // GitHub Token 存Windows 凭据管理器（DECISIONS.md D-053 / Q-001）。
        // 单例：它本身无状态，读写都直接打到系统凭据 API。
        services.AddSingleton<ITokenStore, WindowsCredentialManagerTokenStore>();

        // 监控仓库与已知版本来自 config.json；C2 接入 SQLite 后换实现即可，
        // 界面与用例只认IAppConfiguration（依赖倒置）。
        services.AddSingleton<IAppConfiguration>(sp =>
            new JsonAppConfiguration(sp.GetRequiredService<IAppPaths>().ConfigFile));

        // HttpClient 单例复用（每请求新建会耗尽端口）。
        //
        // 代理：不设 Proxy，保留HttpClientHandler 的默认行为 ——
        // 在 Windows 上即读取系统代理设置，含 PAC / WPAD（D-001 的代理要求）。
        services.AddSingleton(_ => new HttpClient(new HttpClientHandler
        {
            UseProxy = true,
            AllowAutoRedirect = true,
        })
        {
            // GitHub 请求本身很快；给足 30s 以容纳慢速代理链路，
            // 超时由调用方转成"检测失败"，不会挂住界面。
            Timeout = TimeSpan.FromSeconds(30),
        });

        // 真实数据源：GitHub REST API（D-053 选型：BCL HttpClient，不引 Octokit）。
        // 换成样例数据只需把这里改回 SampleReleaseFeed，其余代码不动。
        services.AddSingleton<IReleaseFeed>(sp => new GitHubReleaseFeed(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<ITokenStore>()));
    }

    /// <summary>注册应用层用例。</summary>
    private static void AddApplication(IServiceCollection services)
    {
        // 用例无状态（每次调用只依赖入参与注入的 feed），故注册为单例。
        services.AddSingleton<ReleaseDetectionService>();
    }

    /// <summary>注册 UI 层类型。</summary>
    private static void AddUi(IServiceCollection services)
    {
        // 窗口用 transient 而非 singleton：未来可能出现设置窗口等第二个实例。
        services.AddTransient<Views.MainWindow>();

        // ViewModel 单例：概览与检测列表共享同一次检测结果，
        // 若各自 transient 会产生两份互不相干的检测状态，
        // 会出现"概览说有3 个更新、列表里只列出 2 个"这类矛盾。
        //
        // 用工厂注册而非直接 AddSingleton<T>：两者构造函数需要
        // "仓库列表"与"已知版本表"两个函数参数。两者现由 config.json 提供
        // （C1）；C2 接入 SQLite 后只需换掉这里的取数方式，
        // ViewModel 本身不必改动。
        services.AddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<IAppConfiguration>();

            return new RepositoryListViewModel(
                sp.GetRequiredService<IReleaseFeed>(),
                sp.GetRequiredService<ReleaseDetectionService>(),
                () => configuration.MonitoredRepositories,
                configuration.GetKnownVersion);
        });

        services.AddSingleton(sp =>
            new OverviewViewModel(sp.GetRequiredService<RepositoryListViewModel>()));

        // 设置页只与凭据有关，与检测结果无关，单独一个单例。
        services.AddSingleton<SettingsViewModel>();
    }

    /// <summary>
    /// 创建并配置应用 Host。
    /// </summary>
    /// <remarks>
    /// 目前仅完成 DI 与控制台日志接线；后续按需加入
    /// 配置源、持久化、后台调度服务等（依据 D-001）。
    /// </remarks>
    public static HostApplicationBuilder CreateHostBuilder()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddReleaseWatcher();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        });

        return builder;
    }

    /// <summary>
    /// 确保数据目录存在。宿主启动时调用，避免后续首次写文件失败。
    /// </summary>
    /// <remarks>
    /// 数据目录在全新安装时并不存在，而 SQLite、日志、缓存都要往里写。
    /// 集中创建比让各组件各自 mkdir 更可控。
    /// </remarks>
    public static void EnsureDataDirectories(IAppPaths paths, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        foreach (var directory in new[]
                 {
                     paths.DataDirectory,
                     paths.LogDirectory,
                     paths.CacheDirectory,
                     paths.RuntimeDirectory,
                 })
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 例如只读介质或权限不足 —— 记录后继续，
                // 让用户至少能打开界面看到原因，而不是直接启动失败。
                logger.LogWarning(
                    ex, "无法创建目录 {Directory}，相关功能可能不可用", directory);
            }
        }
    }
}