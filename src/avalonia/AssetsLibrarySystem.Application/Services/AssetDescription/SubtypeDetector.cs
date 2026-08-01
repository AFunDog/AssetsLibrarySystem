using System;
using System.IO;
using System.Linq;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

/// <summary>
/// 子类型检测器接口
/// </summary>
public interface ISubtypeDetector
{
    /// <summary>检测素材的子类型，返回 null 表示无法确定</summary>
    string? DetectSubtype(ManagedAssetRecord asset);
}

/// <summary>
/// 启发式子类型检测器。
/// 根据文件名前缀、路径关键词、时长等元数据判断子类型。
/// </summary>
public sealed class SubtypeDetector : ISubtypeDetector
{
    public string? DetectSubtype(ManagedAssetRecord asset)
    {
        if (asset.AssetType == "音频")
            return DetectAudioSubtype(asset);
        if (asset.AssetType == "视频" || asset.AssetType == "视频剪辑")
            return DetectVideoSubtype(asset);
        return null;
    }

    private static string? DetectAudioSubtype(ManagedAssetRecord asset)
    {
        var name = Path.GetFileNameWithoutExtension(asset.Name);
        var relativePath = asset.RelativePath?.Replace('\\', '/') ?? "";

        // 文件名前缀匹配
        var upperName = name.ToUpperInvariant();

        // 音效
        if (StartsWithAny(upperName, ["SFX_", "SFX", "EFFECT_", "FX_", "UI_", "SE_"]))
            return "音效";
        if (relativePath.Contains("/sfx/") || relativePath.Contains("/se/") ||
            relativePath.Contains("/effect/") || relativePath.Contains("/fx/"))
            return "音效";

        // 歌曲（有人声）
        if (StartsWithAny(upperName, ["VO_", "VOCAL_", "SONG_", "歌_"]))
            return "歌曲";
        if (relativePath.Contains("/vo/") || relativePath.Contains("/vocal/") ||
            relativePath.Contains("/song/") || relativePath.Contains("/voice/"))
            return "歌曲";

        // 纯音乐/BGM
        if (StartsWithAny(upperName, ["BGM_", "MUSIC_", "MELODY_", "LOOP_", "INST_"]))
            return "纯音乐";
        if (relativePath.Contains("/bgm/") || relativePath.Contains("/music/") ||
            relativePath.Contains("/melody/") || relativePath.Contains("/loop/"))
            return "纯音乐";

        // 文件大小和时长启发式：
        // 短文件（<100KB）多为音效
        if (asset.FileSize > 0 && asset.FileSize < 100 * 1024)
            return "音效";

        return null;
    }

    private static string? DetectVideoSubtype(ManagedAssetRecord asset)
    {
        var name = Path.GetFileNameWithoutExtension(asset.Name);
        var relativePath = asset.RelativePath?.Replace('\\', '/') ?? "";
        var upperName = name.ToUpperInvariant();

        // 游戏录制
        if (StartsWithAny(upperName, ["GAME_", "GAMEPLAY_", "REC_", "录屏_"]))
            return "游戏录制";
        if (relativePath.Contains("/game/") || relativePath.Contains("/gameplay/") ||
            relativePath.Contains("/录屏/"))
            return "游戏录制";

        // 动画/CG
        if (StartsWithAny(upperName, ["CG_", "ANIME_", "ANIM_", "SPINE_", "LIVE2D_"]))
            return "动画";
        if (relativePath.Contains("/cg/") || relativePath.Contains("/anime/") ||
            relativePath.Contains("/animation/") || relativePath.Contains("/spine/"))
            return "动画";

        // 短片段（<3MB）多为实拍素材片段
        if (asset.FileSize > 0 && asset.FileSize < 3 * 1024 * 1024)
            return "实拍";

        return null;
    }

    private static bool StartsWithAny(string text, string[] prefixes)
    {
        return prefixes.Any(p => text.StartsWith(p, StringComparison.Ordinal));
    }
}