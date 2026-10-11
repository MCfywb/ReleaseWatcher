using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace ReleaseWatcher.App.Animation;

/// <summary>
/// 让元素在 <see cref="Visibility"/> 切换时淡入淡出，而不是生硬地显隐。
/// </summary>
/// <remarks>
/// <para>
/// 用于"空态 ↔ 内容"、"加载指示显隐"等需要柔和过渡的位置，
/// 也可由后续任何操作复用 —— Request 3 明确要"既改既有、也覆盖未来操作"，
/// 故做成可附加的通用原语，而不是写死在某处。
/// </para>
/// <para>
/// 用法：<c>anim:AnimatedVisibilityHelper.AnimatedVisibility="{Binding SomeVisibility}"</c>。
/// 绑定源通常是 ViewModel 的 <c>Visibility</c> 属性（如 <c>EmptyStateVisibility</c>）；
/// 若来源是 <c>bool</c>（如 <c>IsBusy</c>），配合
/// <c>BooleanToVisibilityConverter</c> 即可。
/// </para>
/// </remarks>
public static class AnimatedVisibilityHelper
{
    /// <summary>淡入淡出时长（秒），可针对元素覆盖。</summary>
    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.RegisterAttached(
            "Duration",
            typeof(double),
            typeof(AnimatedVisibilityHelper),
            new PropertyMetadata(0.16));

    /// <summary>要呈现的目标可见性；值变化时自动播放过渡。</summary>
    /// <remarks>
    /// <b>默认值必须是 <see cref="Visibility.Visible"/>。</b>
    /// WPF 在"绑定推入的值与依赖属性当前有效值相同"时**不会触发属性变更回调**。
    /// 若默认值取 <c>Collapsed</c>，当绑定初值恰为 <c>Collapsed</c>（例如尚未选中任何
    /// Release 时）回调就不会触发，元素会停留在 XAML 默认的 <c>Visible</c> 上：
    /// 空态文本块以空文本继续占位，把下方内容整体顶偏。
    /// 2026-10-12 现场取证即此故障：右栏详情内容被下移 52px（20 高度 + 16×2 边距），
    /// 直到某次真正发生 <c>Visible→Collapsed</c> 变化才淡出收起、内容瞬间贴回标题下方。
    /// 取 <c>Visible</c> 后，"初值为 <c>Collapsed</c>"必然产生一次值变化并触发回调，
    /// 首次落地由回调直接落位，方向反了也同样自洽。
    /// </remarks>
    public static readonly DependencyProperty AnimatedVisibilityProperty =
        DependencyProperty.RegisterAttached(
            "AnimatedVisibility",
            typeof(Visibility),
            typeof(AnimatedVisibilityHelper),
            new PropertyMetadata(Visibility.Visible, OnAnimatedVisibilityChanged));

    /// <summary>读取淡入淡出时长（秒）。</summary>
    public static double GetDuration(DependencyObject obj)
        => (double)obj.GetValue(DurationProperty);

    /// <summary>设置淡入淡出时长（秒）。</summary>
    public static void SetDuration(DependencyObject obj, double value)
        => obj.SetValue(DurationProperty, value);

    /// <summary>读取目标可见性。</summary>
    public static Visibility GetAnimatedVisibility(DependencyObject obj)
        => (Visibility)obj.GetValue(AnimatedVisibilityProperty);

    /// <summary>设置目标可见性。</summary>
    public static void SetAnimatedVisibility(DependencyObject obj, Visibility value)
        => obj.SetValue(AnimatedVisibilityProperty, value);

    private static void OnAnimatedVisibilityChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        var target = (Visibility)e.NewValue;
        double seconds = GetDuration(d);
        var duration = TimeSpan.FromSeconds(seconds);

        // 首次落地时直接落位、不播放动画，避免启动时"先闪一下再淡入/淡出"。
        var first = !_firstApplied.TryGetValue(d, out _);
        if (first)
        {
            _firstApplied.Add(d, new object());
        }

        CancelPendingCollapse(element);

        if (target == Visibility.Visible)
        {
            element.Visibility = Visibility.Visible;
            if (first)
            {
                element.Opacity = 1;
                return;
            }

            // 先置可见，再从 0 淡入，避免"先闪一下再淡入"。
            element.BeginAnimation(
                UIElement.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, duration));
            return;
        }

        if (first)
        {
            element.Visibility = Visibility.Collapsed;
            return;
        }

        // 先淡出，动画结束后再收起，避免硬切。
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(
            element.Opacity, 0, duration);
        element.BeginAnimation(UIElement.OpacityProperty, fadeOut);

        // 兜底收起：淡出被后续动画打断时 Completed 不会触发，元素会以 opacity 0
        // 继续占据布局空间，把下方内容顶偏。用计时器保证"目标为 Collapsed 时
        // 最终一定收起"，不再依赖动画是否跑完。
        var timer = new DispatcherTimer
        {
            Interval = duration + TimeSpan.FromMilliseconds(40),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CancelPendingCollapse(element);

            // 期间若又被切回可见，则不收起。
            if (GetAnimatedVisibility(d) == Visibility.Collapsed)
            {
                element.Visibility = Visibility.Collapsed;
            }
        };
        _pendingCollapse.AddOrUpdate(d, timer);
        timer.Start();
    }

    /// <summary>记录每个元素是否已做过首次落地，避免启动期闪动。</summary>
    private static readonly ConditionalWeakTable<DependencyObject, object> _firstApplied = new();

    /// <summary>记录待执行的收起计时器，便于目标转回可见时取消。</summary>
    private static readonly ConditionalWeakTable<DependencyObject, DispatcherTimer> _pendingCollapse = new();

    private static void CancelPendingCollapse(UIElement element)
    {
        if (_pendingCollapse.TryGetValue(element, out var timer))
        {
            timer.Stop();
            _pendingCollapse.Remove(element);
        }
    }
}
