using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AssetsLibrarySystem.Application.Models;

/// <summary>分段时间轴上的一个片段块（用于分割点可视化）</summary>
public sealed record SegmentBlockRecord(
    int SegmentIndex,
    double Start,
    double End,
    bool IsDescribed)
{
    /// <summary>片段各角度的标签合并（去重，保持出现顺序）</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>时间范围文本，如「0:10-0:25」，用于 ToolTip</summary>
    public string TimeRangeText => $"{FormatTime(Start)}-{FormatTime(End)}";

    /// <summary>ToolTip 完整文本：片段号 + 时间范围 + 描述状态</summary>
    public string ToolTipText => $"片段 {SegmentIndex + 1} · {TimeRangeText} · {(IsDescribed ? "已描述" : "待描述")}";

    private static string FormatTime(double seconds)
    {
        var total = Math.Max(0, (int)Math.Round(seconds));
        var minutes = total / 60;
        var secs = total % 60;
        return $"{minutes}:{secs:00}";
    }
}

/// <summary>分段时间轴数据：片段块 + 列比例 + 分割点刻度文本</summary>
public sealed record SegmentTimelineData(
    IReadOnlyList<SegmentBlockRecord> Blocks,
    string ColumnDefinitions,
    string TimelineText,
    double TotalSeconds);

/// <summary>
/// 从描述 JSON 构建分段时间轴数据（复用 <see cref="StructuredDescriptionHelper.EnumerateSegmentSkeletons"/>）。
/// 无有效片段时返回 null，调用方隐藏时间轴。
/// </summary>
public static class SegmentTimelineHelper
{
    /// <summary>
    /// 构建时间轴数据。列比例为每段时长的星号比例（如 "40*,30*,30*"），
    /// 刻度文本为各分割点时间（如 "0:00 / 0:10 / 0:25 / 0:40"）。
    /// </summary>
    public static SegmentTimelineData? Build(string? descriptionJson)
    {
        var skeletons = StructuredDescriptionHelper.EnumerateSegmentSkeletons(descriptionJson);
        if (skeletons.Count == 0)
        {
            return null;
        }

        var totalSeconds = skeletons.Max(s => s.End);
        var columnParts = new List<string>(skeletons.Count);
        var tickTimes = new List<double>(skeletons.Count + 1);
        var blocks = new List<SegmentBlockRecord>(skeletons.Count);
        var tagsBySegment = ExtractSegmentTags(descriptionJson);

        foreach (var skeleton in skeletons)
        {
            var duration = Math.Max(0.0, skeleton.End - skeleton.Start);
            // 星号列比例：时长不足 1 时给最小 1，保证异常数据也可见
            var weight = Math.Max(1, (int)Math.Round(duration));
            columnParts.Add($"{weight}*");
            tickTimes.Add(skeleton.Start);
            blocks.Add(new SegmentBlockRecord(
                skeleton.SegmentIndex,
                skeleton.Start,
                skeleton.End,
                IsDescribed: !skeleton.IsMissing)
            {
                Tags = tagsBySegment.GetValueOrDefault(skeleton.SegmentIndex, []),
            });
        }

        tickTimes.Add(totalSeconds);

        var timelineText = new StringBuilder();
        foreach (var tick in tickTimes)
        {
            if (timelineText.Length > 0)
            {
                timelineText.Append(" / ");
            }

            timelineText.Append(FormatTime(tick));
        }

        return new SegmentTimelineData(
            Blocks: blocks,
            ColumnDefinitions: string.Join(",", columnParts),
            TimelineText: timelineText.ToString(),
            TotalSeconds: totalSeconds);
    }

    /// <summary>按片段索引收集各角度 tags（去重，保持出现顺序）</summary>
    private static Dictionary<int, string[]> ExtractSegmentTags(string? descriptionJson)
    {
        var result = new Dictionary<int, string[]>();
        if (string.IsNullOrWhiteSpace(descriptionJson))
        {
            return result;
        }

        var trimmed = descriptionJson.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("segments", out var segmentsElement)
                || segmentsElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            var index = 0;
            foreach (var segmentElement in segmentsElement.EnumerateArray())
            {
                var tags = new List<string>();
                if (segmentElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in segmentElement.EnumerateObject())
                    {
                        if (property.Name is "start_time" or "end_time")
                        {
                            continue;
                        }

                        if (property.Value.ValueKind == JsonValueKind.Object
                            && property.Value.TryGetProperty("tags", out var tagsElement)
                            && tagsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var tag in tagsElement.EnumerateArray())
                            {
                                if (tag.ValueKind == JsonValueKind.String)
                                {
                                    var text = tag.GetString();
                                    if (!string.IsNullOrWhiteSpace(text))
                                    {
                                        tags.Add(text);
                                    }
                                }
                            }
                        }
                    }
                }

                result[index] = tags.Distinct(StringComparer.Ordinal).ToArray();
                index++;
            }
        }
        catch (JsonException)
        {
            // 标签解析失败不影响时间轴数据
        }

        return result;
    }

    private static string FormatTime(double seconds)
    {
        var total = Math.Max(0, (int)Math.Round(seconds));
        var minutes = total / 60;
        var secs = total % 60;
        return $"{minutes}:{secs:00}";
    }
}
