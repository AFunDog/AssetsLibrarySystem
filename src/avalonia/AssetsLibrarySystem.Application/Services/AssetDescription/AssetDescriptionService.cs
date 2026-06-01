using System;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.Services.AssetDescription;

public sealed class AssetDescriptionService : IAssetDescriptionService
{
    private IAssetDescriptionStore Store { get; }
    private HttpClient Http { get; } = new();
    private JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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

        var storePath = Store.DatabasePath;

        var document = new AssetDescriptionDocument(
            AssetId: asset.Id,
            AssetName: asset.Name,
            AssetType: asset.AssetType,
            AssetPath: asset.LocalPath,
            StorePath: storePath,
            Description: backendResponse.OutputText,
            BackendEndpoint: backendBaseUrl,
            Mode: backendResponse.Mode,
            GeneratedAt: DateTimeOffset.UtcNow,
            TokenUsage: MapTokenUsage(backendResponse.TokenUsage),
            Prompt: request.Prompt,
            SystemPrompt: request.SystemPrompt);

        await Store.SaveAsync(document, ct);

        Log.Information("素材描述已写入 SQLite: {DatabasePath}", storePath);
        return document;
    }

    private sealed record AssetDescriptionRequest(
        [property: JsonPropertyName("asset_format")] string AssetType,
        [property: JsonPropertyName("asset_path")] string AssetPath,
        [property: JsonPropertyName("prompt")] string? Prompt,
        [property: JsonPropertyName("system_prompt")] string? SystemPrompt,
        [property: JsonPropertyName("mock_response")] bool MockResponse);

    private sealed record AssetDescriptionBackendResponse(
        [property: JsonPropertyName("provider_slot")] string ProviderSlot,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("mode")] string Mode,
        [property: JsonPropertyName("output_text")] string OutputText,
        [property: JsonPropertyName("system_prompt")] string SystemPrompt,
        [property: JsonPropertyName("token_usage")] AssetDescriptionBackendTokenUsage? TokenUsage);

    private static AssetDescriptionTokenUsage? MapTokenUsage(AssetDescriptionBackendTokenUsage? usage)
    {
        if (usage is null)
        {
            return null;
        }

        return new AssetDescriptionTokenUsage(
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

    private sealed record AssetDescriptionBackendTokenUsage(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens,
        [property: JsonPropertyName("total_tokens")] int TotalTokens,
        [property: JsonPropertyName("image_tokens")] int? ImageTokens,
        [property: JsonPropertyName("video_tokens")] int? VideoTokens,
        [property: JsonPropertyName("audio_tokens")] int? AudioTokens,
        [property: JsonPropertyName("input_tokens_details")] JsonElement? InputTokensDetails,
        [property: JsonPropertyName("output_tokens_details")] JsonElement? OutputTokensDetails,
        [property: JsonPropertyName("prompt_tokens_details")] JsonElement? PromptTokensDetails);
}
