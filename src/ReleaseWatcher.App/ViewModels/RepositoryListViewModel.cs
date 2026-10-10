using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Application.Services;
using ReleaseWatcher.Domain.Entities;
using ReleaseWatcher.Domain.Rules;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.App.ViewModels;

/// <summary>
/// 单个仓库的检测结果显示模型。
/// </summary>
/// <remarks>
/// <para>
/// <b>存在的理由：</b>领域层的 <see cref="RepositoryCheckResult"/> 是
/// 只读结构体，其上多为可空引用（<c>LatestVersionText</c> 可能为 <c>null</c>）。
/// 直接绑到 XAML 会迫使每个 TextBlock 都写空值分支与
/// <c>FallbackValue</c>，且"有更新 / 已是最新 / 仅预发布"这类
/// 状态判定散落在多个绑定里。收敛到本类后，界面只绑一个
/// <see cref="StatusText"/>，判定逻辑集中且可单元测试。
/// </para>
/// <para>
/// <b>状态与配色分离：</b><see cref="StatusText"/> 只给文字，
/// 颜色由界面按 <see cref="HasUpdate"/> 与 <see cref="HasError"/>
/// 自行取语义画刷—— ViewModel 不引用任何画刷，保持与主题无关。
/// </para>
/// <para>
/// <b>为何不需要 INotifyPropertyChanged：</b>本对象是<b>不可变</b>的——
/// 所有展示字段仅在构造函数中赋值一次，之后整行被替换
/// （<see cref="RepositoryListViewModel.Rows"/> 每次刷新整体重建），
/// 不存在"构造后就地修改某字段"的需求。因此不继承任何
/// Observable 基类、不套用 <c>[ObservableProperty]</c>，
/// 避免生成一组永远不会被触发的通知，也避免暴露不该存在的
/// 可变 setter。这与"用 CommunityToolkit.Mvvm 替代手写基类"的目标一致：
/// 需要通知时才用工具，不需要时连基类都不引入。
/// </para>
/// </remarks>
public sealed class RepositoryRowViewModel
{
    /// <summary>构造显示模型（正常检测结果，携带完整 Release 历史）。</summary>
    /// <param name="result">检测结果。</param>
    /// <param name="releases">该仓库的全部 Release 历史（来自 feed，未过滤）。</param>
    /// <remarks>
    /// 三栏改造后中栏/右栏需要完整历史，故比单参构造多带一份
    /// <paramref name="releases"/>。单参构造仅用于不需要历史的场景（如概览摘要）。
    /// </remarks>
    public RepositoryRowViewModel(RepositoryCheckResult result, IReadOnlyList<Release> releases)
        : this(result)
    {
        LatestRelease = result.LatestRelease;

        // 历史按发布时间从新到旧排列：中栏"最新在顶"是固定展示约定，
        // 与"最新合格项"（LatestRelease）天然对应。只调顺序、不增删内容。
        Releases = (releases ?? Array.Empty<Release>())
            .OrderByDescending(r => r.PublishedAt)
            .ToArray();
    }

    /// <summary>构造显示模型（正常检测结果，无 Release 历史）。</summary>
    /// <param name="result">检测结果。</param>
    public RepositoryRowViewModel(RepositoryCheckResult result)
    {
        // 刻意不写 ArgumentNullException.ThrowIfNull —— 参数是不可空值类型，
        // 调用该检查会触发 CA2264（对不可空值做空检查是冗余操作）。
        Repository = result.Repository;

        // 判定结论与"有更新"一并取自领域层，不在此处重新推断语义 ——
        // 同一问题若在两处判定，迟早会得出不一致的结论。
        StatusText = result.Outcome.Reason;
        HasUpdate = result.ShouldNotify;
        OutcomeKind = result.Outcome.Kind;

        LatestVersionText = result.LatestVersionText ?? "—";
        KnownVersionText = result.KnownVersion?.ToString() ?? "（首次检测）";

        var release = result.LatestRelease;
        TagText = release?.TagName ?? "—";
        PublishedText = release is null || release.PublishedAt == default
            ? "—"
            : release.PublishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

        AssetCount = release?.Assets.Count ?? 0;

        // 资产摘要：列出文件名让用户确认"该下载哪个"。
        // Release.Assets 为空时给明确的空态文案，而非空字符串 ——
        // 空白格与"没有资产"是两件事。
        AssetsText = release is null || release.Assets.Count == 0
            ? "无下载资产"
            : string.Join("、", release.Assets.Select(a => a.Name));

        TotalAssetSizeText = release is null || release.Assets.Count == 0
            ? string.Empty
            : FormatSize(release.Assets.Sum(a => a.SizeInBytes));

        LatestRelease = release;
        Releases = Array.Empty<Release>();
    }

    private RepositoryRowViewModel(RepositoryRef repository, string errorText)
    {
        Repository = repository;

        // 失败行的其余列给明确的占位符而非空串：
        // 空白单元格会被误读为"该字段为空"，而非"没取到数据"。
        StatusText = errorText;
        HasError = true;
        LatestVersionText = "—";
        KnownVersionText = "—";
        TagText = "—";
        PublishedText = "—";
        AssetsText = "—";
        TotalAssetSizeText = string.Empty;

        // 失败行没有可取的历史或最新 Release —— 让中栏/右栏显示空态而非崩溃。
        LatestRelease = null;
        Releases = Array.Empty<Release>();
    }

    /// <summary>
    /// 构造一个表示检测失败的行。
    /// </summary>
    /// <param name="repository">目标仓库。</param>
    /// <param name="errorText">面向用户的失败原因。</param>
    /// <remarks>
    /// 刻意<b>不复用</b> <see cref="RepositoryCheckResult"/> 表达失败 ——
    /// 领域枚举里没有"数据源不可达"这一类
    /// （<c>DetectionOutcomeKind</c> 描述的都是"判定结论"，
    /// 而非"取数失败"）。硬塞一个 <c>NoCandidate</c> 会让
    /// "仓库确实没有发布"与"网络失败"显示成同一句话，
    /// 那正是 <c>IReleaseFeed</c> 文档明确要求区分的两件事。
    /// </remarks>
    public static RepositoryRowViewModel CreateError(
        RepositoryRef repository,
        string errorText)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return new RepositoryRowViewModel(repository, errorText);
    }

    /// <summary>仓库标识。</summary>
    public RepositoryRef Repository { get; }

    /// <summary>仓库显示名（<c>owner/name</c>）。</summary>
    public string RepositoryText => Repository.ToString();

    /// <summary>检测结论的文字说明（面向用户，非内部术语）。</summary>
    public string StatusText { get; }

    /// <summary>是否有可更新的新版本；界面据此决定状态色。</summary>
    public bool HasUpdate { get; }

    /// <summary>
    /// 该行是否为检测失败；界面据此显示错误色。
    /// </summary>
    /// <remarks>
    /// 单独于 <see cref="HasUpdate"/>：失败不是"没有更新"，
    /// 二者在界面上必须能被一眼区分 ——
    /// 前者是用户的网络问题，后者是仓库的客观状态。
    /// </remarks>
    public bool HasError { get; }

    /// <summary>最新合格版本号文本，无则显示占位符。</summary>
    public string LatestVersionText { get; }

    /// <summary>本次比较所依据的已知版本文本。</summary>
    public string KnownVersionText { get; }

    /// <summary>领域层判定结论；用于把状态压缩成更短的行内文案。</summary>
    public DetectionOutcomeKind OutcomeKind { get; }

    /// <summary>
    /// 是否"没有可展示的 Release"（判定结论为 NoCandidate / IgnoredDraft /
    /// IgnoredPrerelease / IgnoredUnparsableTag）。
    /// </summary>
    /// <remarks>
    /// 这类行不是"检测失败"（红），也不该显示成"已是最新"（绿）——
    /// 绿会把"数据源没东西"误读成"一切正常"，故界面徽章取中性色。
    /// <para>
    /// 注意：首次检测（<c>NoBaseline</c>）不在此列 ——
    /// 领域规则将其"视为已是最新"，有可展示的 Release，按绿色正常态处理。
    /// </para>
    /// </remarks>
    public bool IsNoRelease =>
        OutcomeKind is DetectionOutcomeKind.NoCandidate
            or DetectionOutcomeKind.IgnoredDraft
            or DetectionOutcomeKind.IgnoredPrerelease
            or DetectionOutcomeKind.IgnoredUnparsableTag;

    /// <summary>
    /// 状态短文案：有新版本 / 已是最新 / 暂无 Release / 仅预发布 / 版本无法解析 / 检测失败。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="StatusText"/>（领域层完整原因说明）分工：
    /// 列表行只放两三个字的状态词，长说明留给右侧详情栏，
    /// 避免第一栏文字互相挤压（2026-10-11 用户要求）。
    /// </para>
    /// <para>
    /// 2026-10-11 再调整：去掉"基准版本"与"仅草稿"两种状态 ——
    /// 前者与"已是最新"同义（领域规则本就按已是最新处理），
    /// 后者并入"暂无 Release"（草稿不对外发布，等于没有可展示内容）。
    /// 状态种类越少，列表越容易一眼扫完。
    /// </para>
    /// <para>
    /// 颜色由界面按 <see cref="HasUpdate"/> / <see cref="HasError"/> /
    /// <see cref="IsNoRelease"/> 取语义画刷。
    /// </para>
    /// </remarks>
    public string BadgeText => HasError
        ? "检测失败"
        : HasUpdate
            ? "有新版本"
            : OutcomeKind switch
            {
                DetectionOutcomeKind.NoCandidate or DetectionOutcomeKind.IgnoredDraft => "暂无 Release",
                DetectionOutcomeKind.IgnoredPrerelease => "仅预发布",
                DetectionOutcomeKind.IgnoredUnparsableTag => "版本无法解析",
                _ => "已是最新",
            };

    /// <summary>概览版本摘要：有更新时"旧 → 新"，否则只显示当前最新版本号。</summary>
    /// <remarks>
    /// 有更新必然存在已知版本（否则结论是 NoBaseline 而非 UpdateAvailable），
    /// 故"旧 → 新"两端不会出现"（首次检测）"占位符。
    /// </remarks>
    public string VersionSummaryText => HasError
        ? "—"
        : HasUpdate
            ? $"{KnownVersionText} → {LatestVersionText}"
            : LatestVersionText;

    /// <summary>原始 tag 文本。</summary>
    public string TagText { get; }

    /// <summary>发布时间文本（本地时区）。</summary>
    public string PublishedText { get; }

    /// <summary>资产数量。</summary>
    public int AssetCount { get; }

    /// <summary>资产文件名摘要。</summary>
    public string AssetsText { get; }

    /// <summary>资产总大小文本；无资产时为空字符串。</summary>
    public string TotalAssetSizeText { get; }

    /// <summary>该仓库的全部 Release 历史（含草稿/预发布，未过滤），用于中栏历史列表。</summary>
    /// <remarks>
    /// 与 <see cref="IReleaseFeed"/> 的约定一致：feed 如实取回原始列表，
    /// 不在取数端过滤草稿/预发布。中栏是"历史全貌"，故完整保留，
    /// 由界面按通道分别标注，并借 <see cref="LatestRelease"/> 标记最新合格项。
    /// </remarks>
    public IReadOnlyList<Release> Releases { get; }

    /// <summary>本次检测选出的最新合格 Release；无合格候选时为 <c>null</c>。</summary>
    /// <remarks>中栏历史列表用它给"最新"项做高亮标记；与 <see cref="LatestRelease"/> 一致。</remarks>
    public Release? LatestRelease { get; }

    /// <summary>把字节数格式化为易读文本。</summary>
    /// <remarks>
    /// 用 invariant 的数字格式再补单位，避免不同区域设置下
    /// 小数点与千分位差异导致列宽跳动。
    /// </remarks>
    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Empty;
        }

        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{value:0.#} {units[unitIndex]}");
    }
}

/// <summary>
/// 检测列表页的 ViewModel。
/// </summary>
/// <remarks>
/// <para>
/// <b>职责：</b>驱动检测流程、把结果转成可绑定的行集合、
/// 维护加载态与错误态。<b>不含任何判定逻辑</b> ——
/// "什么算更新"全在 <c>ReleaseDetectionRules</c>，
/// 本类只做编排与展示转换。
/// </para>
/// <para>
/// <b>通知实现：</b>原手写 <c>ObservableObject</c> 基类已替换为
/// CommunityToolkit.Mvvm 的 <see cref="ObservableObject"/>（运行时基类），
/// 复用其 <see cref="ObservableObject.SetProperty{T}"/> 与
/// <see cref="ObservableObject.OnPropertyChanged"/>。刷新命令改用工具包内置的
/// <see cref="AsyncRelayCommand"/>，替代手写的同步 <c>RelayCommand</c>。
/// </para>
/// <para>
/// <b>为何此处不用源生成器 [ObservableProperty]：</b>
/// WPF 的标记编译临时工程（<c>*_wpftmp</c>）会重复编译源生成器产物，
/// 导致 <c>CS0102</c> "字段已定义"。运行时基类 + 手写属性可规避该问题，
/// 同时仍删除手写基类、全面接入工具包。详见 AGENTS.md 的第三方库决策记录。
/// </para>
/// <para>
/// <b>异常处理：</b>单个仓库检测失败<b>不让整页失败</b>。
/// 网络故障下往往多个仓库同时不可达，若首个失败就中断，
/// 用户会看到空白页而不知原因。故逐仓库捕获异常，
/// 失败者以错误行呈现，其余照常显示。
/// </para>
/// </remarks>
public sealed partial class RepositoryListViewModel : ObservableObject
{
    private readonly ReleaseDetectionService _detectionService;
    private readonly IReleaseFeed _feed;
    private readonly Func<IReadOnlyList<RepositoryRef>> _repositoryProvider;
    private readonly Func<RepositoryRef, SemanticVersion?> _knownVersionProvider;

    private bool _isBusy;
    private string _lastRunText = "尚未检测";
    private Visibility _emptyStateVisibility = Visibility.Visible;
    private RepositoryRowViewModel? _selectedRow;
    private Release? _selectedRelease;

    /// <summary>构造检测列表 ViewModel。</summary>
    /// <param name="feed">Release 数据来源（取历史列表用）。</param>
    /// <param name="detectionService">检测用例。</param>
    /// <param name="repositoryProvider">提供待检测的仓库列表。</param>
    /// <param name="knownVersionProvider">提供各仓库的"上次已知版本"，可为 <c>null</c>。</param>
    public RepositoryListViewModel(
        IReleaseFeed feed,
        ReleaseDetectionService detectionService,
        Func<IReadOnlyList<RepositoryRef>> repositoryProvider,
        Func<RepositoryRef, SemanticVersion?> knownVersionProvider)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(detectionService);
        ArgumentNullException.ThrowIfNull(repositoryProvider);
        ArgumentNullException.ThrowIfNull(knownVersionProvider);

        _feed = feed;
        _detectionService = detectionService;
        _repositoryProvider = repositoryProvider;
        _knownVersionProvider = knownVersionProvider;

        // 工具包内置的 AsyncRelayCommand：异步方法包装为 ICommand，
        // 命令名与迁移前一致（RefreshCommand），XAML 绑定无需改动。
        RefreshCommand = new AsyncRelayCommand(Refresh);

        // 三栏选择：左栏选中仓库、中栏选中 Release、以及窄屏下的"返回上一级"。
        SelectRepositoryCommand = new RelayCommand<RepositoryRowViewModel>(SelectRepository);
        SelectReleaseCommand = new RelayCommand<Release>(SelectRelease);
        BackCommand = new RelayCommand(Back);

        // 逐资产下载（过渡实现：交给系统默认浏览器，见 DownloadAsset 注释）。
        DownloadAssetCommand = new RelayCommand<ReleaseAsset>(DownloadAsset);
    }

    /// <summary>检测结果行集合，绑到 <c>ItemsControl.ItemsSource</c>。</summary>
    public ObservableCollection<RepositoryRowViewModel> Rows { get; } = [];

    /// <summary>"立即检查"命令（<see cref="AsyncRelayCommand"/> 包装 <see cref="Refresh"/>）。</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>选中左侧某一仓库（驱动中栏历史与右栏详情）。</summary>
    public RelayCommand<RepositoryRowViewModel> SelectRepositoryCommand { get; }

    /// <summary>选中中栏某一 Release（驱动右栏详情）。</summary>
    public RelayCommand<Release> SelectReleaseCommand { get; }

    /// <summary>窄屏下返回上一级（详情→历史→列表）。</summary>
    public RelayCommand BackCommand { get; }

    /// <summary>"下载该资产"命令（参数为单个 <see cref="ReleaseAsset"/>）。</summary>
    public RelayCommand<ReleaseAsset> DownloadAssetCommand { get; }

    /// <summary>当前选中的仓库（左栏主项）；未选中为 <c>null</c>。</summary>
    /// <remarks>
    /// <para>
    /// setter 公开供左栏 <c>ListBox.SelectedItem</c> 双向绑定直接写入
    /// （用户点击行即选中，无需逐行挂命令）。
    /// </para>
    /// <para>
    /// 选中仓库的同时默认选中其最新合格 Release ——
    /// 保证右栏立即有内容，用户不会先看到两块空白（2026-10-11 用户要求）。
    /// </para>
    /// </remarks>
    public RepositoryRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!SetProperty(ref _selectedRow, value))
            {
                return;
            }

            RaiseSelectionVisualState();

            if (value is not null)
            {
                SelectedRelease = value.LatestRelease ?? value.Releases.FirstOrDefault();
            }
        }
    }

    /// <summary>当前选中的 Release（右栏详情目标）；未选中为 <c>null</c>。</summary>
    /// <remarks>
    /// setter 公开供中栏 <c>ListBox.SelectedItem</c> 双向绑定直接写入：
    /// 用户点选某个 Release 后右栏同步更新。
    /// </remarks>
    public Release? SelectedRelease
    {
        get => _selectedRelease;
        set
        {
            if (!SetProperty(ref _selectedRelease, value))
            {
                return;
            }

            RaiseSelectionVisualState();
        }
    }

    /// <summary>是否正在检测；用于禁用按钮与显示加载提示。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>上次检测时间或结果摘要。</summary>
    public string LastRunText
    {
        get => _lastRunText;
        private set => SetProperty(ref _lastRunText, value);
    }

    /// <summary>空态提示的可见性；<see cref="Rows"/> 非空时为折叠。</summary>
    public Visibility EmptyStateVisibility
    {
        get => _emptyStateVisibility;
        private set
        {
            if (SetProperty(ref _emptyStateVisibility, value))
            {
                // 两个可见性永远互斥，一个变了另一个必须跟着变 ——
                // 各自独立维护会出现"空态与列表同时可见"的叠层。
                ListVisibility = value == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }
    }

    /// <summary>列表内容的可见性；与 <see cref="EmptyStateVisibility"/> 互斥。</summary>
    /// <remarks>
    /// 单独暴露而非让界面写 <c>Converter</c>：一对互斥的可见性
    /// 用两个已算好的属性表达，比引入转换器更直白，
    /// 也不必为它新增一个类型。
    /// </remarks>
    public Visibility ListVisibility { get; private set; } = Visibility.Collapsed;

    // ==================== 三栏空态与派生显示状态 ====================
    // 以下属性全部由 SelectedRow / SelectedRelease 推导，
    // 并在二者变化时经 RaiseSelectionVisualState 统一通知。

    /// <summary>中栏历史列表的可见性：有选中仓库且其确有 Release 时可见。</summary>
    public Visibility HistoryListVisibility
        => SelectedRow is not null && SelectedRow.Releases.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>中栏空态文案；<c>null</c> 表示不显示空态。</summary>
    public string? HistoryEmptyText => SelectedRow is null
        ? "选择一个仓库以查看 Release 历史"
        : SelectedRow.Releases.Count == 0
            ? "该仓库暂无符合条件的 Release"
            : null;

    /// <summary>中栏空态的可见性（与 <see cref="HistoryEmptyText"/> 联动）。</summary>
    public Visibility HistoryEmptyVisibility
        => HistoryEmptyText is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>右栏详情内容的可见性：有选中 Release 时可见。</summary>
    public Visibility DetailContentVisibility
        => SelectedRelease is not null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>右栏空态文案；<c>null</c> 表示不显示空态。</summary>
    public string? DetailEmptyText => SelectedRow is null
        ? "选择一个仓库以查看版本详情"
        : SelectedRelease is null
            ? "该仓库暂无符合条件的 Release"
            : null;

    /// <summary>右栏空态的可见性（与 <see cref="DetailEmptyText"/> 联动）。</summary>
    public Visibility DetailEmptyVisibility
        => DetailEmptyText is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>下载资产列表的可见性：所选 Release 确有资产时可见。</summary>
    public Visibility AssetListVisibility
        => SelectedRelease is not null && SelectedRelease.Assets.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>"没有可下载资产"空态的可见性。</summary>
    public Visibility AssetEmptyVisibility
        => SelectedRelease is not null && SelectedRelease.Assets.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>右栏发布通道徽章文案：草稿 / 预发布 / 正式版。</summary>
    public string DetailChannelText => SelectedRelease is null
        ? string.Empty
        : SelectedRelease.IsDraft
            ? "草稿"
            : SelectedRelease.IsPrerelease
                ? "预发布"
                : "正式版";

    /// <summary>
    /// "有新版本"徽章的可见性：仅当所选 Release 就是该仓库的最新合格项、
    /// 且该仓库确实有更新时显示。
    /// </summary>
    public Visibility DetailUpdatePillVisibility
        => SelectedRow is not null
           && SelectedRow.HasUpdate
           && ReferenceEquals(SelectedRelease, SelectedRow.LatestRelease)
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>右栏发布说明正文；无说明时给明确占位文案而非空白。</summary>
    /// <remarks>
    /// 领域约定 Notes 为空字符串表示"未附说明"（见 <c>Release.Notes</c>），
    /// 空白格会被误读为"内容还没加载"。
    /// </remarks>
    public string DetailNotes
        => string.IsNullOrEmpty(SelectedRelease?.Notes)
            ? "本次发布未附带说明。"
            : SelectedRelease!.Notes;

    /// <summary>
    /// 若当前没有选中仓库，则默认选中"第一个有可用更新"的仓库。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 目标：进入检测列表页时三栏立即有内容，而不是两块大面积空白
    /// （2026-10-11 用户要求）。优先级：第一个有更新 → 第一个有 Release →
    /// 第一行。
    /// </para>
    /// <para>
    /// 已有选择时不覆盖，尊重用户的浏览状态；检测刷新会整体重建行并
    /// 清空选择，因此刷新后自然落到默认项。
    /// </para>
    /// </remarks>
    public void EnsureDefaultSelection()
    {
        if (Rows.Count == 0)
        {
            SelectedRow = null;
            SelectedRelease = null;
            return;
        }

        if (SelectedRow is not null)
        {
            return;
        }

        var row = Rows.FirstOrDefault(r => r.HasUpdate)
            ?? Rows.FirstOrDefault(r => r.Releases.Count > 0)
            ?? Rows[0];

        // setter 会连带默认选中其最新合格 Release。
        SelectedRow = row;
    }

    /// <summary>选中态变化后，统一通知所有派生显示属性。</summary>
    private void RaiseSelectionVisualState()
    {
        OnPropertyChanged(nameof(HistoryListVisibility));
        OnPropertyChanged(nameof(HistoryEmptyText));
        OnPropertyChanged(nameof(HistoryEmptyVisibility));
        OnPropertyChanged(nameof(DetailContentVisibility));
        OnPropertyChanged(nameof(DetailEmptyText));
        OnPropertyChanged(nameof(DetailEmptyVisibility));
        OnPropertyChanged(nameof(AssetListVisibility));
        OnPropertyChanged(nameof(AssetEmptyVisibility));
        OnPropertyChanged(nameof(DetailChannelText));
        OnPropertyChanged(nameof(DetailUpdatePillVisibility));
        OnPropertyChanged(nameof(DetailNotes));
    }

    /// <summary>
    /// 执行一轮检测，刷新 <see cref="Rows"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 入口是 <see cref="RefreshCommand"/>（<c>AsyncRelayCommand</c>）驱动的
    /// 异步方法。真实检测必然是异步的，此处自行负责异常捕获与状态复位，
    /// 不让异常逃逸到 UI 线程的未处理异常处理器 —— 那会让整个应用崩溃，
    /// 而这里失败只应影响列表显示。
    /// </para>
    /// <para>
    /// <b>防并发：</b>以 <see cref="IsBusy"/> 短路后续点击，避免
    /// 并发刷新打乱行顺序，让"检测结果"难以对应到具体仓库。
    /// </para>
    /// </remarks>
    public async Task Refresh()
    {
        if (IsBusy)
        {
            // 防止用户连点导致并发刷新：并发会打乱行的顺序，
            // 让"检测结果"难以对应到具体仓库。
            return;
        }

        IsBusy = true;

        try
        {
            var repositories = _repositoryProvider();
            var newRows = new List<RepositoryRowViewModel>(repositories.Count);

            foreach (var repository in repositories)
            {
                newRows.Add(await CheckOneAsync(repository));
            }

            // 整体替换而非逐行增删：检测结果每次都是"当前快照"，
            // 逐行 diff 只会带来无谓的闪烁。
            // 行实例被整体重建，旧选中引用随之失效，必须一并清空，
            // 否则窄屏视图会停在指向已丢弃对象的"上一级"。
            SelectedRow = null;
            SelectedRelease = null;

            Rows.Clear();
            foreach (var row in newRows)
            {
                Rows.Add(row);
            }

            var updateCount = newRows.Count(r => r.HasUpdate);
            LastRunText = Rows.Count == 0
                ? "没有可检测的仓库"
                : $"已检测 {Rows.Count} 个仓库，{updateCount} 个有新版本"
                  + $"（{DateTime.Now:HH:mm:ss}）";

            EmptyStateVisibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 行已整体重建且选择已清空：默认选中第一个有可用更新的仓库，
            // 让三栏立即有内容而不是两块空白（2026-10-11 用户要求）。
            EnsureDefaultSelection();
        }
        finally
        {
            // 放在 finally：即便检测抛异常也要复位，
            // 否则按钮会永久禁用，界面卡在"检测中"。
            IsBusy = false;
        }
    }

    /// <summary>检测单个仓库，失败时转为错误行而不抛出。</summary>
    /// <remarks>
    /// 逐仓库捕获异常：网络故障往往同时影响多个仓库，
    /// 若首个失败就中断，用户只会看到空白页而不知原因。
    /// </remarks>
    private async Task<RepositoryRowViewModel> CheckOneAsync(RepositoryRef repository)
    {
        try
        {
            var known = _knownVersionProvider(repository);

            // 取回完整历史（含草稿/预发布）供三栏中栏使用，
            // 再用纯判定方法 Evaluate 得到与界面一致的结果 ——
            // 单次 I/O，不重复请求数据源。
            var releases = await _feed.GetReleasesAsync(repository, CancellationToken.None);
            var result = _detectionService.Evaluate(repository, releases, known);

            return new RepositoryRowViewModel(result, releases);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return RepositoryRowViewModel.CreateError(
                repository,
                $"检测失败：{ex.Message}");
        }
    }

    /// <summary>选中某一仓库：右栏默认落到其最新合格 Release。</summary>
    /// <remarks>
    /// 默认选中的连带逻辑已收敛到 <see cref="SelectedRow"/> 的 setter
    /// （左栏 ListBox 双向绑定走同一条路径），此处只负责入口判空。
    /// </remarks>
    private void SelectRepository(RepositoryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        SelectedRow = row;
    }

    /// <summary>在中栏点击某一 Release：更新右栏详情。</summary>
    private void SelectRelease(Release? release)
    {
        if (release is null)
        {
            return;
        }

        SelectedRelease = release;
    }

    /// <summary>窄屏返回上一级：详情→历史→列表，逐级回退。</summary>
    private void Back()
    {
        if (SelectedRelease is not null)
        {
            // 从右栏详情回到中栏历史（同一仓库）。
            SelectedRelease = null;
        }
        else if (SelectedRow is not null)
        {
            // 从中栏历史回到左栏列表。
            SelectedRow = null;
        }
    }

    /// <summary>
    /// 下载单个资产（过渡实现：把稳定 Asset URL 交给系统默认浏览器）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// aria2 下载引擎尚未接入（Infrastructure/Aria2 计划中）。在此之前，
    /// "下载"的最小可用实现是把稳定 Asset URL 交给系统默认处理程序 ——
    /// 用户能真实拿到文件，且按钮与资产一一对应，
    /// 不存在"没有可下载文件却摆一个大禁用按钮"的问题
    /// （2026-10-11 用户要求）。
    /// </para>
    /// <para>
    /// 打开失败（无默认浏览器等环境问题）静默忽略：
    /// 这是辅助操作，不值得为它打断界面。
    /// </para>
    /// </remarks>
    private void DownloadAsset(ReleaseAsset? asset)
    {
        if (asset is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(asset.DownloadUri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            // 无默认浏览器等环境问题：忽略，不打断界面。
        }
    }
}
