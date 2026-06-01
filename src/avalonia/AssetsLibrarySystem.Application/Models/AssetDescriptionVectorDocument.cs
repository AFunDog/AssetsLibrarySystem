using System;

namespace AssetsLibrarySystem.Avalonia.Models;

public sealed record AssetDescriptionVectorDocument(
    string AssetId,
    string AssetName,
    string AssetType,
    string AssetPath,
    string Description,
    string DescriptionStorePath,
    string EmbeddingModel,
    int VectorDim,
    float[] Vector,
    DateTimeOffset VectorizedAt);
