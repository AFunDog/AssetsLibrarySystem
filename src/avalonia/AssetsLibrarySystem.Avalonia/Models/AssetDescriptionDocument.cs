using System;

namespace AssetsLibrarySystem.Avalonia.Models;

public sealed record AssetDescriptionDocument(
    string AssetId,
    string AssetName,
    string AssetType,
    string AssetPath,
    string StorePath,
    string Description,
    string BackendEndpoint,
    string Mode,
    DateTimeOffset GeneratedAt,
    AssetDescriptionTokenUsage? TokenUsage,
    string? Prompt,
    string? SystemPrompt);

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
