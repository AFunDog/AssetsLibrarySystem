using System;

namespace AssetsLibrarySystem.Application.Models;

public sealed record AssetDescriptionVectorDocument(
    string AssetUid,
    string DescriptionStorePath,
    string EmbeddingModel,
    int VectorDim,
    float[] Vector,
    DateTimeOffset VectorizedAt,
    string? ContentHash)
{
    public string AssetId => AssetUid;
}
