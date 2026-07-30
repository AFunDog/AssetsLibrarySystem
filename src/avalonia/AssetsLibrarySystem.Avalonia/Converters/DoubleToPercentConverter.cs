using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AssetsLibrarySystem.Avalonia.Converters;

/// <summary>
/// 将 0-1 范围的 double 值转换为 0-100 的百分比值（用于 ProgressBar）。
/// </summary>
public sealed class DoubleToPercentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d)
            return Math.Clamp(d * 100.0, 0.0, 100.0);
        if (value is float f)
            return Math.Clamp(f * 100.0, 0.0, 100.0);
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}