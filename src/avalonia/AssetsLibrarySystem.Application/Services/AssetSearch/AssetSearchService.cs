using System;
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

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

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
        var startedAt = DateTimeOffset.UtcNow;
        var request = new SearchExploreRequest(
            Query: query,
            CandidateTopK: candidateTopK,
            FinalTopK: finalTopK,
            AssetFormat: string.IsNullOrWhiteSpace(assetFormat) ? null : assetFormat.Trim());

        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/explore";
        Log.Information(
            "发起素材检索请求: endpoint={Endpoint}, queryLength={QueryLength}, candidateTopK={CandidateTopK}, finalTopK={FinalTopK}, assetFormat={AssetFormat}, queryPreview={QueryPreview}",
            endpoint,
            query.Length,
            candidateTopK,
            finalTopK,
            request.AssetFormat ?? "全部",
            BuildPreview(query));
        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await Http.PostAsync(endpoint, content, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning(
                "素材搜索请求失败: endpoint={Endpoint}, statusCode={StatusCode}, elapsedMs={ElapsedMs}, body={Body}",
                endpoint,
                (int)response.StatusCode,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                responseText);
            throw new InvalidOperationException($"后端搜索失败：{responseText}");
        }

        var backendResponse = JsonSerializer.Deserialize<SearchExploreResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空搜索响应。");

        Log.Information(
            "素材搜索响应完成: endpoint={Endpoint}, elapsedMs={ElapsedMs}, returned={ReturnedCount}, embeddingModel={EmbeddingModel}, rerankModel={RerankModel}",
            endpoint,
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            backendResponse.Results.Length,
            backendResponse.EmbeddingModel,
            backendResponse.RerankModel);

        return new AssetSearchResponseDocument(
            Query: backendResponse.Query,
            CandidateTopK: backendResponse.CandidateTopK,
            FinalTopK: backendResponse.FinalTopK,
            AssetFormat: backendResponse.AssetFormat,
            EmbeddingModel: backendResponse.EmbeddingModel,
            RerankModel: backendResponse.RerankModel,
            Results: backendResponse.Results.Select(item =>
                new AssetSearchDocument
                {
                    AssetUid = item.AssetId,
                    AssetName = item.AssetName,
                    AssetType = item.AssetFormat,
                    CurrentPath = item.AssetPath,
                    Description = item.Description,
                    Tags = item.Tags,
                    GeneratedAt = item.GeneratedAt,
                    EmbeddingSimilarity = item.EmbeddingSimilarity,
                    VectorDistance = item.VectorDistance,
                    RerankScore = item.RerankScore,
                    CombinedScore = item.CombinedScore,
                }).ToArray());
    }

    public async Task<AssetReindexResponseDocument> ReindexAsync(
        string backendBaseUrl,
        CancellationToken ct = default)
    {
        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/reindex";
        Log.Information("发起向量索引重建请求: endpoint={Endpoint}", endpoint);
        using var content = new StringContent(
            "{}",
            Encoding.UTF8,
            "application/json");

        using var response = await Http.PostAsync(endpoint, content, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning(
                "素材索引重建请求失败: endpoint={Endpoint}, statusCode={StatusCode}, body={Body}",
                endpoint,
                (int)response.StatusCode,
                responseText);
            throw new InvalidOperationException($"后端索引重建失败：{responseText}");
        }

        var backendResponse = JsonSerializer.Deserialize<SearchReindexResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空重建响应。");

        Log.Information(
            "向量索引重建完成: endpoint={Endpoint}, documentCount={DocumentCount}, vectorDim={VectorDim}, databasePath={DatabasePath}, indexPath={IndexPath}",
            endpoint,
            backendResponse.DocumentCount,
            backendResponse.VectorDim,
            backendResponse.DatabasePath,
            backendResponse.IndexPath);

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

    public async Task<AssetSearchModelStatusDocument> GetModelStatusAsync(
        string backendBaseUrl,
        CancellationToken ct = default)
    {
        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/models/status";
        Log.Debug("查询模型状态: endpoint={Endpoint}", endpoint);
        using var response = await Http.GetAsync(endpoint, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning(
                "模型状态查询失败: endpoint={Endpoint}, statusCode={StatusCode}, body={Body}",
                endpoint,
                (int)response.StatusCode,
                responseText);
            throw new InvalidOperationException($"后端模型状态查询失败：{responseText}");
        }

        var backendResponse = JsonSerializer.Deserialize<SearchModelStatusResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空模型状态响应。");

        Log.Debug(
            "模型状态查询完成: endpoint={Endpoint}, loadedCount={LoadedCount}, device={Device}, embeddingLoaded={EmbeddingLoaded}, rerankLoaded={RerankLoaded}",
            endpoint,
            backendResponse.LoadedCount,
            backendResponse.Device,
            backendResponse.EmbeddingLoaded,
            backendResponse.RerankLoaded);

        return new AssetSearchModelStatusDocument(
            EmbeddingModelName: backendResponse.EmbeddingModelName,
            RerankModelName: backendResponse.RerankModelName,
            Device: backendResponse.Device,
            LoadedModelKinds: backendResponse.LoadedModelKinds.ToArray(),
            EmbeddingLoaded: backendResponse.EmbeddingLoaded,
            RerankLoaded: backendResponse.RerankLoaded,
            LoadedCount: backendResponse.LoadedCount);
    }

    public async Task<AssetSearchModelCloseDocument> CloseModelAsync(
        string backendBaseUrl,
        string modelKind,
        CancellationToken ct = default)
    {
        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/models/close";
        var request = new SearchModelCloseRequest(modelKind);
        Log.Information("请求关闭本地搜索模型: endpoint={Endpoint}, modelKind={ModelKind}", endpoint, modelKind);
        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await Http.PostAsync(endpoint, content, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning(
                "模型关闭请求失败: endpoint={Endpoint}, statusCode={StatusCode}, body={Body}",
                endpoint,
                (int)response.StatusCode,
                responseText);
            throw new InvalidOperationException($"后端模型关闭失败：{responseText}");
        }

        var backendResponse = JsonSerializer.Deserialize<SearchModelCloseResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空模型关闭响应。");

        Log.Information(
            "本地搜索模型关闭完成: endpoint={Endpoint}, modelKind={ModelKind}, closed={Closed}, remainingLoadedModels={RemainingLoadedModels}",
            endpoint,
            backendResponse.ModelKind,
            backendResponse.Closed,
            string.Join(", ", backendResponse.RemainingLoadedModels));

        return new AssetSearchModelCloseDocument(
            ModelKind: backendResponse.ModelKind,
            ModelName: backendResponse.ModelName,
            Device: backendResponse.Device,
            Closed: backendResponse.Closed,
            CudaCacheCleared: backendResponse.CudaCacheCleared,
            RemainingLoadedModels: backendResponse.RemainingLoadedModels.ToArray());
    }

    private async Task<AssetSearchWarmupDocument> WarmupAsync(
        string backendBaseUrl,
        string modelKind,
        CancellationToken ct)
    {
        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/warmup/{modelKind}";
        Log.Information("请求模型预热: endpoint={Endpoint}, modelKind={ModelKind}", endpoint, modelKind);
        using var content = new StringContent(
            "{}",
            Encoding.UTF8,
            "application/json");

        using var response = await Http.PostAsync(endpoint, content, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning(
                "素材 {ModelKind} 模型预热失败: endpoint={Endpoint}, statusCode={StatusCode}, body={Body}",
                modelKind,
                endpoint,
                (int)response.StatusCode,
                responseText);
            throw new InvalidOperationException($"后端{modelKind}模型预热失败：{responseText}");
        }

        var backendResponse = JsonSerializer.Deserialize<SearchWarmupResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空模型预热响应。");

        Log.Information(
            "模型预热完成: endpoint={Endpoint}, modelKind={ModelKind}, modelName={ModelName}, device={Device}, warmed={Warmed}",
            endpoint,
            backendResponse.ModelKind,
            backendResponse.ModelName,
            backendResponse.Device,
            backendResponse.Warmed);

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
        [property: JsonPropertyName("tags")] string[] Tags,
        [property: JsonPropertyName("generated_at")] DateTimeOffset? GeneratedAt,
        [property: JsonPropertyName("embedding_similarity")] float? EmbeddingSimilarity,
        [property: JsonPropertyName("vector_distance")] float? VectorDistance,
        [property: JsonPropertyName("rerank_score")] float RerankScore,
        [property: JsonPropertyName("combined_score")] float? CombinedScore);

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

    private sealed record SearchModelStatusResponse(
        [property: JsonPropertyName("embedding_model_name")] string EmbeddingModelName,
        [property: JsonPropertyName("rerank_model_name")] string RerankModelName,
        [property: JsonPropertyName("device")] string Device,
        [property: JsonPropertyName("loaded_model_kinds")] string[] LoadedModelKinds,
        [property: JsonPropertyName("embedding_loaded")] bool EmbeddingLoaded,
        [property: JsonPropertyName("rerank_loaded")] bool RerankLoaded,
        [property: JsonPropertyName("loaded_count")] int LoadedCount);

    private sealed record SearchModelCloseRequest(
        [property: JsonPropertyName("model_kind")] string ModelKind);

    private sealed record SearchModelCloseResponse(
        [property: JsonPropertyName("model_kind")] string ModelKind,
        [property: JsonPropertyName("model_name")] string ModelName,
        [property: JsonPropertyName("device")] string Device,
        [property: JsonPropertyName("closed")] bool Closed,
        [property: JsonPropertyName("cuda_cache_cleared")] bool CudaCacheCleared,
        [property: JsonPropertyName("remaining_loaded_models")] string[] RemainingLoadedModels);

    private static string BuildPreview(string query)
    {
        var trimmed = query.Trim();
        return trimmed.Length <= 48 ? trimmed : $"{trimmed[..48]}…";
    }
}
