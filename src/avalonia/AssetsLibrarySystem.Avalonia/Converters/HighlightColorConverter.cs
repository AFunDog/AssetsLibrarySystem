using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AssetsLibrarySystem.Avalonia.Converters;

/// <summary>
/// 布尔值到高亮颜色的转换器。
/// true 返回主题强调色（高亮），false 返回默认文本色。
/// </summary>
public sealed class HighlightColorConverter : IValueConverter
{
    // 高亮颜色：蓝色（匹配系统强调色）
    private static readonly SolidColorBrush HighlightBrush = new(Color.FromRgb(76, 148, 255));
    // 正常颜色：白色
    private static readonly SolidColorBrush NormalBrush = new(Colors.White);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool isHighlight && isHighlight
            ? HighlightBrush
            : NormalBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}