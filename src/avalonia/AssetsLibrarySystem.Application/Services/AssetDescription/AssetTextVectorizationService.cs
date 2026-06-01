using System;
using System.Linq;
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

public sealed class AssetTextVectorizationService : IAssetTextVectorizationService
{
    private HttpClient Http { get; } = new();
    private JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<AssetDescriptionVectorDocument> VectorizeAsync(
        AssetDescriptionDocument document,
        string backendBaseUrl,
        CancellationToken ct = default)
    {
        var request = new SearchIndexRequest(
            AssetId: document.AssetId,
            AssetName: document.AssetName,
            AssetType: document.AssetType,
            AssetPath: document.AssetPath,
            Description: document.Description,
            SourceStorePath: document.StorePath,
            GeneratedAt: document.GeneratedAt);

        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/index";
        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await Http.PostAsync(endpoint, content, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("素材向量化请求失败: {StatusCode}, body={Body}", (int)response.StatusCode, responseText);
            throw new InvalidOperationException($"后端向量化失败：{responseText}");
        }

        var vectorResponse = JsonSerializer.Deserialize<SearchIndexResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空向量响应。");

        return new AssetDescriptionVectorDocument(
            AssetId: vectorResponse.AssetId,
            AssetName: vectorResponse.AssetName,
            AssetType: vectorResponse.AssetFormat,
            AssetPath: vectorResponse.AssetPath,
            Description: vectorResponse.Description,
            DescriptionStorePath: document.StorePath,
            EmbeddingModel: vectorResponse.EmbeddingModel,
            VectorDim: vectorResponse.VectorDim,
            Vector: JsonSerializer.Deserialize<float[]>(vectorResponse.Vector.GetRawText(), JsonOptions) ?? [],
            VectorizedAt: DateTimeOffset.UtcNow);
    }

    private sealed record SearchIndexRequest(
        [property: JsonPropertyName("asset_id")] string AssetId,
        [property: JsonPropertyName("asset_name")] string AssetName,
        [property: JsonPropertyName("asset_format")] string AssetType,
        [property: JsonPropertyName("asset_path")] string AssetPath,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("source_store_path")] string? SourceStorePath,
        [property: JsonPropertyName("generated_at")] DateTimeOffset? GeneratedAt);

    private sealed record SearchIndexResponse(
        [property: JsonPropertyName("asset_id")] string AssetId,
        [property: JsonPropertyName("asset_name")] string AssetName,
        [property: JsonPropertyName("asset_format")] string AssetFormat,
        [property: JsonPropertyName("asset_path")] string AssetPath,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("vector")] JsonElement Vector,
        [property: JsonPropertyName("vector_dim")] int VectorDim,
        [property: JsonPropertyName("embedding_model")] string EmbeddingModel);
}
