using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ReleaseWatcher.Application.Abstractions;

namespace ReleaseWatcher.App.ViewModels;

/// <summary>
/// 设置页中与代理相关的状态与操作。
/// </summary>
/// <remarks>
/// 三种模式对应三类真实场景：跟着公司/家里环境的系统设置走；
/// 系统代理不可用时强制直连；或手工指定一个代理地址。
/// 系统现状只**展示**不代改——那属于系统设置，不该由本应用越权修改。
/// </remarks>
public sealed class ProxySettingsViewModel : ObservableObject
{
    private readonly IProxySettings _settings;

    private ProxyMode _mode;
    private string _manualServer = string.Empty;
    private string _statusText = string.Empty;

    /// <summary>构造 ViewModel。</summary>
    /// <param name="settings">代理配置。</param>
    public ProxySettingsViewModel(IProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _mode = settings.Current.Mode;
        _manualServer = settings.Current.ProxyServer ?? string.Empty;

        ApplyCommand = new RelayCommand(Apply);
        ResetCommand = new RelayCommand(ResetToSystem);

        RefreshSystemStatus();
    }

    /// <summary>当前选择的模式。</summary>
    public ProxyMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsManualMode));
                ApplyCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>下拉框选中项索引；与 <see cref="Mode"/> 互为投影。</summary>
    /// <remarks>
    /// ComboBox 直接绑枚举需要转换器或 Tag 匹配，而下拉项是固定三个，
    /// 用索引做双向绑定最省事，也少一个转换器类型。
    /// </remarks>
    public int ModeIndex
    {
        get => (int)Mode;
        set
        {
            var mode = Enum.IsDefined(typeof(ProxyMode), value) ? (ProxyMode)value : ProxyMode.System;

            if (mode != Mode)
            {
                Mode = mode;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>是否处于自定义模式（决定代理地址输入框是否显示）。</summary>
    public bool IsManualMode => Mode == ProxyMode.Manual;

    /// <summary>手工代理地址。</summary>
    public string ManualServer
    {
        get => _manualServer;
        set
        {
            if (SetProperty(ref _manualServer, value))
            {
                ApplyCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>系统代理现状的一句话说明。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>应用当前选择。</summary>
    public RelayCommand ApplyCommand { get; }

    /// <summary>恢复为跟随系统。</summary>
    public RelayCommand ResetCommand { get; }

    private void Apply()
    {
        var server = IsManualMode ? ManualServer.Trim() : null;

        try
        {
            _settings.Update(new ProxySettings(Mode, server));
        }
        catch (ArgumentException ex)
        {
            StatusText = $"未应用：{ex.Message}";
            return;
        }

        StatusText = $"已应用：{DescribeMode(Mode, server)}{Environment.NewLine}系统代理：{DescribeSystem()}";
    }

    private void ResetToSystem()
    {
        Mode = ProxyMode.System;
        Apply();
    }

    /// <summary>重新读取系统代理现状并刷新状态行。</summary>
    public void RefreshSystemStatus()
        => StatusText = $"系统代理：{DescribeSystem()}{Environment.NewLine}{AppliedSuffix()}";

    private string AppliedSuffix()
        => Mode == ProxyMode.System
            ? "当前：跟随系统"
            : $"当前：{DescribeMode(Mode, IsManualMode ? ManualServer : null)}（点「应用」后生效）";

    private string DescribeMode(ProxyMode mode, string? server)
        => mode switch
        {
            ProxyMode.Direct => "直连，不使用代理",
            ProxyMode.Manual => $"自定义代理 {server}",
            _ => "跟随系统设置",
        };

    private string DescribeSystem()
    {
        try
        {
            return _settings.ReadSystemStatus().Summary;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 读不到系统设置不该阻断用户改自己的代理
            return "读取失败（不影响手动设置）";
        }
    }
}
