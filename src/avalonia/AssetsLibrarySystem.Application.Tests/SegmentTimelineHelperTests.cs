using AssetsLibrarySystem.Application.Models;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class SegmentTimelineHelperTests
{
    private const string MixedDescription = """
    {
        "整体": {"text": "剪辑总览", "tags": ["剪辑"]},
        "segments": [
            {
                "start_time": 0.0,
                "end_time": 10.0,
                "整体": {"text": "开场", "tags": []},
                "场景": {"text": "室内", "tags": []}
            },
            {
                "start_time": 10.0,
                "end_time": 25.0,
                "整体": {"text": "中段", "tags": []},
                "场景": {"text": "", "tags": []}
            },
            {
                "start_time": 25.0,
                "end_time": 40.0,
                "整体": {"text": "", "tags": []},
                "场景": {"text": "", "tags": []}
            }
        ]
    }
    """;

    private const string SkeletonOnlyDescription = """
    {
        "整体": {"text": "", "tags": []},
        "segments": [
            {"start_time": 0.0, "end_time": 8.0},
            {"start_time": 8.0, "end_time": 20.0}
        ]
    }
    """;

    [Fact]
    public void Build_ReturnsBlocksColumnsAndTicks()
    {
        var data = SegmentTimelineHelper.Build(MixedDescription);

        Assert.NotNull(data);
        Assert.Equal(3, data.Blocks.Count);

        // 列比例按秒数：10*,15*,15*
        Assert.Equal("10*,15*,15*", data.ColumnDefinitions);

        // 分割点刻度：各段起点 + 总终点
        Assert.Equal("0:00 / 0:10 / 0:25 / 0:40", data.TimelineText);
        Assert.Equal(40.0, data.TotalSeconds);

        // 已描述与未描述状态
        Assert.True(data.Blocks[0].IsDescribed);
        Assert.True(data.Blocks[1].IsDescribed);
        Assert.False(data.Blocks[2].IsDescribed);

        // 时间范围文本（ToolTip）
        Assert.Equal("0:25-0:40", data.Blocks[2].TimeRangeText);
        Assert.Equal(2, data.Blocks[2].SegmentIndex);
        Assert.Equal(25.0, data.Blocks[2].Start);
        Assert.Equal(40.0, data.Blocks[2].End);
    }

    [Fact]
    public void Build_SkeletonOnly_AllBlocksNotDescribed()
    {
        var data = SegmentTimelineHelper.Build(SkeletonOnlyDescription);

        Assert.NotNull(data);
        Assert.Equal(2, data.Blocks.Count);
        Assert.All(data.Blocks, block => Assert.False(block.IsDescribed));
        Assert.Equal("8*,12*", data.ColumnDefinitions);
        Assert.Equal("0:00 / 0:08 / 0:20", data.TimelineText);
    }

    [Fact]
    public void Build_ReturnsNull_ForNullOrNoSegments()
    {
        Assert.Null(SegmentTimelineHelper.Build(null));
        Assert.Null(SegmentTimelineHelper.Build("   "));
        Assert.Null(SegmentTimelineHelper.Build("一段纯文本描述"));
        Assert.Null(SegmentTimelineHelper.Build("""{"整体":{"text":"无片段"}}"""));
    }

    [Fact]
    public void Build_ExtractsPerSegmentTags()
    {
        const string taggedDescription = """
        {
            "整体": {"text": "总览", "tags": ["剪辑"]},
            "segments": [
                {
                    "start_time": 0.0,
                    "end_time": 10.0,
                    "整体": {"text": "开场", "tags": ["日常", "开场"]},
                    "场景": {"text": "室内", "tags": ["室内", "日常"]}
                },
                {
                    "start_time": 10.0,
                    "end_time": 25.0,
                    "整体": {"text": "", "tags": []}
                }
            ]
        }
        """;

        var data = SegmentTimelineHelper.Build(taggedDescription);

        Assert.NotNull(data);
        // 片段 0：各角度 tags 合并去重，保持出现顺序
        Assert.Equal(new[] { "日常", "开场", "室内" }, data.Blocks[0].Tags);
        // 片段 1：无标签
        Assert.Empty(data.Blocks[1].Tags);
    }
}
