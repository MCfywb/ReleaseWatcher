using System.Windows;

namespace ReleaseWatcher.App.Views;

/// <summary>
/// 主窗口。
/// </summary>
/// <remarks>
/// <para>
/// 目前为骨架：只有最小尺寸与标题，用于验证组合根与 DI 链路是否通。
/// 实际内容（Sidebar / Toolbar / Split View）在 Phase 1 静态 UI 阶段实现，
/// 见 <c>NOW.md</c> 下一节。
/// </para>
/// <para>
/// **约束：不要在此类中 <c>using ReleaseWatcher.Infrastructure.*</c>。**
/// 需要路径等信息时注入 <c>IAppPaths</c>。
/// 该约束由 <c>CompositionRootTests</c> 自动守护（D-037）。
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>由 DI 容器解析并注入（见 App.xaml.cs 的 OnStartup）。</summary>
    public MainWindow(Application.Abstractions.IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // 标题中显示路径，用于验证 IAppPaths 已正确注入。
        // Phase 1 会替换为真实布局与本地化标题。
        Title = $"ReleaseWatcher — {paths.AppDirectory}";

        InitializeComponent();

        // 依据 docs/reference/development-plan-ui.md 的视觉规范：
        // 最小 1200×760，推荐 1440×900。
        Width = 1440;
        Height = 900;
        MinWidth = 1200;
        MinHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
}