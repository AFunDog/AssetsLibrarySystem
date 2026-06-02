using System;

namespace AssetsLibrarySystem.Avalonia.Models;

public sealed class AssetSearchDocument
{
    public AssetSearchDocument()
    {
    }

    public AssetSearchDocument(
        string assetId,
        string assetName,
        string assetType,
        string assetPath,
        string description,
        string? sourceStorePath,
        DateTimeOffset? generatedAt,
        float embeddingSimilarity,
        float rerankScore)
    {
        AssetId = assetId;
        AssetName = assetName;
        AssetType = assetType;
        AssetPath = assetPath;
        Description = description;
        SourceStorePath = sourceStorePath;
        GeneratedAt = generatedAt;
        EmbeddingSimilarity = embeddingSimilarity;
        RerankScore = rerankScore;
    }

    public string AssetId { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string AssetPath { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? SourceStorePath { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public float EmbeddingSimilarity { get; set; }
    public float RerankScore { get; set; }
}

public sealed record AssetSearchResponseDocument(
    string Query,
    int CandidateTopK,
    int FinalTopK,
    string? AssetFormat,
    string EmbeddingModel,
    string RerankModel,
    AssetSearchDocument[] Results);

public sealed record AssetReindexResponseDocument(
    int DocumentCount,
    int VectorDim,
    string DatabasePath,
    string IndexPath,
    string MetadataPath,
    string[] EmbeddingModels);

public sealed record AssetSearchWarmupDocument(
    string ModelKind,
    string ModelName,
    string Device,
    bool Warmed);

public sealed record AssetSearchModelStatusDocument(
    string EmbeddingModelName,
    string RerankModelName,
    string Device,
    string[] LoadedModelKinds,
    bool EmbeddingLoaded,
    bool RerankLoaded,
    int LoadedCount);

public sealed record AssetSearchModelCloseDocument(
    string ModelKind,
    string ModelName,
    string Device,
    bool Closed,
    bool CudaCacheCleared,
    string[] RemainingLoadedModels);
