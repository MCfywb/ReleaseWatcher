using System.Globalization;
using System.Windows.Data;

namespace ReleaseWatcher.App.Converters;

/// <summary>
/// 把字节数格式化为易读的文件大小文本（"1.2 MB" / "856 KB"）。
/// </summary>
/// <remarks>
/// 下载资产列表直接绑定 <c>ReleaseAsset.SizeInBytes</c>（long），
/// 逐字节展示没有可读性；格式化属纯展示职责，故放在转换器
/// 而不是领域实体上（Domain 不应知道 UI 单位约定）。
/// 0 视为"大小未知"给明确文案，而非显示 "0 B" 造成误解。
/// </remarks>
public sealed class FileSizeConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        long bytes = value is long l ? l : 0;

        if (bytes <= 0)
        {
            return "大小未知";
        }

        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        // invariant 格式：小数点与千分位不随系统区域变化，避免列宽跳动。
        return string.Create(CultureInfo.InvariantCulture, $"{size:0.#} {units[unitIndex]}");
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("FileSizeConverter 仅用于单向展示。");
}
