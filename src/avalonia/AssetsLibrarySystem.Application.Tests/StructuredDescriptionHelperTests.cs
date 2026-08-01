using AssetsLibrarySystem.Application.Models;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class StructuredDescriptionHelperTests
{
    [Fact]
    public void ExtractPrimaryText_ReturnsEmpty_ForNull()
    {
        Assert.Equal("", StructuredDescriptionHelper.ExtractPrimaryText(null));
    }

    [Fact]
    public void ExtractPrimaryText_ReturnsEmpty_ForEmptyString()
    {
        Assert.Equal("", StructuredDescriptionHelper.ExtractPrimaryText(""));
        Assert.Equal("", StructuredDescriptionHelper.ExtractPrimaryText("   "));
    }

    [Fact]
    public void ExtractPrimaryText_ReturnsPlainText_WhenNotJson()
    {
        Assert.Equal("一段描述", StructuredDescriptionHelper.ExtractPrimaryText("一段描述"));
    }

    [Fact]
    public void ExtractPrimaryText_Extracts整体_ObjectFormat()
    {
        const string json = """{"整体":{"text":"日常街景","tags":["街景"]},"风格":{"text":"纪实"}}""";
        Assert.Equal("日常街景", StructuredDescriptionHelper.ExtractPrimaryText(json));
    }

    [Fact]
    public void ExtractPrimaryText_Extracts全面_ObjectFormat_LegacyCompatible()
    {
        const string json = """{"全面":{"text":"一段舒缓的钢琴配乐","tags":["钢琴","舒缓"]}}""";
        Assert.Equal("一段舒缓的钢琴配乐", StructuredDescriptionHelper.ExtractPrimaryText(json));
    }

    [Fact]
    public void ExtractPrimaryText_Extracts全面_StringFormat_LegacyCompatible()
    {
        const string json = """{"全面":"一段安静的环境音"}""";
        Assert.Equal("一段安静的环境音", StructuredDescriptionHelper.ExtractPrimaryText(json));
    }

    [Fact]
    public void ExtractPrimaryText_Prefers整体_Over全面()
    {
        const string json = """{"整体":{"text":"新主角度"},"全面":{"text":"旧主角度"}}""";
        Assert.Equal("新主角度", StructuredDescriptionHelper.ExtractPrimaryText(json));
    }

    [Fact]
    public void ExtractPrimaryText_FallsBack_WhenPrimaryMissing()
    {
        const string json = """{"风格":{"text":"偏抒情"}}""";
        Assert.Equal(json, StructuredDescriptionHelper.ExtractPrimaryText(json));
    }

    // ===== 动态角度测试 =====

    [Fact]
    public void ExtractSegments_ReturnsEmpty_ForNull()
    {
        Assert.Empty(StructuredDescriptionHelper.ExtractSegments(null));
    }

    [Fact]
    public void ExtractSegments_ReturnsEmpty_ForEmptyString()
    {
        Assert.Empty(StructuredDescriptionHelper.ExtractSegments(""));
    }

    [Fact]
    public void ExtractSegments_ParsesDynamicAngles()
    {
        const string json = """
        {
            "场景": {"text": "城市街道", "tags": ["城市"]},
            "动作": {"text": "人物行走", "tags": ["行走"]},
            "整体": {"text": "日常街景", "tags": ["日常"]}
        }
        """;

        var segments = StructuredDescriptionHelper.ExtractSegments(json);

        Assert.Equal(3, segments.Count);
        Assert.Contains(segments, s => s.AngleType == "场景" && s.Text == "城市街道");
        Assert.Contains(segments, s => s.AngleType == "动作" && s.Text == "人物行走");
        Assert.Contains(segments, s => s.AngleType == "整体" && s.Text == "日常街景");
    }

    [Fact]
    public void ExtractSegments_HandlesAudioAngles()
    {
        const string json = """
        {
            "歌词大意": {"text": "关于爱情", "tags": ["爱情"]},
            "曲风": {"text": "流行", "tags": ["流行"]},
            "情感": {"text": "温暖", "tags": []},
            "乐器": {"text": "钢琴", "tags": []},
            "整体": {"text": "一首流行情歌", "tags": []}
        }
        """;

        var segments = StructuredDescriptionHelper.ExtractSegments(json);

        Assert.Equal(5, segments.Count);
        Assert.Contains(segments, s => s.AngleType == "歌词大意");
        Assert.Contains(segments, s => s.AngleType == "曲风");
        Assert.Contains(segments, s => s.AngleType == "情感");
        Assert.Contains(segments, s => s.AngleType == "乐器");
        Assert.Contains(segments, s => s.AngleType == "整体");
    }

    [Fact]
    public void ExtractSegments_KeepsJsonOrder()
    {
        const string json = """
        {
            "动作": {"text": "跑步", "tags": []},
            "场景": {"text": "公园", "tags": []},
            "整体": {"text": "晨跑", "tags": []}
        }
        """;

        var segments = StructuredDescriptionHelper.ExtractSegments(json);

        // 应保持 JSON 中的原始顺序
        Assert.Equal("动作", segments[0].AngleType);
        Assert.Equal("场景", segments[1].AngleType);
        Assert.Equal("整体", segments[2].AngleType);
    }

    [Fact]
    public void ExtractSegments_SkipsEmptyAngles()
    {
        const string json = """
        {
            "场景": {"text": "城市街道", "tags": []},
            "动作": {"text": "", "tags": []},
            "整体": {"text": "街景", "tags": []}
        }
        """;

        var segments = StructuredDescriptionHelper.ExtractSegments(json);

        Assert.Equal(2, segments.Count);
        Assert.DoesNotContain(segments, s => s.AngleType == "动作");
    }

    [Fact]
    public void ExtractSegments_Throws_WhenAllEmpty()
    {
        const string json = """{"场景": {"text": "", "tags": []}, "动作": {"text": "", "tags": []}}""";

        Assert.Throws<System.Text.Json.JsonException>(() =>
            StructuredDescriptionHelper.ExtractSegments(json));
    }

    [Fact]
    public void ExtractSegments_Throws_WhenInvalidJson()
    {
        // 以 { 开头但不是合法 JSON → JsonReaderException（JsonException 的子类）
        const string invalidJson = "{invalid}";

        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            StructuredDescriptionHelper.ExtractSegments(invalidJson));
    }

    [Fact]
    public void ExtractSegments_HandlesStringValueAngle()
    {
        // 角度值可以是字符串而不是对象
        const string json = """{"场景": "森林", "整体": {"text": "自然风光", "tags": []}}""";

        var segments = StructuredDescriptionHelper.ExtractSegments(json);

        Assert.Equal(2, segments.Count);
        Assert.Contains(segments, s => s.AngleType == "场景" && s.Text == "森林");
    }

    [Fact]
    public void ExtractTextByAngle_ReturnsSpecificAngle()
    {
        const string json = """
        {
            "场景": {"text": "森林", "tags": []},
            "整体": {"text": "自然风光", "tags": []}
        }
        """;

        Assert.Equal("森林", StructuredDescriptionHelper.ExtractTextByAngle(json, "场景"));
        Assert.Equal("自然风光", StructuredDescriptionHelper.ExtractTextByAngle(json, "整体"));
    }

    [Fact]
    public void ExtractTextByAngle_FallsBackTo全面_WhenAngleNotFound()
    {
        const string json = """{"全面":{"text":"默认描述","tags":[]}}""";
        Assert.Equal("默认描述", StructuredDescriptionHelper.ExtractTextByAngle(json, "不存在的角度"));
    }

    [Fact]
    public void ExtractTextByAngle_ReturnsEmpty_ForNull()
    {
        Assert.Equal("", StructuredDescriptionHelper.ExtractTextByAngle(null, "场景"));
    }

    [Fact]
    public void ExtractAngleTags_ReturnsNon全面Angles()
    {
        const string json = """
        {
            "场景": {"text": "森林", "tags": []},
            "全面": {"text": "自然", "tags": []},
            "情感": {"text": "宁静", "tags": []}
        }
        """;

        var tags = StructuredDescriptionHelper.ExtractAngleTags(json);

        Assert.Contains(tags, t => t == "场景：森林");
        Assert.Contains(tags, t => t == "情感：宁静");
        Assert.DoesNotContain(tags, t => t.Contains("全面"));
    }

    [Fact]
    public void ExtractAngleTags_ReturnsEmpty_ForAll全面()
    {
        const string json = """{"全面":{"text":"描述","tags":[]}}""";
        Assert.Empty(StructuredDescriptionHelper.ExtractAngleTags(json));
    }

    [Fact]
    public void ExtractAngleTags_Excludes整体PrimaryAngle()
    {
        const string json = """
        {
            "整体": {"text": "总述", "tags": []},
            "场景": {"text": "室内", "tags": []}
        }
        """;

        var tags = StructuredDescriptionHelper.ExtractAngleTags(json);
        Assert.Contains(tags, t => t == "场景：室内");
        Assert.DoesNotContain(tags, t => t.Contains("整体"));
    }

    [Fact]
    public void ExtractAngleTags_ReturnsEmpty_ForNull()
    {
        Assert.Empty(StructuredDescriptionHelper.ExtractAngleTags(null));
    }

    // ===== 视频剪辑片段解析 =====

    private const string ClipDescription = """
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

    [Fact]
    public void SegmentAngleType_BuildAndParse_RoundTrip()
    {
        var built = SegmentAngleType.Build(3, "场景");
        Assert.Equal("seg3_场景", built);

        Assert.True(SegmentAngleType.TryParse(built, out var index, out var angle));
        Assert.Equal(3, index);
        Assert.Equal("场景", angle);

        Assert.False(SegmentAngleType.TryParse("整体", out _, out _));
        Assert.False(SegmentAngleType.TryParse("segX_场景", out _, out _));
    }

    [Fact]
    public void EnumerateSegmentAngleTexts_ReturnsPerSegmentAngles()
    {
        var records = StructuredDescriptionHelper.EnumerateSegmentAngleTexts(ClipDescription);

        Assert.Equal(3, records.Count); // 0-整体, 0-场景, 1-整体（片段 1 场景空、片段 2 全空被跳过）
        Assert.Contains(records, r => r.SegmentIndex == 0 && r.AngleType == "场景" && r.Text == "室内");
        Assert.Contains(records, r => r.SegmentIndex == 1 && r.AngleType == "整体" && r.Text == "中段");
        Assert.DoesNotContain(records, r => r.SegmentIndex == 2);
        var seg1 = records.First(r => r.SegmentIndex == 1);
        Assert.Equal(10.0, seg1.StartTime);
        Assert.Equal(25.0, seg1.EndTime);
    }

    [Fact]
    public void GetSegmentCount_ReturnsTotalSegments()
    {
        Assert.Equal(3, StructuredDescriptionHelper.GetSegmentCount(ClipDescription));
        Assert.Equal(0, StructuredDescriptionHelper.GetSegmentCount(null));
    }

    [Fact]
    public void GetMissingSegmentRanges_ReturnsOnlyEmptySegments()
    {
        var missing = StructuredDescriptionHelper.GetMissingSegmentRanges(ClipDescription);

        Assert.Single(missing);
        Assert.Equal(25.0, missing[0].Start);
        Assert.Equal(40.0, missing[0].End);
    }

    [Fact]
    public void GetMissingSegmentRanges_FiltersByRange()
    {
        // 范围 [5, 12]：片段 0（0-10）重叠但已描述；片段 1（10-25）重叠但已描述 → 无缺失
        var missing = StructuredDescriptionHelper.GetMissingSegmentRanges(ClipDescription, 5.0, 12.0);
        Assert.Empty(missing);

        // 范围 [20, 30]：片段 1（10-25）已描述；片段 2（25-40）缺失 → 1 个缺失
        var missing2 = StructuredDescriptionHelper.GetMissingSegmentRanges(ClipDescription, 20.0, 30.0);
        Assert.Single(missing2);
        Assert.Equal(25.0, missing2[0].Start);
    }

    [Fact]
    public void IsRangeCovered_DetectsCoverage()
    {
        Assert.True(StructuredDescriptionHelper.IsRangeCovered(ClipDescription, 5.0, 12.0));
        Assert.False(StructuredDescriptionHelper.IsRangeCovered(ClipDescription, 100.0, 200.0));
        Assert.False(StructuredDescriptionHelper.IsRangeCovered(null, 0.0, 10.0));
    }

    [Fact]
    public void MergeClipSegments_ReplacesByStartTimeAndKeepsExisting()
    {
        const string incoming = """
        {
            "整体": {"text": "新摘要", "tags": []},
            "segments": [
                {
                    "start_time": 25.0,
                    "end_time": 40.0,
                    "整体": {"text": "结尾已描述", "tags": []},
                    "场景": {"text": "室外", "tags": []}
                }
            ]
        }
        """;

        var merged = StructuredDescriptionHelper.MergeClipSegments(ClipDescription, incoming);

        // 整体保留现有（非空不覆盖）
        using var doc = System.Text.Json.JsonDocument.Parse(merged);
        var overallText = doc.RootElement.GetProperty("整体").GetProperty("text").GetString();
        Assert.Equal("剪辑总览", overallText);
        // 片段 0/1 保留，片段 2 被替换
        Assert.Equal(3, StructuredDescriptionHelper.GetSegmentCount(merged));
        var missing = StructuredDescriptionHelper.GetMissingSegmentRanges(merged);
        Assert.Empty(missing);
    }

    [Fact]
    public void MergeClipSegments_AppendsNewSegmentsWhenNoMatch()
    {
        const string skeleton = """
        {
            "整体": {"text": "", "tags": []},
            "segments": [
                {"start_time": 50.0, "end_time": 60.0, "整体": {"text": "", "tags": []}}
            ]
        }
        """;

        var merged = StructuredDescriptionHelper.MergeClipSegments(ClipDescription, skeleton);

        Assert.Equal(4, StructuredDescriptionHelper.GetSegmentCount(merged));
        Assert.True(StructuredDescriptionHelper.IsRangeCovered(merged, 50.0, 60.0));
    }

    [Fact]
    public void ExtractTextByAngle_SupportsSegmentAngleType()
    {
        var text = StructuredDescriptionHelper.ExtractTextByAngle(ClipDescription, "seg0_场景");
        Assert.Equal("室内", text);

        var missing = StructuredDescriptionHelper.ExtractTextByAngle(ClipDescription, "seg5_场景");
        Assert.Equal("", missing);
    }

    [Fact]
    public void SetPrimaryText_MergesIntoOverallAndKeepsSegments()
    {
        var merged = StructuredDescriptionHelper.SetPrimaryText(ClipDescription, "新的整体摘要");

        using var doc = System.Text.Json.JsonDocument.Parse(merged);
        var root = doc.RootElement;
        // 整体被替换为新文本
        Assert.Equal("新的整体摘要", root.GetProperty("整体").GetProperty("text").GetString());
        // 片段结构完整保留
        Assert.Equal(3, root.GetProperty("segments").GetArrayLength());
        var missing = StructuredDescriptionHelper.GetMissingSegmentRanges(merged);
        Assert.Single(missing); // 片段 2 仍缺失
    }

    [Fact]
    public void SetPrimaryText_ReturnsPlainText_WhenNotClipJson()
    {
        // 非 JSON / 无 segments 的 JSON → 直接覆盖（保持原行为）
        Assert.Equal("新文本", StructuredDescriptionHelper.SetPrimaryText(null, "新文本"));
        Assert.Equal("新文本", StructuredDescriptionHelper.SetPrimaryText("旧文本", "新文本"));
        Assert.Equal("新文本", StructuredDescriptionHelper.SetPrimaryText("""{"整体":"旧"}""", "新文本"));
    }
}