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
    string? ContentHash)
{
    public const string DefaultAngleType = "全面";
}
