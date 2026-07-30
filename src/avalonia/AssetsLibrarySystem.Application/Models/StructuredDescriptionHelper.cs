using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AssetsLibrarySystem.Application.Models;

/// <summary>匹配高亮结果中的一段文本</summary>
public sealed record HighlightSegment(string Text, bool IsHighlight);

public static class StructuredDescriptionHelper
{
    /// <summary>
/// 将文本中的匹配关键词高亮为 <see cref="HighlightSegment"/> 列表。
/// 不区分大小写，支持中文和英文关键词匹配。
/// </summary>
/// <param name="text">要搜索的文本</param>
/// <param name="query">搜索关键词</param>
/// <returns>高亮分段列表，匹配部分 IsHighlight=true</returns>
public static IReadOnlyList<HighlightSegment> HighlightMatches(string? text, string? query)
{
    if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
    {
        return string.IsNullOrWhiteSpace(text)
            ? []
            : [new HighlightSegment(text.Trim(), false)];
    }

    var trimmedText = text.Trim();
    var trimmedQuery = query.Trim();

    // 对查询词按空格分词，每段独立高亮
    var queryWords = trimmedQuery
        .Split([' ', '\t', '\n', '\r', '，', '　', '、'], StringSplitOptions.RemoveEmptyEntries)
        .Where(w => w.Length >= 1)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(w => w.Length)
        .ToList();

    if (queryWords.Count == 0)
    {
        return [new HighlightSegment(trimmedText, false)];
    }

    var segments = new List<HighlightSegment>();
    var remaining = trimmedText.AsSpan();

    while (remaining.Length > 0)
    {
        // 查找最近匹配位置
        int? bestMatchIndex = null;
        string? bestMatchWord = null;

        foreach (var word in queryWords)
        {
            var matchIdx = remaining.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (matchIdx >= 0 && (bestMatchIndex is null || matchIdx < bestMatchIndex))
            {
                bestMatchIndex = matchIdx;
                bestMatchWord = word;
            }
        }

        if (bestMatchIndex is null || bestMatchWord is null)
        {
            // 剩余部分无匹配
            segments.Add(new HighlightSegment(remaining.ToString(), false));
            break;
        }

        // 匹配前的非高亮段
        if (bestMatchIndex.Value > 0)
        {
            segments.Add(new HighlightSegment(remaining[..bestMatchIndex.Value].ToString(), false));
        }

        // 高亮匹配段
        segments.Add(new HighlightSegment(remaining.Slice(bestMatchIndex.Value, bestMatchWord.Length).ToString(), true));

        // 跳过已匹配部分
        remaining = remaining[(bestMatchIndex.Value + bestMatchWord.Length)..];
    }

    return segments;
}

public static string ExtractPrimaryText(string? rawDescription)
    {
        if (string.IsNullOrWhiteSpace(rawDescription))
        {
            return string.Empty;
        }

        var trimmed = rawDescription.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return trimmed;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return trimmed;
            }

            // 主角度优先「整体」，兼容历史数据「全面」。
            foreach (var primaryKey in new[]
                     {
                         AssetDescriptionVectorDocument.DefaultAngleType,
                         AssetDescriptionVectorDocument.LegacyPrimaryAngleType
                     })
            {
                if (!document.RootElement.TryGetProperty(primaryKey, out var comprehensiveElement))
                {
                    continue;
                }

                if (comprehensiveElement.ValueKind == JsonValueKind.String)
                {
                    return comprehensiveElement.GetString()?.Trim() ?? string.Empty;
                }

                if (comprehensiveElement.ValueKind == JsonValueKind.Object
                    && comprehensiveElement.TryGetProperty("text", out var textElement)
                    && textElement.ValueKind == JsonValueKind.String)
                {
                    return textElement.GetString()?.Trim() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            return trimmed;
        }

        return trimmed;
    }

    public static IReadOnlyList<StructuredDescriptionSegment> ExtractSegments(string? rawDescription)
    {
        if (string.IsNullOrWhiteSpace(rawDescription))
        {
            return [];
        }

        var trimmed = rawDescription.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return [new StructuredDescriptionSegment(AssetDescriptionVectorDocument.DefaultAngleType, trimmed)];
        }

        using var document = JsonDocument.Parse(trimmed);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"素材描述 JSON 顶层不是对象类型: {document.RootElement.ValueKind}");
        }

        var segments = new List<StructuredDescriptionSegment>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var text = ExtractSegmentText(property.Value);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            segments.Add(new StructuredDescriptionSegment(property.Name, text));
        }

        if (segments.Count > 0)
        {
            SortSegments(segments);
            return segments;
        }

        throw new JsonException("素材描述 JSON 中没有可向量化的有效角度文本。");
    }

    public static string ExtractTextByAngle(string? rawDescription, string? angleType)
    {
        var normalizedAngleType = string.IsNullOrWhiteSpace(angleType)
            ? AssetDescriptionVectorDocument.DefaultAngleType
            : angleType.Trim();

        try
        {
            var segments = ExtractSegments(rawDescription);
            foreach (var segment in segments)
            {
                if (string.Equals(segment.NormalizedAngleType, normalizedAngleType, StringComparison.Ordinal))
                {
                    return segment.NormalizedText;
                }
            }
        }
        catch (JsonException)
        {
            // 搜索展示场景下，JSON 解析失败不阻断流程，回退到通用提取
        }

        return ExtractPrimaryText(rawDescription);
    }

    public static string[] ExtractAngleTags(string? rawDescription)
    {
        try
        {
            return ExtractSegments(rawDescription)
                .Where(segment => !AssetDescriptionVectorDocument.IsPrimaryAngleType(segment.NormalizedAngleType))
                .Select(segment => $"{segment.NormalizedAngleType}：{segment.NormalizedText}")
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? ExtractSegmentText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString()?.Trim();
        }

        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("text", out var textElement)
            && textElement.ValueKind == JsonValueKind.String)
        {
            return textElement.GetString()?.Trim();
        }

        return null;
    }

    private static void SortSegments(List<StructuredDescriptionSegment> segments)
    {
        // 保持 JSON 中的原始顺序，不做硬编码优先级排序。
        // 子类型和角度配置由 C# 端的 AngleProfileManager 管理，
        // 不再在此处硬编码 "全面"→0, "乐器"→1 等优先级。
        // 仅按出现顺序稳定排序即可。
    }
}
