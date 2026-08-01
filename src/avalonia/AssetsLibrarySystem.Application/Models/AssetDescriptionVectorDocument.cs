using System;

namespace AssetsLibrarySystem.Application.Models;

public sealed record AssetDescriptionVectorDocument(
    long AssetId,
    string AssetUid,
    string AngleType,
    string EmbeddingModel,
    int VectorDim,
    float[] Vector,
    DateTimeOffset VectorizedAt,
    string? ContentHash,
    string? SourceFingerprint = null)
{
    /// <summary>当前主角度键（与 angle_profiles.yaml 一致）。</summary>
    public const string DefaultAngleType = "整体";

    /// <summary>历史主角度别名，读取时兼容旧数据。</summary>
    public const string LegacyPrimaryAngleType = "全面";

    public static bool IsPrimaryAngleType(string? angleType)
    {
        if (string.IsNullOrWhiteSpace(angleType))
        {
            return false;
        }

        var trimmed = angleType.Trim();
        return string.Equals(trimmed, DefaultAngleType, StringComparison.Ordinal)
            || string.Equals(trimmed, LegacyPrimaryAngleType, StringComparison.Ordinal);
    }
}
