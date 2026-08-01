using System;

namespace AssetsLibrarySystem.Application.Models;

public sealed record AngleDescriptionRecord(
    string AngleKey,
    string Label,
    string Text,
    string[] Tags,
    int MaxLength)
{
    public string TagsDisplay => string.Join("、", Tags);

    /// <summary>所属片段序号（剪辑素材的片段角度记录；null=顶层角度）</summary>
    public int? SegmentIndex { get; init; }

    /// <summary>片段开始时间（秒）</summary>
    public double? StartTime { get; init; }

    /// <summary>片段结束时间（秒）</summary>
    public double? EndTime { get; init; }

    /// <summary>是否为片段级记录</summary>
    public bool IsSegmentRecord => SegmentIndex is not null;

    /// <summary>是否为未描述的骨架片段占位卡片（分割后仅保存时间点）</summary>
    public bool IsMissingSegment { get; init; }

    /// <summary>片段时间标记，如「片段 2 · 00:10-00:25」</summary>
    public string SegmentLabel => StartTime is null || EndTime is null
        ? $"片段 {SegmentIndex + 1}"
        : $"片段 {SegmentIndex + 1} · {FormatTime(StartTime.Value)}-{FormatTime(EndTime.Value)}";

    private static string FormatTime(double seconds)
    {
        var total = Math.Max(0, (int)Math.Round(seconds));
        var minutes = total / 60;
        var secs = total % 60;
        return $"{minutes:00}:{secs:00}";
    }
}