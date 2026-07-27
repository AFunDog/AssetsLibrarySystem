using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class SubtypeDetectorTests
{
    private readonly SubtypeDetector _detector = new();

    [Fact]
    public void DetectSubtype_ReturnsNull_ForUnknownType()
    {
        var asset = CreateAsset("图片", "photo.png", "photos/");
        Assert.Null(_detector.DetectSubtype(asset));
    }

    // ===== 音频子类型检测 =====

    [Fact]
    public void DetectAudioSubtype_SfxPrefix_Returns音效()
    {
        var asset = CreateAsset("音频", "SFX_explosion.wav", "audio/");
        Assert.Equal("音效", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectAudioSubtype_SfxLowercasePrefix_Returns音效()
    {
        var asset = CreateAsset("音频", "sfx_click.wav", "audio/");
        Assert.Equal("音效", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectAudioSubtype_SePrefix_Returns音效()
    {
        var asset = CreateAsset("音频", "SE_footstep.wav", "audio/");
        Assert.Equal("音效", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectAudioSubtype_EffectPrefix_Returns音效()
    {
        var asset = CreateAsset("音频", "effect_gun.wav", "audio/");
        Assert.Equal("音效", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectAudioSubtype_SfxPath_Returns音效()
    {
        var asset = CreateAsset("音频", "hit.wav", "audio/sfx/");
        Assert.Equal("音效", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectAudioSubtype_EffectPath_Returns音效()
    {
        var asset = CreateAsset("音频", "explosion.wav", "assets/effect/");
        Assert.Equal("音效", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectAudioSubtype_VoPrefix_Returns歌曲()
    {
        var asset = CreateAsset("音频", "VO_dialogue.wav", "audio/");
        Assert.Equal("歌曲", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectAudioSubtype_VocalPath_Returns歌曲()
    {
        var asset = CreateAsset("音频", "song.wav", "audio/vocal/");
        Assert.Equal("歌曲", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectAudioSubtype_BgmPrefix_Returns纯音乐()
    {
        var asset = CreateAsset("音频", "BGM_happy.mp3", "audio/");
        Assert.Equal("纯音乐", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectAudioSubtype_MusicPath_Returns纯音乐()
    {
        var asset = CreateAsset("音频", "background.mp3", "audio/bgm/");
        Assert.Equal("纯音乐", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectAudioSubtype_LoopPath_Returns纯音乐()
    {
        var asset = CreateAsset("音频", "loop.mp3", "audio/melody/");
        Assert.Equal("纯音乐", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectAudioSubtype_SmallFile_Returns音效()
    {
        var asset = CreateAsset("音频", "click.wav", "audio/", fileSize: 50 * 1024);
        Assert.Equal("音效", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectAudioSubtype_UnknownName_ReturnsNull()
    {
        var asset = CreateAsset("音频", "recording001.mp3", "audio/", fileSize: 5 * 1024 * 1024);
        Assert.Null(_detector.DetectSubtype(asset));
    }

    // ===== 视频子类型检测 =====

    [Fact]
    public void DetectVideoSubtype_GamePrefix_Returns游戏录制()
    {
        var asset = CreateAsset("视频", "GAME_level1.mp4", "videos/");
        Assert.Equal("游戏录制", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectVideoSubtype_RecPrefix_Returns游戏录制()
    {
        var asset = CreateAsset("视频", "REC_2024.mp4", "videos/");
        Assert.Equal("游戏录制", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectVideoSubtype_录屏ChinesePrefix_Returns游戏录制()
    {
        var asset = CreateAsset("视频", "录屏_演示.mp4", "videos/");
        Assert.Equal("游戏录制", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectVideoSubtype_GameplayPrefix_Returns游戏录制()
    {
        var asset = CreateAsset("视频", "GAMEPLAY_boss.mp4", "videos/");
        Assert.Equal("游戏录制", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectVideoSubtype_GamePath_Returns游戏录制()
    {
        var asset = CreateAsset("视频", "recording.mp4", "some/game/", fileSize: 50 * 1024 * 1024);
        Assert.Equal("游戏录制", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectVideoSubtype_GameplayPath_Returns游戏录制()
    {
        var asset = CreateAsset("视频", "recording.mp4", "assets/gameplay/", fileSize: 50 * 1024 * 1024);
        Assert.Equal("游戏录制", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectVideoSubtype_CgPrefix_Returns动画()
    {
        var asset = CreateAsset("视频", "CG_opening.mp4", "videos/");
        Assert.Equal("动画", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectVideoSubtype_AnimePrefix_Returns动画()
    {
        var asset = CreateAsset("视频", "ANIME_scene.mp4", "videos/");
        Assert.Equal("动画", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectVideoSubtype_AnimationPath_Returns动画()
    {
        var asset = CreateAsset("视频", "cutscene.mp4", "assets/animation/", fileSize: 50 * 1024 * 1024);
        Assert.Equal("动画", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectVideoSubtype_SpinePath_Returns动画()
    {
        var asset = CreateAsset("视频", "character.mp4", "assets/spine/", fileSize: 50 * 1024 * 1024);
        Assert.Equal("动画", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectVideoSubtype_SmallFile_Returns实拍()
    {
        var asset = CreateAsset("视频", "clip.mp4", "videos/", fileSize: 2 * 1024 * 1024);
        Assert.Equal("实拍", _detector.DetectSubtype(asset));
    }

    [Fact]
    public void DetectVideoSubtype_UnknownName_ReturnsNull()
    {
        var asset = CreateAsset("视频", "my_video.mp4", "videos/", fileSize: 50 * 1024 * 1024);
        Assert.Null(_detector.DetectSubtype(asset));
    }

    private static ManagedAssetRecord CreateAsset(
        string assetType,
        string name,
        string relativePath,
        long fileSize = 1024 * 1024)
    {
        return new ManagedAssetRecord
        {
            DatabaseId = 1,
            AssetUid = $"test_{name}",
            Name = name,
            AssetType = assetType,
            RelativePath = $"{relativePath.TrimEnd('/')}/{name}",
            LocalPath = $"C:/test/{relativePath.TrimEnd('/')}/{name}",
            FileSize = fileSize,
            ContentHash = "hash123",
        };
    }
}