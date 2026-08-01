using System;
using System.Collections.ObjectModel;
using AssetsLibrarySystem.Application.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

/// <summary>
/// 剪辑素材描述详情按片段分组展示：一个分组 = 一段时间的切片，
/// 组内为该片段各角度的描述记录（整体概述/场景环境/动作事件/镜头）。
/// </summary>
public sealed partial class SegmentDescriptionGroupViewModel : ObservableObject
{
    public SegmentDescriptionGroupViewModel(int segmentIndex, double startTime, double endTime)
    {
        SegmentIndex = segmentIndex;
        StartTime = startTime;
        EndTime = endTime;
    }

    /// <summary>片段序号（0-based，对应 segments 数组下标）</summary>
    public int SegmentIndex { get; }

    public double StartTime { get; }

    public double EndTime { get; }

    /// <summary>组头文字，如「片段 2 · 00:10-00:25」</summary>
    public string HeaderText => $"片段 {SegmentIndex + 1} · {FormatTime(StartTime)}-{FormatTime(EndTime)}";

    /// <summary>该片段是否还没有任何角度描述（骨架占位）</summary>
    [ObservableProperty]
    public partial bool IsMissing { get; set; } = true;

    /// <summary>组内角度描述记录（按 JSON 属性顺序）</summary>
    public ObservableCollection<AngleDescriptionRecord> Angles { get; } = [];

    private static string FormatTime(double seconds)
    {
        var total = Math.Max(0, (int)Math.Round(seconds));
        var minutes = total / 60;
        var secs = total % 60;
        return $"{minutes:00}:{secs:00}";
    }
}
