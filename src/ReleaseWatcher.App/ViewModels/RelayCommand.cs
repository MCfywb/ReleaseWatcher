using System.Windows.Input;

namespace ReleaseWatcher.App.ViewModels;

/// <summary>
/// 同步执行的命令。
/// </summary>
/// <remarks>
/// <para>
/// 刻意只实现<b>同步</b>命令，不含 <c>AsyncRelayCommand</c>：
/// 本轮的命令只触发界面状态切换与本地样例数据读取，
/// 没有需要等待的后台工作。真正需要 await 的场景（如网络检测）
/// 出现时，再由ViewModel 内部改用 <c>async void</c> 包装并自行管理
/// <c>IsBusy</c>，而不是提前引入未被使用的抽象。
/// </para>
/// </remarks>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>构造命令。</summary>
    /// <param name="execute">执行逻辑。</param>
    /// <param name="canExecute">
    /// 可执行性判定；为 <c>null</c> 表示始终可执行。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> 为空。</exception>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = execute;
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute();
        }
    }

    /// <summary>
    /// 通知可执行性已变化。
    /// </summary>
    /// <remarks>
    /// 供 ViewModel 在影响 <see cref="CanExecute"/> 的状态变化后调用。
    /// WPF 会重新查询 <see cref="CanExecute"/> 并据此更新按钮的可用态。
    /// </remarks>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}