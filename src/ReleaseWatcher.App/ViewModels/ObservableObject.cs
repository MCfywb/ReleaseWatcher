using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReleaseWatcher.App.ViewModels;

/// <summary>
/// 可通知变更的 ViewModel 基类。
/// </summary>
/// <remarks>
/// <para>
/// 刻意<b>不引入 CommunityToolkit.Mvvm</b>：整个解决方案目前只依赖
/// <c>Microsoft.Extensions.Hosting</c>，保持零第三方依赖可避免
/// 包版本升级带来的不确定性，且本基类所需功能极少
/// （属性通知 + 值相等时跳过通知）。
/// </para>
/// <para>
/// <b>为何必须校验值相等再通知：</b>绑定的 setter 会被频繁调用
/// （如 <c>Text="{Binding X}"</c> 触发的回写）。若无条件发通知，
/// 会造成无谓的界面重绘，甚至在属性 setter 内联赋值时触发死循环。
/// </para>
/// </remarks>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 赋值并按需触发 <see cref="PropertyChanged"/>。
    /// </summary>
    /// <typeparam name="T">属性类型。</typeparam>
    /// <param name="field">后备字段引用。</param>
    /// <param name="value">新值。</param>
    /// <param name="propertyName">属性名，由编译器提供。</param>
    /// <returns>值是否发生变化。</returns>
    /// <remarks>
    /// 用 <see cref="EqualityComparer{T}"/> 的默认值比较，对字符串、数值、
    /// 引用类型均为各类型惯用的相等语义。
    /// </remarks>
    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>触发属性变更通知。</summary>
    /// <param name="propertyName">属性名；为 <c>null</c> 表示"所有属性都变了"。</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}