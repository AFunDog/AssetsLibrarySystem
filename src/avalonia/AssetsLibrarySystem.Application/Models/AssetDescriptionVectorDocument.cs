using System;

namespace AssetsLibrarySystem.Avalonia.Models;

public sealed record AssetDescriptionVectorDocument(
    string AssetUid,
    string AssetName,
    string AssetType,
    string CurrentPath,
    string Description,
    string DescriptionStorePath,
    string EmbeddingModel,
    int VectorDim,
    float[] Vector,
    DateTimeOffset VectorizedAt,
    string? ContentHash)
{
    public string AssetId => AssetUid;
    public string AssetPath => CurrentPath;
}
