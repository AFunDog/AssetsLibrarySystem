using System;

namespace AssetsLibrarySystem.Application.Models;

public sealed record AssetDescriptionDocument(
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
    string MetadataStatus)
{
    public string AssetId => AssetUid;
    public string AssetPath => CurrentPath;
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
    object? PromptTokensDetails);
