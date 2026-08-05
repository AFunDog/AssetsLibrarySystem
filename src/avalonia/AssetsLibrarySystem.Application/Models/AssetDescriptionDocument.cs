using System;

namespace AssetsLibrarySystem.Application.Models;

public sealed record AssetDescriptionDocument(
    long AssetId,
    string AssetUid,
    string AssetName,
    string AssetType,
    string CurrentPath,
    string Description,
    string BackendEndpoint,
    string Mode,
    DateTimeOffset GeneratedAt,
    AssetDescriptionTokenUsage? TokenUsage,
    string? Prompt,
    string? SystemPrompt,
    string? ContentHash,
    string MetadataStatus,
    string? Subtype = null)
{
    public string AssetPath => CurrentPath;
    public string PrimaryDescription => StructuredDescriptionHelper.ExtractPrimaryText(Description);
}

public sealed record AssetDescriptionTokenUsage(
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    int? ImageTokens,
    int? VideoTokens,
    int? AudioTokens,
    object? InputTokensDetails,
    object? OutputTokensDetails,
    object? PromptTokensDetails,
    double? EstimatedCostCny = null);
