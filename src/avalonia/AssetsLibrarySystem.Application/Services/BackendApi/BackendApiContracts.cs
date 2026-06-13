using System;
using System.Text.Json;

namespace AssetsLibrarySystem.Application.Services.BackendApi;

public sealed record BackendSearchIndexRequest(
    string Provider,
    string Model,
    int? EmbeddingDimensions,
    string AssetId,
    string AssetName,
    string AssetFormat,
    string AssetPath,
    string Description,
    DateTimeOffset? GeneratedAt);

public sealed record BackendSearchIndexResponse(
    string AssetId,
    string AssetName,
    string AssetFormat,
    string AssetPath,
    string Description,
    JsonElement Vector,
    int VectorDim,
    string EmbeddingModel,
    int? TokenUsage);

public sealed record BackendSearchQueryRequest(
    string Provider,
    string Model,
    string Query,
    BackendSearchQueryCandidate[] Candidates,
    int FinalTopK);

public sealed record BackendSearchQueryCandidate(
    string CandidateId,
    string AssetId,
    string AssetName,
    string AssetFormat,
    string AssetPath,
    string Description,
    string[] Tags,
    DateTimeOffset? GeneratedAt);

public sealed record BackendSearchQueryResponse(
    string Query,
    int FinalTopK,
    string RerankModel,
    BackendSearchQueryResult[] Results,
    int? TokenUsage);

public sealed record BackendSearchQueryResult(string? CandidateId, float RerankScore);

public sealed record BackendModelGenerateRequest(
    string AssetFormat,
    string AssetPath,
    string? Prompt,
    string? SystemPrompt,
    bool MockResponse);

public sealed record BackendModelGenerateResponse(
    string ProviderSlot,
    string Provider,
    string Model,
    string Mode,
    string OutputText,
    string SystemPrompt,
    BackendTokenUsage? TokenUsage);

public sealed record BackendTokenUsage(
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    int? ImageTokens,
    int? VideoTokens,
    int? AudioTokens,
    JsonElement? InputTokensDetails,
    JsonElement? OutputTokensDetails,
    JsonElement? PromptTokensDetails);

public sealed record BackendModelWarmupResponse(string ModelKind, string ModelName, string Device, bool Warmed);

public sealed record BackendModelStatusResponse(
    string EmbeddingModelName,
    string RerankModelName,
    string Device,
    string[] LoadedModelKinds,
    bool EmbeddingLoaded,
    bool RerankLoaded,
    int LoadedCount);

public sealed record BackendModelCloseRequest(string ModelKind);

public sealed record BackendModelCloseResponse(
    string ModelKind,
    string ModelName,
    string Device,
    bool Closed,
    bool CudaCacheCleared,
    string[] RemainingLoadedModels);
