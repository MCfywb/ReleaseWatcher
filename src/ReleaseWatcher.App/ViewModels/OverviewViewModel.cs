using System.Globalization;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Application.Services;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.App.ViewModels;

/// <summary>
/// 概览页 ViewModel —— 汇总统计。
/// </summary>
/// <remarks>
/// <para>
/// <b>数据来源与检测列表页同源</b>：两者都调用
/// <see cref="ReleaseDetectionService"/>，因此概览里的计数
/// 与列表里的行必然一致。若概览另走一套统计，
/// 会出现"概览说3 个有更新、列表里只看到 2 个"这类矛盾。
/// </para>
/// <para>
/// <b>共享同一次检测：</b>概览注入的是
/// <see cref="RepositoryListViewModel"/> 本身，
/// 直接复用其已完成的检测结果，不重复取数——
/// 概览单独再取一次会让两个页面在数据不一致时无法解释。
/// </para>
/// <para>
/// <b>通知实现：</b>原手写 <c>ObservableObject</c> 基类已替换为
/// CommunityToolkit.Mvvm 的 <see cref="ObservableObject"/>（运行时基类），
/// 复用其 <see cref="ObservableObject.SetProperty{T}"/> 与
/// <see cref="ObservableObject.OnPropertyChanged"/>；刷新命令改用内置的
/// <see cref="AsyncRelayCommand"/>。WPF 标记编译临时工程对源生成器产物存在
/// 重复编译问题，故此处采用运行时基类 + 手写属性的稳妥方案，
/// 同时仍删除手写基类、全面接入工具包（详见 AGENTS.md）。
/// </para>
/// </remarks>
public sealed partial class OverviewViewModel : ObservableObject
{
    private readonly RepositoryListViewModel _repositoryList;

    private bool _isBusy;
    private int _totalCount;
    private int _updateCount;
    private int _upToDateCount;
    private int _errorCount;
    private DateTime? _lastCheckedAt;
    private Visibility _emptyStateVisibility = Visibility.Visible;
    private Visibility _contentVisibility = Visibility.Collapsed;

    /// <summary>构造概览 ViewModel。</summary>
    /// <param name="repositoryList">检测列表 ViewModel（复用其检测结果）。</param>
    public OverviewViewModel(RepositoryListViewModel repositoryList)
    {
        ArgumentNullException.ThrowIfNull(repositoryList);

        _repositoryList = repositoryList;

        // 检测列表变化时概览要跟着变 —— 否则会出现
        // "列表已刷新、概览数字还是旧的"。
        _repositoryList.PropertyChanged += OnRepositoryListChanged;

        // Rows 是 ObservableCollection，集合变动需单独订阅。
        _repositoryList.Rows.CollectionChanged += OnRowsChanged;

        // 工具包内置的 AsyncRelayCommand 包装 Refresh()。
        RefreshCommand = new AsyncRelayCommand(Refresh);
    }

    /// <summary>解除对检测列表的订阅。</summary>
    /// <remarks>
    /// 显式解绑而非依赖 GC：若不解除，检测列表会因本类的订阅而一直可达，
    /// 在单例注册下将无法回收。这是事件订阅最常见的泄漏形态。
    /// </remarks>
    public void Detach()
    {
        _repositoryList.PropertyChanged -= OnRepositoryListChanged;
        _repositoryList.Rows.CollectionChanged -= OnRowsChanged;
    }

    /// <summary>"刷新"命令（<see cref="AsyncRelayCommand"/> 包装 <see cref="Refresh"/>）。</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>底层检测列表，供检测列表页绑定。</summary>
    /// <remarks>
    /// <b>为何把 DataContext 设在概览上、列表页再下钻取本属性：</b>
    /// 概览与列表必须读到<b>同一份</b>检测结果。若两个页面各自
    /// 设 DataContext，就会得到两个独立实例，计数与列表内容
    /// 可能对不上。故统一挂在概览上，列表页用
    /// <c>DataContext.RepositoryList</c> 下钻取同一对象。
    /// </remarks>
    public RepositoryListViewModel RepositoryList => _repositoryList;

    /// <summary>是否正在检测。</summary>
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

    /// <summary>监控仓库总数。</summary>
    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(HeaderSubtitleText));
            }
        }
    }

    /// <summary>有新版本的仓库数。</summary>
    public int UpdateCount
    {
        get => _updateCount;
        private set => SetProperty(ref _updateCount, value);
    }

    /// <summary>已是最新（或首次记录）的仓库数。</summary>
    public int UpToDateCount
    {
        get => _upToDateCount;
        private set => SetProperty(ref _upToDateCount, value);
    }

    /// <summary>检测失败的仓库数。</summary>
    public int ErrorCount
    {
        get => _errorCount;
        private set => SetProperty(ref _errorCount, value);
    }

    /// <summary>上次检测时间；未检测过为 <c>null</c>。</summary>
    public DateTime? LastCheckedAt
    {
        get => _lastCheckedAt;
        private set
        {
            if (SetProperty(ref _lastCheckedAt, value))
            {
                OnPropertyChanged(nameof(LastCheckedText));
                OnPropertyChanged(nameof(HeaderSubtitleText));
            }
        }
    }

    /// <summary>上次检测时间的文本形式。</summary>
    public string LastCheckedText => LastCheckedAt is null
        ? "尚未检测"
        : LastCheckedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    /// <summary>页头副标题：「上次检查：… · 监控 N 个仓库」。</summary>
    /// <remarks>
    /// 概览版式（2026-10-11 截图）把检测时间与监控数合成一行小字；
    /// 汇总为一个属性，避免在 XAML 里拼 MultiBinding。
    /// </remarks>
    public string HeaderSubtitleText
        => $"上次检查：{LastCheckedText} · 监控 {TotalCount} 个仓库";

    /// <summary>空态提示可见性。</summary>
    public Visibility EmptyStateVisibility
    {
        get => _emptyStateVisibility;
        private set => SetProperty(ref _emptyStateVisibility, value);
    }

    /// <summary>统计内容区可见性；与 <see cref="EmptyStateVisibility"/> 互斥。</summary>
    public Visibility ContentVisibility
    {
        get => _contentVisibility;
        private set => SetProperty(ref _contentVisibility, value);
    }

    /// <summary>概览页展示的最近若干条检测结果。</summary>
    /// <remarks>
    /// 只取前 <see cref="RecentRowLimit"/> 条：概览是"摘要"，
    /// 完整列表在检测列表页。这里列全了会让两页内容重复，
    /// 反而弱化了概览"一眼看重点"的作用。
    /// </remarks>
    public IEnumerable<RepositoryRowViewModel> RecentRows
        => _repositoryList.Rows.Take(RecentRowLimit);

    /// <summary>概览最多展示多少条。</summary>
    private const int RecentRowLimit = 5;

    /// <summary>执行一轮检测并刷新统计。</summary>
    public async Task Refresh()
    {
        IsBusy = true;

        try
        {
            await _repositoryList.Refresh();

            // 统计口径：总数 = 全部行；
            // 更新数由领域层判定结果给出，不在此处按版本号大小自行比较。
            TotalCount = _repositoryList.Rows.Count;
            UpdateCount = _repositoryList.Rows.Count(r => r.HasUpdate);
            ErrorCount = _repositoryList.Rows.Count(r => r.HasError);
            UpToDateCount = TotalCount - UpdateCount - ErrorCount;
            LastCheckedAt = DateTime.Now;

            var hasContent = TotalCount > 0;
            EmptyStateVisibility = hasContent ? Visibility.Collapsed : Visibility.Visible;
            ContentVisibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>检测列表变化时重算统计。</summary>
    /// <remarks>
    /// <see cref="RepositoryListViewModel.Rows"/> 是
    /// <c>ObservableCollection</c>，集合变动走 CollectionChanged 而非
    /// PropertyChanged，故两者都要监听：前者刷新"最近结果"列表，
    /// 后者同步忙碌态。
    /// </remarks>
    private void OnRepositoryListChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepositoryListViewModel.IsBusy))
        {
            IsBusy = _repositoryList.IsBusy;
        }
    }

    /// <summary>检测列表的行集合变动时，刷新"最近结果"。</summary>
    private void OnRowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(RecentRows));
}
