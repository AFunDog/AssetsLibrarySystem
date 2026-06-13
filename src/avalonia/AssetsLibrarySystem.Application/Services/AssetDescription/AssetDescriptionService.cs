using System;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.BackendApi;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public sealed class AssetDescriptionService : IAssetDescriptionService
{
    private IAssetDescriptionStore Store { get; }
    private IBackendModelClient BackendModelClient { get; }

    public AssetDescriptionService(IAssetDescriptionStore store, IBackendModelClient backendModelClient)
    {
        Store = store;
        BackendModelClient = backendModelClient;
    }

    public async Task<AssetDescriptionDocument> DescribeAsync(
        ManagedAssetRecord asset,
        string backendBaseUrl,
        string? prompt,
        string? systemPrompt,
        CancellationToken ct = default)
    {
        var request = new BackendModelGenerateRequest(
            AssetFormat: asset.AssetType,
            AssetPath: asset.LocalPath,
            Prompt: string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim(),
            SystemPrompt: string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt.Trim(),
            MockResponse: false);

        var backendResponse = await BackendModelClient.GenerateAsync(backendBaseUrl, request, ct).ConfigureAwait(false);

        var document = new AssetDescriptionDocument(
            AssetId: asset.DatabaseId,
            AssetUid: asset.AssetUid,
            AssetName: asset.Name,
            AssetType: asset.AssetType,
            CurrentPath: asset.CurrentPath,
            Description: backendResponse.OutputText,
            BackendEndpoint: backendBaseUrl,
            Mode: backendResponse.Mode,
            GeneratedAt: DateTimeOffset.UtcNow,
            TokenUsage: MapTokenUsage(backendResponse.TokenUsage),
            Prompt: request.Prompt,
            SystemPrompt: request.SystemPrompt,
            ContentHash: asset.ContentHash,
            MetadataStatus: asset.MetadataStatus);

        await Store.SaveAsync(document, ct);

        Log.Information("素材描述已写入 SQLite: {DatabasePath}", Store.DatabasePath);
        return document;
    }

    private static AssetDescriptionTokenUsage? MapTokenUsage(BackendTokenUsage? usage) =>
        usage is null ? null : new AssetDescriptionTokenUsage(
            usage.InputTokens,
            usage.OutputTokens,
            usage.TotalTokens,
            usage.ImageTokens,
            usage.VideoTokens,
            usage.AudioTokens,
            usage.InputTokensDetails,
            usage.OutputTokensDetails,
            usage.PromptTokensDetails);
}
