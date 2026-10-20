using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Application.Exceptions;

namespace ReleaseWatcher.App.ViewModels;

/// <summary>
/// 设置页中与 GitHub 凭据相关的状态与操作。
/// </summary>
/// <remarks>
/// <para>
/// 只负责 Token 的录入、保存、清除与状态说明；
/// 具体存在哪里由 <see cref="ITokenStore"/> 决定（本轮为 Windows 凭据管理器）。
/// </para>
/// <para>
/// <b>不回显 Token：</b>已保存的 Token 不会读回界面（<c>AGENTS.md</c> §1.4）。
/// 输入框只在用户主动键入时才有内容，状态区只说"是否已配置"。
/// </para>
/// </remarks>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ITokenStore _tokenStore;

    private string _tokenInput = string.Empty;
    private string _tokenStatusText = string.Empty;
    private bool _hasToken;

    /// <summary>构造 ViewModel。</summary>
    /// <param name="tokenStore">Token 存储。</param>
    public SettingsViewModel(ITokenStore tokenStore)
    {
        ArgumentNullException.ThrowIfNull(tokenStore);

        _tokenStore = tokenStore;

        // 命令必须先于 RefreshStatus 创建：刷新会改写 HasToken，
        // 而它的 setter 要通知 ClearTokenCommand 的可执行状态。
        SaveTokenCommand = new RelayCommand(SaveToken, CanSaveToken);
        ClearTokenCommand = new RelayCommand(ClearToken, () => HasToken);

        // 启动即读一次存在性：决定状态文案与"清除"按钮是否可用。
        RefreshStatus();
    }

    /// <summary>用户正在键入的 Token。</summary>
    public string TokenInput
    {
        get => _tokenInput;
        set
        {
            if (SetProperty(ref _tokenInput, value))
            {
                SaveTokenCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Token 状态说明（未配置 / 已配置 / 出错）。</summary>
    public string TokenStatusText
    {
        get => _tokenStatusText;
        private set => SetProperty(ref _tokenStatusText, value);
    }

    /// <summary>是否已保存过 Token。</summary>
    public bool HasToken
    {
        get => _hasToken;
        private set
        {
            if (SetProperty(ref _hasToken, value))
            {
                ClearTokenCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>保存 Token 到凭据存储。</summary>
    public RelayCommand SaveTokenCommand { get; }

    /// <summary>清除已保存的 Token。</summary>
    public RelayCommand ClearTokenCommand { get; }

    /// <summary>Token 是否可以不填。</summary>
    /// <remarks>
    /// 不填也能用：匿名调用可访问公开仓库（限额较低）。
    /// 因此这里只拦空输入，不强制用户配置。
    /// </remarks>
    private bool CanSaveToken() => !string.IsNullOrWhiteSpace(TokenInput);

    private void SaveToken()
    {
        var token = TokenInput.Trim();

        try
        {
            _tokenStore.Save(token);
        }
        catch (Exception ex) when (ex is TokenStorageException or ArgumentException)
        {
            TokenStatusText = $"保存失败：{ex.Message}";
            return;
        }

        // 保存后立刻清空输入框，避免 Token 长时间留在界面与内存里。
        TokenInput = string.Empty;
        RefreshStatus();
        TokenStatusText = "已保存 Token 到 Windows 凭据管理器。访问限额将提升至每小时 5000 次。";
    }

    private void ClearToken()
    {
        try
        {
            _tokenStore.Clear();
        }
        catch (TokenStorageException ex)
        {
            TokenStatusText = $"清除失败：{ex.Message}";
            return;
        }

        TokenInput = string.Empty;
        RefreshStatus();
        TokenStatusText = "已清除 Token，将以匿名方式访问（公开仓库每小时 60 次）。";
    }

    /// <summary>按当前是否有 Token 重写状态说明。</summary>
    private void RefreshStatus()
    {
        bool hasToken;

        try
        {
            hasToken = _tokenStore.HasToken;
        }
        catch (TokenStorageException ex)
        {
            // 读不到不等于没有：如实说明，别让用户以为已配置。
            TokenStatusText = $"无法读取凭据：{ex.Message}";
            HasToken = false;
            return;
        }

        HasToken = hasToken;
        TokenStatusText = hasToken
            ? "已配置 Token，将以认证方式访问 GitHub。"
            : "未配置 Token：将以匿名方式访问公开仓库（每小时 60 次）。可在下方粘贴 Token 后保存。";
    }
}
