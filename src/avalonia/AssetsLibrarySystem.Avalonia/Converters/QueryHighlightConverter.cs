using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.ViewModels;

namespace AssetsLibrarySystem.Avalonia.Converters;

/// <summary>
/// 多值转换器：将素材描述文本和搜索关键词转换为高亮分段列表。
/// 绑定顺序：ConverterParameter="DescriptionProperty,QueryProperty"
/// </summary>
public sealed class QueryHighlightConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return Array.Empty<object>();

        var description = values[0] as string;
        var query = values[1] as string;

        return StructuredDescriptionHelper.HighlightMatches(description, query);
    }

    public IList<object?> ConvertBack(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}