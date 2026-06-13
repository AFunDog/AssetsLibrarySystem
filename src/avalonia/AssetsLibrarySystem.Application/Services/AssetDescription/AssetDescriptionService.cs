using System;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public sealed class AssetDescriptionService : IAssetDescriptionService
{
    private IAssetDescriptionStore Store { get; }
    private HttpClient Http { get; } = new();
    private JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public AssetDescriptionService(IAssetDescriptionStore store)
    {
        Store = store;
    }

    public async Task<AssetDescriptionDocument> DescribeAsync(
        ManagedAssetRecord asset,
        string backendBaseUrl,
        string? prompt,
        string? systemPrompt,
        CancellationToken ct = default)
    {
        var request = new AssetDescriptionRequest(
            AssetType: asset.AssetType,
            AssetPath: asset.LocalPath,
            Prompt: string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim(),
            SystemPrompt: string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt.Trim(),
            MockResponse: false);

        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/model/generate";
        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await Http.PostAsync(endpoint, content, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("素材描述请求失败: {StatusCode}, body={Body}", (int)response.StatusCode, responseText);
            throw new InvalidOperationException($"后端描述失败：{responseText}");
        }

        var backendResponse = JsonSerializer.Deserialize<AssetDescriptionBackendResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空响应。");

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

    private sealed record AssetDescriptionRequest(
        string AssetType,
        string AssetPath,
        string? Prompt,
        string? SystemPrompt,
        bool MockResponse);

    private sealed record AssetDescriptionBackendResponse(
        string ProviderSlot,
        string Provider,
        string Model,
        string Mode,
        string OutputText,
        string SystemPrompt,
        AssetDescriptionBackendTokenUsage? TokenUsage);

    private static AssetDescriptionTokenUsage? MapTokenUsage(AssetDescriptionBackendTokenUsage? usage) =>
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

    private sealed record AssetDescriptionBackendTokenUsage(
        int InputTokens,
        int OutputTokens,
        int TotalTokens,
        int? ImageTokens,
        int? VideoTokens,
        int? AudioTokens,
        JsonElement? InputTokensDetails,
        JsonElement? OutputTokensDetails,
        JsonElement? PromptTokensDetails);
}
