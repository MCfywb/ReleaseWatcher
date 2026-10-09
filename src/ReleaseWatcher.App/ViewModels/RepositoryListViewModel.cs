using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Application.Services;
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
/// </remarks>
public sealed class RepositoryRowViewModel : ObservableObject
{
    private string _statusText = string.Empty;
    private bool _hasUpdate;
    private bool _hasError;

    /// <summary>构造显示模型（正常检测结果）。</summary>
    /// <param name="result">检测结果。</param>
    public RepositoryRowViewModel(RepositoryCheckResult result)
    {
        // 刻意不写 ArgumentNullException.ThrowIfNull —— 参数是不可空值类型，
        // 调用该检查会触发 CA2264（对不可空值做空检查是��操作）。
        Repository = result.Repository;

        // 判定结论与"有更新"一并取自领域层，不在此处重新推断语义 ——
        // 同一问题若在两处判定，迟早会得出不一致的结论。
        StatusText = result.Outcome.Reason;
        HasUpdate = result.ShouldNotify;

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
    }

    private RepositoryRowViewModel(RepositoryRef repository, string errorText)
    {
        Repository = repository;

        // 直接赋后备字段而非走属性：私有构造里属性是 init-only，
        // 且赋字段能让编译器确知"退出构造函数时已赋值"（避免 CS8618）。
        _statusText = errorText;
        _hasError = true;

        // 失败行的其余列给明确的占位符而非空串：
        // 空白单元格会被误读为"该字段为空"，而非"没取到数据"。
        LatestVersionText = "—";
        KnownVersionText = "—";
        TagText = "—";
        PublishedText = "—";
        AssetsText = "—";
        TotalAssetSizeText = string.Empty;
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
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>是否有可更新的新版本；界面据此决定状态色。</summary>
    public bool HasUpdate
    {
        get => _hasUpdate;
        private set => SetProperty(ref _hasUpdate, value);
    }

    /// <summary>
    /// 该行是否为检测失败；界面据此显示错误色。
    /// </summary>
    /// <remarks>
    /// 单独于 <see cref="HasUpdate"/>：失败不是"没有更新"，
    /// 二者在界面上必须能被一眼区分 ——
    /// 前者是用户的网络问题，后者是仓库的客观状态。
    /// </remarks>
    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    /// <summary>最新合格版本号文本，无则显示占位符。</summary>
    public string LatestVersionText { get; }

    /// <summary>本次比较所依据的已知版本文本。</summary>
    public string KnownVersionText { get; }

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

    /// <summary>把字节数格式化为易读文本。</summary>
    /// <remarks>
    /// 用invariant 的数字格式再补单位，避免不同区域设置下
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
/// <b>异常处理：</b>单个仓库检测失败<b>不让整页失败</b>。
/// 网络故障下往往多个仓库同时不可达，若首个失败就中断，
/// 用户会看到空白页而不知原因。故逐仓库捕获异常，
/// 失败者以错误行呈现，其余照常显示。
/// </para>
/// </remarks>
public sealed class RepositoryListViewModel : ObservableObject
{
    private readonly ReleaseDetectionService _detectionService;
    private readonly Func<IReadOnlyList<RepositoryRef>> _repositoryProvider;
    private readonly Func<RepositoryRef, SemanticVersion?> _knownVersionProvider;

    private bool _isBusy;
    private string _lastRunText = "尚未检测";
    private Visibility _emptyStateVisibility = Visibility.Visible;

    /// <summary>构造检测列表 ViewModel。</summary>
    /// <param name="detectionService">检测用例。</param>
    /// <param name="repositoryProvider">提供待检测的仓库列表。</param>
    /// <param name="knownVersionProvider">提供各仓库的"上次已知版本"，可为 <c>null</c>。</param>
    public RepositoryListViewModel(
        ReleaseDetectionService detectionService,
        Func<IReadOnlyList<RepositoryRef>> repositoryProvider,
        Func<RepositoryRef, SemanticVersion?> knownVersionProvider)
    {
        ArgumentNullException.ThrowIfNull(detectionService);
        ArgumentNullException.ThrowIfNull(repositoryProvider);
        ArgumentNullException.ThrowIfNull(knownVersionProvider);

        _detectionService = detectionService;
        _repositoryProvider = repositoryProvider;
        _knownVersionProvider = knownVersionProvider;

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
    }

    /// <summary>检测结果行集合，绑到 <c>ItemsControl.ItemsSource</c>。</summary>
    public ObservableCollection<RepositoryRowViewModel> Rows { get; } = [];

    /// <summary>"立即检查"命令。</summary>
    public RelayCommand RefreshCommand { get; }

    /// <summary>是否正在检测；用于禁用按钮与显示加载提示。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
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

    /// <summary>
    /// 执行一轮检测，刷新 <see cref="Rows"/>。
    /// </summary>
    /// <remarks>
    /// 以 <c>async void</c> 触发：入口是 <c>RelayCommand</c>（同步契约），
    /// 而真实检测必然是异步的。此处自行负责异常捕获与状态复位，
    /// 不让异常逃逸到 UI 线程的未处理异常处理器 ——
    /// 那会让整个应用崩溃，而这里失败只应影响列表显示。
    /// </remarks>
    public async Task RefreshAsync()
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
            var result = await _detectionService.CheckAsync(repository, known);

            return new RepositoryRowViewModel(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return RepositoryRowViewModel.CreateError(
                repository,
                $"检测失败：{ex.Message}");
        }
    }
}