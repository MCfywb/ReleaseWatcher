using System.Windows;
using System.Windows.Media.Animation;

namespace ReleaseWatcher.App.Animation;

/// <summary>
/// 在 <see cref="GridLength"/> 之间做补间动画，用于 Sidebar 展开/收起时平滑过渡列宽。
/// </summary>
/// <remarks>
/// <para>
/// WPF 内置动画没有 GridLength 版本，故自建一个轻量 <see cref="AnimationTimeline"/>。
/// 不引入第三方库 —— 这是一个几十行、且项目随时可能要复用的基础原语
/// （见 AGENTS.md §1.8：能用少量自建代码解决的就不引库，尤其要避免再踩
/// 源生成器 / 工具链一类坑）。
/// </para>
/// </remarks>
public class GridLengthAnimation : AnimationTimeline
{
    /// <summary>动画起点（列宽）。</summary>
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(
            nameof(From),
            typeof(GridLength),
            typeof(GridLengthAnimation),
            new PropertyMetadata(default(GridLength)));

    /// <summary>动画终点（列宽）。</summary>
    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(
            nameof(To),
            typeof(GridLength),
            typeof(GridLengthAnimation),
            new PropertyMetadata(default(GridLength)));

    /// <summary>动画起点（列宽）。</summary>
    public GridLength From
    {
        get => (GridLength)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>动画终点（列宽）。</summary>
    public GridLength To
    {
        get => (GridLength)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    /// <inheritdoc />
    public override Type TargetPropertyType => typeof(GridLength);

    /// <inheritdoc />
    protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

    /// <inheritdoc />
    public override object GetCurrentValue(
        object defaultOriginValue,
        object defaultDestinationValue,
        AnimationClock animationClock)
    {
        var from = From.IsAbsolute ? From : (GridLength)defaultOriginValue;
        var to = To.IsAbsolute ? To : (GridLength)defaultDestinationValue;

        // 若来源或目标不是绝对像素长度（例如 Star），退回目标像素值，避免 NaN。
        double fromValue = from.IsAbsolute ? from.Value : (to.IsAbsolute ? to.Value : 0);
        double toValue = to.IsAbsolute ? to.Value : fromValue;

        double progress = animationClock.CurrentProgress ?? 1.0;

        // easeOutCubic：起步快、收尾缓，贴近 Windows / Fluent 的过渡节奏。
        double eased = progress <= 0 ? 0 : progress >= 1 ? 1 : 1 - Math.Pow(1 - progress, 3);

        return new GridLength(fromValue + (toValue - fromValue) * eased, GridUnitType.Pixel);
    }
}
