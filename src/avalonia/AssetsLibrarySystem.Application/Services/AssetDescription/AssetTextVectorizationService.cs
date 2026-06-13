using System;
using System.Collections.Generic;
using System.Linq;
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

public sealed class AssetTextVectorizationService : IAssetTextVectorizationService
{
    private HttpClient Http { get; } = new();
    private JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<IReadOnlyList<AssetDescriptionVectorDocument>> VectorizeAsync(
        AssetDescriptionDocument document,
        string backendBaseUrl,
        string provider,
        string model,
        CancellationToken ct = default)
    {
        var segments = StructuredDescriptionHelper.ExtractSegments(document.Description);
        var vectorDocuments = new List<AssetDescriptionVectorDocument>(segments.Count);
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment.NormalizedText))
            {
                continue;
            }

            var request = new SearchIndexRequest(
                Provider: provider,
                Model: model,
                AssetId: document.AssetUid,
                AssetName: document.AssetName,
                AssetType: document.AssetType,
                AssetPath: document.CurrentPath,
                Description: segment.NormalizedText,
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

            vectorDocuments.Add(new AssetDescriptionVectorDocument(
                AssetId: document.AssetId,
                AssetUid: vectorResponse.AssetId,
                AngleType: segment.NormalizedAngleType,
                EmbeddingModel: vectorResponse.EmbeddingModel,
                VectorDim: vectorResponse.VectorDim,
                Vector: JsonSerializer.Deserialize<float[]>(vectorResponse.Vector.GetRawText(), JsonOptions) ?? [],
                VectorizedAt: DateTimeOffset.UtcNow,
                ContentHash: document.ContentHash));
        }

        if (vectorDocuments.Count == 0)
        {
            throw new InvalidOperationException("当前描述中没有可向量化的有效角度文本。");
        }

        return vectorDocuments;
    }

    private sealed record SearchIndexRequest(
        string Provider,
        string Model,
        string AssetId,
        string AssetName,
        string AssetType,
        string AssetPath,
        string Description,
        DateTimeOffset? GeneratedAt);

    private sealed record SearchIndexResponse(
        string AssetId,
        string AssetName,
        string AssetFormat,
        string AssetPath,
        string Description,
        JsonElement Vector,
        int VectorDim,
        string EmbeddingModel);
}
