using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ReleaseWatcher.App.Converters;

/// <summary>
/// 比较两个值是否相等，相等则 <see cref="Visibility.Visible"/>，否则 <see cref="Visibility.Collapsed"/>。
/// </summary>
/// <remarks>
/// 用于中栏 Release 历史列表：把每条 Release 的标识与
/// <c>SelectedRow.LatestRelease</c> 比较，给"最新合格"的那条打上"最新"标记。
/// 两个输入任一为 <c>null</c> 视为不相等（避免 null 误标）。
/// </remarks>
public sealed class AreEqualToVisibilityConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var equal = values is { Length: 2 }
                    && values[0] is not null
                    && values[1] is not null
                    && Equals(values[0], values[1]);

        return equal ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
