using System;

namespace AssetsLibrarySystem.Avalonia.Models;

public sealed record AssetSearchDocument(
    string AssetId,
    string AssetName,
    string AssetType,
    string AssetPath,
    string Description,
    string? SourceStorePath,
    DateTimeOffset? GeneratedAt,
    float EmbeddingSimilarity,
    float RerankScore);

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
