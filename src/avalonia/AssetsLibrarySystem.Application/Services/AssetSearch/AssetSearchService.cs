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

namespace AssetsLibrarySystem.Avalonia.Services.AssetSearch;

public sealed class AssetSearchService : IAssetSearchService
{
    private HttpClient Http { get; } = new();
    private JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<AssetSearchResponseDocument> SearchAsync(
        string backendBaseUrl,
        string query,
        int candidateTopK = 20,
        int finalTopK = 5,
        string? assetFormat = null,
        CancellationToken ct = default)
    {
        var request = new SearchExploreRequest(
            Query: query,
            CandidateTopK: candidateTopK,
            FinalTopK: finalTopK,
            AssetFormat: string.IsNullOrWhiteSpace(assetFormat) ? null : assetFormat.Trim());

        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/explore";
        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await Http.PostAsync(endpoint, content, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("素材搜索请求失败: {StatusCode}, body={Body}", (int)response.StatusCode, responseText);
            throw new InvalidOperationException($"后端搜索失败：{responseText}");
        }

        var backendResponse = JsonSerializer.Deserialize<SearchExploreResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空搜索响应。");

        return new AssetSearchResponseDocument(
            Query: backendResponse.Query,
            CandidateTopK: backendResponse.CandidateTopK,
            FinalTopK: backendResponse.FinalTopK,
            AssetFormat: backendResponse.AssetFormat,
            EmbeddingModel: backendResponse.EmbeddingModel,
            RerankModel: backendResponse.RerankModel,
            Results: backendResponse.Results.Select(item =>
                new AssetSearchDocument(
                    AssetId: item.AssetId,
                    AssetName: item.AssetName,
                    AssetType: item.AssetFormat,
                    AssetPath: item.AssetPath,
                    Description: item.Description,
                    SourceStorePath: item.SourceStorePath,
                    GeneratedAt: item.GeneratedAt,
                    EmbeddingSimilarity: item.EmbeddingSimilarity,
                    RerankScore: item.RerankScore)).ToArray());
    }

    public async Task<AssetReindexResponseDocument> ReindexAsync(
        string backendBaseUrl,
        CancellationToken ct = default)
    {
        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/reindex";
        using var content = new StringContent(
            "{}",
            Encoding.UTF8,
            "application/json");

        using var response = await Http.PostAsync(endpoint, content, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("素材索引重建请求失败: {StatusCode}, body={Body}", (int)response.StatusCode, responseText);
            throw new InvalidOperationException($"后端索引重建失败：{responseText}");
        }

        var backendResponse = JsonSerializer.Deserialize<SearchReindexResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空重建响应。");

        return new AssetReindexResponseDocument(
            DocumentCount: backendResponse.DocumentCount,
            VectorDim: backendResponse.VectorDim,
            DatabasePath: backendResponse.DatabasePath,
            IndexPath: backendResponse.IndexPath,
            MetadataPath: backendResponse.MetadataPath,
            EmbeddingModels: backendResponse.EmbeddingModels.ToArray());
    }

    public async Task<AssetSearchWarmupDocument> WarmupEmbeddingAsync(
        string backendBaseUrl,
        CancellationToken ct = default)
    {
        return await WarmupAsync(backendBaseUrl, "embedding", ct);
    }

    public async Task<AssetSearchWarmupDocument> WarmupRerankAsync(
        string backendBaseUrl,
        CancellationToken ct = default)
    {
        return await WarmupAsync(backendBaseUrl, "rerank", ct);
    }

    private async Task<AssetSearchWarmupDocument> WarmupAsync(
        string backendBaseUrl,
        string modelKind,
        CancellationToken ct)
    {
        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/warmup/{modelKind}";
        using var content = new StringContent(
            "{}",
            Encoding.UTF8,
            "application/json");

        using var response = await Http.PostAsync(endpoint, content, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("素材 {ModelKind} 模型预热失败: {StatusCode}, body={Body}", modelKind, (int)response.StatusCode, responseText);
            throw new InvalidOperationException($"后端{modelKind}模型预热失败：{responseText}");
        }

        var backendResponse = JsonSerializer.Deserialize<SearchWarmupResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空模型预热响应。");

        return new AssetSearchWarmupDocument(
            ModelKind: backendResponse.ModelKind,
            ModelName: backendResponse.ModelName,
            Device: backendResponse.Device,
            Warmed: backendResponse.Warmed);
    }

    private sealed record SearchExploreRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("candidate_top_k")] int CandidateTopK,
        [property: JsonPropertyName("final_top_k")] int FinalTopK,
        [property: JsonPropertyName("asset_format")] string? AssetFormat);

    private sealed record SearchExploreResponse(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("candidate_top_k")] int CandidateTopK,
        [property: JsonPropertyName("final_top_k")] int FinalTopK,
        [property: JsonPropertyName("asset_format")] string? AssetFormat,
        [property: JsonPropertyName("embedding_model")] string EmbeddingModel,
        [property: JsonPropertyName("rerank_model")] string RerankModel,
        [property: JsonPropertyName("results")] SearchExploreResult[] Results);

    private sealed record SearchExploreResult(
        [property: JsonPropertyName("asset_id")] string AssetId,
        [property: JsonPropertyName("asset_name")] string AssetName,
        [property: JsonPropertyName("asset_format")] string AssetFormat,
        [property: JsonPropertyName("asset_path")] string AssetPath,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("source_store_path")] string? SourceStorePath,
        [property: JsonPropertyName("generated_at")] DateTimeOffset? GeneratedAt,
        [property: JsonPropertyName("embedding_similarity")] float EmbeddingSimilarity,
        [property: JsonPropertyName("rerank_score")] float RerankScore);

    private sealed record SearchReindexResponse(
        [property: JsonPropertyName("document_count")] int DocumentCount,
        [property: JsonPropertyName("vector_dim")] int VectorDim,
        [property: JsonPropertyName("database_path")] string DatabasePath,
        [property: JsonPropertyName("index_path")] string IndexPath,
        [property: JsonPropertyName("metadata_path")] string MetadataPath,
        [property: JsonPropertyName("embedding_models")] string[] EmbeddingModels);

    private sealed record SearchWarmupResponse(
        [property: JsonPropertyName("model_kind")] string ModelKind,
        [property: JsonPropertyName("model_name")] string ModelName,
        [property: JsonPropertyName("device")] string Device,
        [property: JsonPropertyName("warmed")] bool Warmed);
}
