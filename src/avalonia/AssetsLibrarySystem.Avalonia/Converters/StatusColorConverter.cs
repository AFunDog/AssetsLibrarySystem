using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using AvaloniaApp = Avalonia.Application;

namespace AssetsLibrarySystem.Avalonia.Converters;

/// <summary>
/// 将状态颜色键（如 "StatusDescribedBrush"）转换为对应的主题 <see cref="IBrush"/>。
/// </summary>
public sealed class StatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string colorKey)
            return new SolidColorBrush(Colors.Gray);

        var app = AvaloniaApp.Current;
        if (app is null)
            return new SolidColorBrush(Colors.Gray);

        // 直接访问资源字典
        if (app.Resources.TryGetResource(colorKey, app.ActualThemeVariant, out var resource)
            && resource is IBrush brush)
            return brush;

        return new SolidColorBrush(Colors.Gray);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}