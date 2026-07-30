using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AssetsLibrarySystem.Avalonia.Converters;

/// <summary>
/// 布尔值到透明度转换器：true=1.0, false=0.4
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? 1.0 : 0.4;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}