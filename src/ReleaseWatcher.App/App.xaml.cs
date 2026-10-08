using System.IO;
using System.Windows;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ReleaseWatcher.Application.Abstractions;

namespace ReleaseWatcher.App;

/// <summary>
/// 应用入口与生命周期管理。
/// </summary>
/// <remarks>
/// <para>
/// WPF 的 <see cref="System.Windows.Application"/> 类在 XAML 中实例化
/// （见 App.xaml），因此**不能**在此直接使用构造函数注入。
/// 本类采用传统方式：<see cref="OnStartup"/> 中构建 <see cref="IHost"/>，
/// 再从容器解析主窗口。
/// </para>
/// <para>
/// 组合根位于 <see cref="AppServices"/>（依据 <c>DECISIONS.md</c> D-037）。
/// </para>
/// <para>
/// <b>命名冲突说明：</b>本项目有一个名为 <c>ReleaseWatcher.Application</c> 的
/// <em>命名空间</em>（对应 ReleaseWatcher.Application 项目），它会遮蔽
/// <c>System.Windows.Application</c>。因此本文件中的基类必须写全名
/// <see cref="System.Windows.Application"/>，不可简写为 <c>Application</c>。
/// 今后新增文件若同时用到 WPF 的 <c>Application</c> 与该命名空间，
/// 需特别注意这一冲突。
/// </para>
/// </remarks>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    /// <summary>
    /// 应用容器。供后续 ViewModel 解析依赖。
    /// </summary>
    public static IServiceProvider Services { get; private set; } =
        null!; // OnStartup 中赋值；在此之前不应被访问。

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = AppServices.CreateHostBuilder().Build();

        Services = _host.Services;

        // 数据目录在全新安装时不存在，SQLite / 日志 / 缓存都要往里写。
        var paths = Services.GetRequiredService<IAppPaths>();
        AppServices.EnsureDataDirectories(paths, _host.Services.GetRequiredService<ILogger<App>>());

        await _host.StartAsync();

        // 由容器创建主窗口，以便其构造函数可注入依赖。
        // StartupUri 已清空（见 App.xaml），此处手动 Show。
        var mainWindow = Services.GetRequiredService<Views.MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // 顺序很重要：先停宿主（让后台服务收尾），再走WPF 默认退出流程。
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}