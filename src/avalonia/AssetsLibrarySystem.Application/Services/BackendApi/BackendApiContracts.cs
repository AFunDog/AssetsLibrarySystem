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

public sealed record AngleDefinitionDto(
    string Key,
    string Label,
    string Prompt,
    int MaxLength = 120);

public sealed record BackendModelGenerateRequest(
    string AssetFormat,
    string AssetPath,
    string? Prompt,
    string? SystemPrompt,
    bool MockResponse,
    string? Subtype = null,
    AngleDefinitionDto[]? Angles = null,
    bool EnableSlicing = false,
    double SliceThreshold = 60.0,
    int MinSceneLen = 15,
    double AdaptiveThreshold = 3.0);

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
