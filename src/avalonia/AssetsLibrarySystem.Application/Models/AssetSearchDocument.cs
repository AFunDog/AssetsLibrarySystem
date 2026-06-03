using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetsLibrarySystem.Avalonia.Models;

public sealed class AssetSearchDocument
{
    public AssetSearchDocument()
    {
    }

    public AssetSearchDocument(
        string assetUid,
        string assetName,
        string assetType,
        string currentPath,
        string description,
        string? sourceStorePath,
        DateTimeOffset? generatedAt,
        float? embeddingSimilarity,
        float? vectorDistance,
        float rerankScore,
        IEnumerable<string>? tags = null)
    {
        AssetUid = assetUid;
        AssetName = assetName;
        AssetType = assetType;
        CurrentPath = currentPath;
        Description = description;
        SourceStorePath = sourceStorePath;
        GeneratedAt = generatedAt;
        EmbeddingSimilarity = embeddingSimilarity;
        VectorDistance = vectorDistance;
        RerankScore = rerankScore;
        Tags = tags?.ToArray() ?? [];
    }

    public string AssetUid { get; set; } = string.Empty;
    public string AssetId
    {
        get => AssetUid;
        set => AssetUid = value;
    }
    public string AssetName { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string CurrentPath { get; set; } = string.Empty;
    public string AssetPath
    {
        get => CurrentPath;
        set => CurrentPath = value;
    }
    public string Description { get; set; } = string.Empty;
    public string? SourceStorePath { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public float? EmbeddingSimilarity { get; set; }
    public float? VectorDistance { get; set; }
    public float RerankScore { get; set; }
    public float? CombinedScore { get; set; }
    public string[] Tags { get; set; } = [];
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
