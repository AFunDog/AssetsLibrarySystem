using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AssetsLibrarySystem.Application.Models;

/// <summary>匹配高亮结果中的一段文本</summary>
public sealed record HighlightSegment(string Text, bool IsHighlight);

/// <summary>描述 JSON 中的一个视频片段（时间点 + 各角度文本）</summary>
public sealed record VideoSegmentRecord(int SegmentIndex, double StartTime, double EndTime, string AngleType, string Text);

/// <summary>片段时间范围（秒），用于「只补缺失片段」</summary>
public sealed record SegmentTimeRange(double Start, double End);

/// <summary>片段角度向量类型：segN_角度（如 seg0_整体、seg1_场景）</summary>
public static class SegmentAngleType
{
    public const string Prefix = "seg";

    public static string Build(int segmentIndex, string angleType) => $"{Prefix}{segmentIndex}_{angleType}";

    /// <summary>解析 "segN_角度" 格式；非片段角度返回 false</summary>
    public static bool TryParse(string? angleType, out int segmentIndex, out string angle)
    {
        segmentIndex = -1;
        angle = string.Empty;
        if (string.IsNullOrWhiteSpace(angleType) || !angleType.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var underscoreIndex = angleType.IndexOf('_');
        if (underscoreIndex <= Prefix.Length)
        {
            return false;
        }

        var indexText = angleType[Prefix.Length..underscoreIndex];
        if (!int.TryParse(indexText, out segmentIndex) || segmentIndex < 0)
        {
            return false;
        }

        angle = angleType[(underscoreIndex + 1)..];
        return !string.IsNullOrWhiteSpace(angle);
    }
}

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

        // 片段角度（segN_角度）：从 segments 数组提取对应片段的角度文本
        if (SegmentAngleType.TryParse(normalizedAngleType, out var segmentIndex, out var segmentAngle))
        {
            foreach (var segment in EnumerateSegmentAngleTexts(rawDescription))
            {
                if (segment.SegmentIndex == segmentIndex
                    && string.Equals(segment.AngleType, segmentAngle, StringComparison.Ordinal))
                {
                    return segment.Text;
                }
            }

            return string.Empty;
        }

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

    /// <summary>
    /// 遍历描述 JSON 中 segments 数组，产出每个片段每个角度的文本（跳过空文本）。
    /// 用于片段级向量化与检索。
    /// </summary>
    public static IReadOnlyList<VideoSegmentRecord> EnumerateSegmentAngleTexts(string? rawDescription)
    {
        if (string.IsNullOrWhiteSpace(rawDescription))
        {
            return [];
        }

        var trimmed = rawDescription.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("segments", out var segmentsElement)
                || segmentsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var records = new List<VideoSegmentRecord>();
            var segmentIndex = 0;
            foreach (var segmentElement in segmentsElement.EnumerateArray())
            {
                if (segmentElement.ValueKind != JsonValueKind.Object)
                {
                    segmentIndex++;
                    continue;
                }

                double start = 0.0;
                double end = 0.0;
                if (segmentElement.TryGetProperty("start_time", out var startElement) && startElement.ValueKind == JsonValueKind.Number)
                {
                    start = startElement.GetDouble();
                }

                if (segmentElement.TryGetProperty("end_time", out var endElement) && endElement.ValueKind == JsonValueKind.Number)
                {
                    end = endElement.GetDouble();
                }

                foreach (var property in segmentElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, "start_time", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(property.Name, "end_time", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var text = ExtractSegmentText(property.Value);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    records.Add(new VideoSegmentRecord(segmentIndex, start, end, property.Name, text));
                }

                segmentIndex++;
            }

            return records;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>获取 segments 数组的片段总数（含未描述片段）</summary>
    public static int GetSegmentCount(string? rawDescription)
    {
        if (string.IsNullOrWhiteSpace(rawDescription))
        {
            return 0;
        }

        var trimmed = rawDescription.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("segments", out var segmentsElement)
                || segmentsElement.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            return segmentsElement.GetArrayLength();
        }
        catch (JsonException)
        {
            return 0;
        }
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

    // ===== 视频剪辑片段合并（两阶段描述） =====

    /// <summary>
    /// 把新片段（骨架或描述响应）合并进现有描述 JSON。
    /// 规则：整体仅在现有为空时替换；片段按 start_time 匹配（差 &lt;1s 视为同一片段）替换或追加，按时间排序。
    /// </summary>
    public static string MergeClipSegments(string? existingDescription, string? incomingDescription)
    {
        if (string.IsNullOrWhiteSpace(incomingDescription))
        {
            return existingDescription ?? string.Empty;
        }

        var incoming = ParseJsonObject(incomingDescription);
        if (incoming is null)
        {
            return existingDescription ?? incomingDescription;
        }

        var existing = ParseJsonObject(existingDescription);
        if (existing is null)
        {
            return incoming.ToJsonString();
        }

        // 整体：现有非空则保留，否则用新整体
        var existingOverall = existing["整体"];
        if (existingOverall is null || IsEmptyAngle(existingOverall))
        {
            existing["整体"] = incoming["整体"]?.DeepClone();
        }

        // segments 合并
        var incomingSegments = incoming["segments"] as JsonArray;
        if (incomingSegments is { Count: > 0 })
        {
            var existingSegments = existing["segments"] as JsonArray ?? [];
            existing["segments"] = MergeSegmentsArray(existingSegments, incomingSegments);
        }

        return existing.ToJsonString();
    }

    /// <summary>
    /// 获取缺失（未描述）片段的时间范围列表。
    /// 片段判定：除 start_time/end_time 外所有角度文本均为空。
    /// rangeStart/rangeEnd 非空时只返回与该范围重叠的缺失片段。
    /// </summary>
    public static IReadOnlyList<SegmentTimeRange> GetMissingSegmentRanges(string? description, double? rangeStart = null, double? rangeEnd = null)
    {
        var root = ParseJsonObject(description);
        var segments = root?["segments"] as JsonArray;
        if (segments is null)
        {
            return [];
        }

        var missing = new List<SegmentTimeRange>();
        foreach (var segment in segments)
        {
            if (segment is not JsonObject segmentObject)
            {
                continue;
            }

            var start = GetDouble(segmentObject, "start_time", 0.0);
            var end = GetDouble(segmentObject, "end_time", start);

            if (rangeStart is not null && end <= rangeStart.Value)
            {
                continue;
            }

            if (rangeEnd is not null && start >= rangeEnd.Value)
            {
                continue;
            }

            if (IsSegmentMissing(segmentObject))
            {
                missing.Add(new SegmentTimeRange(start, end));
            }
        }

        return missing;
    }

    /// <summary>指定时间范围 [rangeStart, rangeEnd] 内是否已有任何片段覆盖</summary>
    public static bool IsRangeCovered(string? description, double? rangeStart, double? rangeEnd)
    {
        var root = ParseJsonObject(description);
        var segments = root?["segments"] as JsonArray;
        if (segments is null)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment is not JsonObject segmentObject)
            {
                continue;
            }

            var start = GetDouble(segmentObject, "start_time", 0.0);
            var end = GetDouble(segmentObject, "end_time", start);

            if (rangeStart is not null && end <= rangeStart.Value)
            {
                continue;
            }

            if (rangeEnd is not null && start >= rangeEnd.Value)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 手动编辑主描述文本：对剪辑素材（含 segments）把新文本合并进「整体」并保留片段结构；
    /// 非 JSON 或非剪辑 JSON 时直接返回新文本（保持原覆盖行为）。
    /// </summary>
    public static string SetPrimaryText(string? description, string newText)
    {
        var root = ParseJsonObject(description);
        if (root is null || root["segments"] is not JsonArray)
        {
            return newText;
        }

        var overall = root["整体"] as JsonObject ?? [];
        var tags = overall["tags"]?.DeepClone() ?? new JsonArray();
        root["整体"] = new JsonObject
        {
            ["text"] = newText.Trim(),
            ["tags"] = tags,
        };
        return root.ToJsonString();
    }

    private static JsonObject? ParseJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(json);
            return node as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsEmptyAngle(JsonNode? node) =>
        node is not JsonObject obj
        || obj["text"] is not JsonValue text
        || string.IsNullOrWhiteSpace(text.GetValue<string>());

    private static bool IsSegmentMissing(JsonObject segment)
    {
        foreach (var property in segment)
        {
            if (string.Equals(property.Key, "start_time", StringComparison.OrdinalIgnoreCase)
                || string.Equals(property.Key, "end_time", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value is JsonObject angleObject)
            {
                var text = angleObject["text"] as JsonValue;
                if (text is not null && !string.IsNullOrWhiteSpace(text.GetValue<string>()))
                {
                    return false;
                }
            }
            else if (property.Value is JsonValue value && !string.IsNullOrWhiteSpace(value.GetValue<string>()))
            {
                return false;
            }
        }

        return true;
    }

    private static double GetDouble(JsonObject obj, string key, double fallback)
    {
        if (obj[key] is JsonValue value && value.TryGetValue<double>(out var result))
        {
            return result;
        }

        return fallback;
    }

    private static JsonArray MergeSegmentsArray(JsonArray existing, JsonArray incoming)
    {
        var merged = new List<JsonObject>();
        foreach (var node in existing)
        {
            if (node is JsonObject obj)
            {
                merged.Add(obj);
            }
        }

        foreach (var node in incoming)
        {
            if (node is not JsonObject incomingSegment)
            {
                continue;
            }

            var incomingStart = GetDouble(incomingSegment, "start_time", 0.0);
            var replacedIndex = -1;
            for (var i = 0; i < merged.Count; i++)
            {
                var existingStart = GetDouble(merged[i], "start_time", 0.0);
                if (Math.Abs(existingStart - incomingStart) < 1.0)
                {
                    replacedIndex = i;
                    break;
                }
            }

            if (replacedIndex >= 0)
            {
                merged[replacedIndex] = incomingSegment;
            }
            else
            {
                merged.Add(incomingSegment);
            }
        }

        var result = new JsonArray();
        foreach (var segment in merged
                     .OrderBy(seg => GetDouble(seg, "start_time", 0.0))
                     .Select(seg => seg.DeepClone()))
        {
            result.Add(segment);
        }

        return result;
    }
}
