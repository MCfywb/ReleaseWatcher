using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ReleaseWatcher.App.Converters;

/// <summary>
/// 把"对象是否为 null"映射为可见性，用于三栏视图按选择态淡入淡出。
/// </summary>
/// <remarks>
/// 默认：值为 <c>null</c> → <see cref="Visibility.Collapsed"/>，非 <c>null</c> →
/// <see cref="Visibility.Visible"/>。传入 <c>ConverterParameter="Invert"</c> 时反转，
/// 以便同一个绑定源同时驱动"内容卡片"与"占位提示"的互斥显隐。
/// 配合 <c>AnimatedVisibilityHelper</c> 使用，实现选中仓库/Release 时详情与历史的淡入。
/// </remarks>
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNull = value is null;

        if (parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            isNull = !isNull;
        }

        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <inheritdoc />
    /// <remarks>单向转换；可见性不回写到对象。</remarks>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
