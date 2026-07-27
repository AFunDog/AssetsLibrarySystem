using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class AssetDescriptionServiceTests
{
    [Fact]
    public void BuildDynamicSystemPrompt_ContainsAngleKeys()
    {
        var angles = new[]
        {
            new AngleDefinition("场景", "场景环境", "描述视频中的场景和环境", 100),
            new AngleDefinition("动作", "动作活动", "描述视频中的人物动作", 100),
            new AngleDefinition("整体", "整体", "一句话概括", 120),
        };

        var prompt = AssetDescriptionService.BuildDynamicSystemPrompt("视频", angles);

        Assert.Contains("视频素材", prompt);
        Assert.Contains("\"场景\"", prompt);
        Assert.Contains("\"动作\"", prompt);
        Assert.Contains("\"整体\"", prompt);
        Assert.Contains("描述视频中的场景和环境", prompt);
        Assert.Contains("描述视频中的人物动作", prompt);
        Assert.Contains("100 字", prompt);
        Assert.Contains("120 字", prompt);
    }

    [Fact]
    public void BuildDynamicSystemPrompt_ContainsJsonExample()
    {
        var angles = new[]
        {
            new AngleDefinition("场景", "场景环境", "描述场景", 100),
            new AngleDefinition("整体", "整体", "概括", 120),
        };

        var prompt = AssetDescriptionService.BuildDynamicSystemPrompt("视频", angles);

        // 验证输出了 JSON 示例
        Assert.Contains("输出格式示例", prompt);
        Assert.Contains("\"场景\": { \"text\": \"...\", \"tags\": [\"...\"] }", prompt);
        Assert.Contains("\"整体\": { \"text\": \"...\", \"tags\": [\"...\"] }", prompt);
    }

    [Fact]
    public void BuildDynamicSystemPrompt_ContainsOutputRequirements()
    {
        var angles = new[]
        {
            new AngleDefinition("整体", "整体", "概括", 120),
        };

        var prompt = AssetDescriptionService.BuildDynamicSystemPrompt("音频", angles);

        Assert.Contains("音频素材", prompt);
        Assert.Contains("只描述当前素材内容本身", prompt);
        Assert.Contains("只能输出 JSON", prompt);
        Assert.Contains("每个字段都是对象", prompt);
        Assert.Contains("不超过对应字段的最大字数", prompt);
    }

    [Fact]
    public void BuildDynamicSystemPrompt_SingleAngle_FormatCorrect()
    {
        var angles = new[]
        {
            new AngleDefinition("整体", "整体", "概括", 120),
        };

        var prompt = AssetDescriptionService.BuildDynamicSystemPrompt("文本", angles);

        // 验证 JSON 示例中只有一条
        Assert.Contains("\"整体\": { \"text\": \"...\", \"tags\": [\"...\"] }", prompt);
        // 没有逗号结尾（唯一字段）
        Assert.DoesNotContain("\"整体\": { \"text\": \"...\", \"tags\": [\"...\"] },", prompt);
    }

    [Fact]
    public void BuildDynamicSystemPrompt_EmptyAngles_StillProducesValidPrompt()
    {
        var prompt = AssetDescriptionService.BuildDynamicSystemPrompt("视频", []);

        Assert.Contains("视频素材", prompt);
        Assert.Contains("只能输出 JSON", prompt);
        // 空角度列表时，JSON 必须包含且只包含 空列表
        Assert.Contains("必须包含且只包含以下字段", prompt);
    }

    [Fact]
    public void BuildDynamicSystemPrompt_PictureFormat_ProducesCorrectLabel()
    {
        var angles = new[] { new AngleDefinition("整体", "整体", "概括", 120) };

        var prompt = AssetDescriptionService.BuildDynamicSystemPrompt("图片", angles);

        Assert.Contains("图片素材", prompt);
    }

    [Fact]
    public void BuildDynamicSystemPrompt_TextFormat_ProducesCorrectLabel()
    {
        var angles = new[] { new AngleDefinition("整体", "整体", "概括", 120) };

        var prompt = AssetDescriptionService.BuildDynamicSystemPrompt("文本", angles);

        Assert.Contains("文本素材", prompt);
    }
}