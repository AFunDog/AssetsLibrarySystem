using System;
using System.IO;
using System.Linq;
using AssetsLibrarySystem.Application.Models;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

/// <summary>
/// 片段列表项：缩略图 + 时间范围 + 标签摘要（已分割剪辑素材的分割点列表展示）。
/// </summary>
public sealed partial class SegmentListItemViewModel : ObservableObject
{
    public SegmentListItemViewModel(SegmentBlockRecord block)
    {
        SegmentIndex = block.SegmentIndex;
        Start = block.Start;
        End = block.End;
        IsDescribed = block.IsDescribed;
        TimeRangeText = block.TimeRangeText;
        TagsSummary = block.IsDescribed
            ? block.Tags.Count > 0
                ? string.Join("、", block.Tags.Take(6))
                : "（无标签）"
            : "待描述";
    }

    public int SegmentIndex { get; }
    public double Start { get; }
    public double End { get; }
    public bool IsDescribed { get; }

    /// <summary>时间范围文本，如「0:10-0:25」</summary>
    public string TimeRangeText { get; }

    /// <summary>标签摘要（未描述片段显示「待描述」）</summary>
    public string TagsSummary { get; }

    [ObservableProperty] public partial Bitmap? Thumbnail { get; set; }

    [ObservableProperty] public partial bool HasThumbnail { get; set; }

    /// <summary>设置缩略图；无效数据时保留占位图标</summary>
    public void SetThumbnail(byte[]? jpegBytes)
    {
        if (jpegBytes is null)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(jpegBytes);
            var previous = Thumbnail;
            Thumbnail = new Bitmap(stream);
            previous?.Dispose();
            HasThumbnail = true;
        }
        catch
        {
            // 无效图片数据时保留占位
        }
    }
}
