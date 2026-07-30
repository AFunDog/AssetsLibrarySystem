using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AssetsLibrarySystem.Avalonia.Converters;

/// <summary>
/// 将字符串数字转换为 int（用于 ProgressBar Value 绑定）。
/// Metrics 中的 Value 是 "42" 格式的字符串。
/// </summary>
public sealed class StringToIntConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && int.TryParse(s, out var intVal))
            return intVal;
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}