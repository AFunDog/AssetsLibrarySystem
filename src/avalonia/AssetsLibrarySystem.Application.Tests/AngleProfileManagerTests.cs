using System.IO;
using AssetsLibrarySystem.Application.Models;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class AngleProfileManagerTests
{
    public static readonly string TestYamlPath;

    static AngleProfileManagerTests()
    {
        // 从源码目录查找测试 YAML
        var baseDir = AppContext.BaseDirectory;
        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "src", "avalonia", "AssetsLibrarySystem.Application", "angle_profiles.yaml");
            if (File.Exists(candidate))
            {
                TestYamlPath = candidate;
                return;
            }
            current = current.Parent;
        }
        // 回退到输出目录
        TestYamlPath = Path.Combine(baseDir, "angle_profiles.yaml");
    }

    [Fact]
    public void Constructor_Throws_WhenFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => new AngleProfileManager("nonexistent.yaml"));
    }

    [Fact]
    public void LoadYaml_Succeeds()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        Assert.NotNull(manager);
    }

    [Fact]
    public void GetProfile_ReturnsDefault_WhenUnknownType()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        var profile = manager.GetProfile("未知类型", null);
        Assert.NotNull(profile);
        Assert.Equal("默认", profile.Subtype);
        Assert.Single(profile.Angles);
        Assert.Equal("整体", profile.Angles[0].Key);
    }

    [Fact]
    public void GetProfile_ReturnsDefault_WhenSubtypeNotFound()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        var profile = manager.GetProfile("音频", "不存在的子类型");
        Assert.NotNull(profile);
        Assert.Equal("默认", profile.Subtype);
    }

    [Fact]
    public void GetProfile_纯音乐_ResolvesShorthandAngles()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        var profile = manager.GetProfile("音频", "纯音乐");
        Assert.NotNull(profile);
        Assert.Equal("纯音乐/伴奏", profile.Label);
        // 简写角度应从 angle_definitions 继承完整属性
        var 曲风 = Assert.Single(profile.Angles, a => a.Key == "曲风");
        Assert.Equal("曲风", 曲风.Label);
        Assert.Equal("描述音乐风格和流派", 曲风.Prompt);
        // max_length 应继承自 angle_definitions（曲风定义为 80）
        // 注意：如果为 120 说明简写解析未正确继承 max_length，
        // 可能是 YamlDotNet 版本差异导致 GetInt() 未识别类型
        Assert.Equal(80, 曲风.MaxLength);
    }

    [Fact]
    public void GetProfile_Audio_Default_HasLegacyAngles()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        var profile = manager.GetProfile("音频", "默认");
        Assert.NotNull(profile);
        Assert.Contains(profile.Angles, a => a.Key == "整体");
        Assert.Contains(profile.Angles, a => a.Key == "乐器");
        Assert.Contains(profile.Angles, a => a.Key == "风格");
        Assert.Contains(profile.Angles, a => a.Key == "情感");
    }

    [Fact]
    public void GetProfile_图片插画_HasCorrectAngles()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        var profile = manager.GetProfile("图片", "插画");
        Assert.NotNull(profile);
        Assert.Contains(profile.Angles, a => a.Key == "视觉风格");
        Assert.Contains(profile.Angles, a => a.Key == "场景");
        Assert.Contains(profile.Angles, a => a.Key == "整体");
    }

    [Fact]
    public void GetProfile_图片照片_HasCorrectAngles()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        var profile = manager.GetProfile("图片", "照片");
        Assert.NotNull(profile);
        Assert.Contains(profile.Angles, a => a.Key == "场景");
        Assert.Contains(profile.Angles, a => a.Key == "整体");
        Assert.DoesNotContain(profile.Angles, a => a.Key == "视觉风格");
    }

    [Fact]
    public void GetProfiles_ReturnsEmpty_ForUnknownType()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        var profiles = manager.GetProfiles("不存在的类型");
        Assert.Empty(profiles);
    }

    [Fact]
    public void GetProfiles_ReturnsAllVideoSubtypes()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        var profiles = manager.GetProfiles("视频");
        Assert.Contains(profiles, p => p.Subtype == "实拍");
        Assert.Contains(profiles, p => p.Subtype == "动画");
        Assert.Contains(profiles, p => p.Subtype == "游戏录制");
        Assert.Contains(profiles, p => p.Subtype == "默认");
    }

    [Fact]
    public void GetProfile_Video_ReturnsRealSubtypeAngles()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        var profile = manager.GetProfile("视频", "实拍");
        Assert.NotNull(profile);
        Assert.Equal("实拍", profile.Subtype);
        Assert.Contains(profile.Angles, a => a.Key == "时间线");
        Assert.Contains(profile.Angles, a => a.Key == "场景");
        Assert.Contains(profile.Angles, a => a.Key == "动作");
        Assert.Contains(profile.Angles, a => a.Key == "镜头");
        Assert.Contains(profile.Angles, a => a.Key == "整体");
    }

    [Fact]
    public void GetProfile_Video_Default_HasTimelineAndOverall()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        var profile = manager.GetProfile("视频", "默认");
        Assert.NotNull(profile);
        Assert.Contains(profile.Angles, a => a.Key == "时间线");
        Assert.Contains(profile.Angles, a => a.Key == "整体");
    }

    [Fact]
    public void GetProfile_Audio_Sfx_HasSoundDescription()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        var profile = manager.GetProfile("音频", "音效");
        Assert.NotNull(profile);
        Assert.Contains(profile.Angles, a => a.Key == "声音描述");
        Assert.Contains(profile.Angles, a => a.Key == "使用场景");
        Assert.DoesNotContain(profile.Angles, a => a.Key == "歌词大意");
    }

    [Fact]
    public void GetProfiles_ReturnsAllSubtypes()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        var profiles = manager.GetProfiles("音频");
        Assert.NotEmpty(profiles);
        Assert.Contains(profiles, p => p.Subtype == "歌曲");
        Assert.Contains(profiles, p => p.Subtype == "纯音乐");
        Assert.Contains(profiles, p => p.Subtype == "音效");
        Assert.Contains(profiles, p => p.Subtype == "默认");
    }

    [Fact]
    public void GetAssetTypes_ReturnsAllTypes()
    {
        var manager = new AngleProfileManager(TestYamlPath);
        var types = manager.GetAssetTypes();
        Assert.Contains("音频", types);
        Assert.Contains("视频", types);
        Assert.Contains("图片", types);
        Assert.Contains("文本", types);
    }

    [Fact]
    public void AngleDefinition_HasCorrectDefaults()
    {
        var angle = new AngleDefinition("测试", "测试标签", "测试提示");
        Assert.Equal("测试", angle.Key);
        Assert.Equal("测试标签", angle.Label);
        Assert.Equal("测试提示", angle.Prompt);
        Assert.Equal(120, angle.MaxLength);
    }

    [Fact]
    public void AngleDefinition_CustomMaxLength()
    {
        var angle = new AngleDefinition("歌词大意", "歌词大意", "分析歌词", 150);
        Assert.Equal(150, angle.MaxLength);
    }
}