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
using AssetsLibrarySystem.Application.Services.Infrastructure;
using Microsoft.Data.Sqlite;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

public sealed class AssetSearchService : IAssetSearchService
{
    private HttpClient Http { get; } = new();
    private IAssetDatabase AssetDatabase { get; }
    private LocalHnswSearchIndexManager IndexManager { get; } = new();
    private JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public AssetSearchService(IAssetDatabase assetDatabase)
    {
        AssetDatabase = assetDatabase;
    }

    public async Task<AssetSearchResponseDocument> SearchAsync(
        string backendBaseUrl,
        string query,
        int candidateTopK = 20,
        int finalTopK = 5,
        string? assetFormat = null,
        CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var normalizedQuery = query.Trim();
        var normalizedAssetFormat = string.IsNullOrWhiteSpace(assetFormat) ? null : assetFormat.Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            throw new InvalidOperationException("搜索词不能为空。");
        }

        var records = await LoadVectorRecordsAsync(ct).ConfigureAwait(false);
        if (records.Count == 0)
        {
            throw new InvalidOperationException("当前没有可检索的素材描述。");
        }

        var queryVectorResponse = await IndexTextAsync(backendBaseUrl, normalizedQuery, ct).ConfigureAwait(false);
        var queryVector = JsonSerializer.Deserialize<float[]>(queryVectorResponse.Vector.GetRawText(), JsonOptions) ?? [];
        if (queryVector.Length == 0)
        {
            throw new InvalidOperationException("后端返回的查询向量为空。");
        }

        var filteredRecords = records
            .Where(record => normalizedAssetFormat is null || string.Equals(record.AssetType, normalizedAssetFormat, StringComparison.Ordinal))
            .ToList();
        if (filteredRecords.Count == 0)
        {
            throw new InvalidOperationException("未找到符合条件的素材。");
        }

        var state = BuildIndexState(records);
        IndexManager.EnsureCurrent(records.Select(record => record.Vector).ToArray(), state);

        var expandedCandidateTopK = Math.Min(
            records.Count,
            Math.Max(candidateTopK * 8, Math.Max(candidateTopK, 50)));

        var searchResults = IndexManager.Search(queryVector, expandedCandidateTopK);
        var vectorCandidates = new List<VectorCandidateRecord>(searchResults.Count);
        foreach (var (index, similarity) in searchResults)
        {
            if (index < 0 || index >= records.Count)
            {
                continue;
            }

            var record = records[index];
            if (normalizedAssetFormat is not null && !string.Equals(record.AssetType, normalizedAssetFormat, StringComparison.Ordinal))
            {
                continue;
            }

            vectorCandidates.Add(new VectorCandidateRecord(
                CandidateId: $"{record.AssetUid}::{record.AngleType}",
                Record: record,
                EmbeddingSimilarity: similarity,
                VectorDistance: Math.Max(0f, 1f - similarity)));
            if (vectorCandidates.Count >= candidateTopK * 8)
            {
                break;
            }
        }

        if (vectorCandidates.Count == 0)
        {
            throw new InvalidOperationException("未找到符合条件的素材。");
        }

        var rerankResponse = await RerankAsync(backendBaseUrl, normalizedQuery, vectorCandidates, finalTopK, ct).ConfigureAwait(false);
        var rerankScoreMap = rerankResponse.Results
            .Where(item => !string.IsNullOrWhiteSpace(item.CandidateId))
            .ToDictionary(item => item.CandidateId!, item => item.RerankScore, StringComparer.Ordinal);

        var rerankScores = vectorCandidates
            .Select(candidate => rerankScoreMap.GetValueOrDefault(candidate.CandidateId, 0f))
            .ToArray();
        var normalizedRerankScores = NormalizeScores(rerankScores);

        var scoredCandidates = vectorCandidates
            .Select((candidate, index) => new ScoredVectorCandidateRecord(
                candidate.CandidateId,
                candidate.Record,
                candidate.EmbeddingSimilarity,
                candidate.VectorDistance,
                rerankScores[index],
                normalizedRerankScores[index],
                CombineScores(candidate.EmbeddingSimilarity, normalizedRerankScores[index])))
            .ToList();

        var aggregatedResults = AggregateCandidates(scoredCandidates, candidateTopK)
            .Take(finalTopK)
            .ToArray();

        Log.Information(
            "素材搜索完成: elapsedMs={ElapsedMs}, localCandidates={LocalCandidates}, returned={ReturnedCount}, embeddingModel={EmbeddingModel}, rerankModel={RerankModel}",
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            vectorCandidates.Count,
            aggregatedResults.Length,
            queryVectorResponse.EmbeddingModel,
            rerankResponse.RerankModel);

        return new AssetSearchResponseDocument(
            Query: normalizedQuery,
            CandidateTopK: Math.Min(candidateTopK, aggregatedResults.Length),
            FinalTopK: Math.Min(finalTopK, aggregatedResults.Length),
            AssetFormat: normalizedAssetFormat,
            EmbeddingModel: queryVectorResponse.EmbeddingModel,
            RerankModel: rerankResponse.RerankModel,
            Results: aggregatedResults);
    }

    public async Task<AssetReindexResponseDocument> ReindexAsync(
        string backendBaseUrl,
        CancellationToken ct = default)
    {
        var records = await LoadVectorRecordsAsync(ct).ConfigureAwait(false);
        if (records.Count == 0)
        {
            throw new InvalidOperationException("当前没有可用于本地检索的向量数据。");
        }

        var state = BuildIndexState(records);
        IndexManager.Rebuild(records.Select(record => record.Vector).ToArray(), state);

        var vectorDim = records[0].Vector.Length;
        var embeddingModels = records
            .Select(record => record.EmbeddingModel)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        Log.Information(
            "本地检索模式下刷新索引信息: documentCount={DocumentCount}, vectorDim={VectorDim}, databasePath={DatabasePath}",
            records.Count,
            vectorDim,
            AssetDatabase.DatabasePath);

        return new AssetReindexResponseDocument(
            DocumentCount: records.Count,
            VectorDim: vectorDim,
            DatabasePath: AssetDatabase.DatabasePath,
            IndexPath: IndexManager.IndexPath,
            MetadataPath: IndexManager.MetadataPath,
            EmbeddingModels: embeddingModels);
    }

    public async Task<AssetSearchWarmupDocument> WarmupEmbeddingAsync(
        string backendBaseUrl,
        CancellationToken ct = default)
    {
        return await WarmupAsync(backendBaseUrl, "embedding", ct).ConfigureAwait(false);
    }

    public async Task<AssetSearchWarmupDocument> WarmupRerankAsync(
        string backendBaseUrl,
        CancellationToken ct = default)
    {
        return await WarmupAsync(backendBaseUrl, "rerank", ct).ConfigureAwait(false);
    }

    public async Task<AssetSearchModelStatusDocument> GetModelStatusAsync(
        string backendBaseUrl,
        CancellationToken ct = default)
    {
        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/models/status";
        Log.Debug("查询模型状态: endpoint={Endpoint}", endpoint);
        using var response = await Http.GetAsync(endpoint, ct).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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
        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await Http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"后端模型关闭失败：{responseText}");
        }

        var backendResponse = JsonSerializer.Deserialize<SearchModelCloseResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空模型关闭响应。");

        return new AssetSearchModelCloseDocument(
            ModelKind: backendResponse.ModelKind,
            ModelName: backendResponse.ModelName,
            Device: backendResponse.Device,
            Closed: backendResponse.Closed,
            CudaCacheCleared: backendResponse.CudaCacheCleared,
            RemainingLoadedModels: backendResponse.RemainingLoadedModels.ToArray());
    }

    private async Task<SearchIndexResponse> IndexTextAsync(string backendBaseUrl, string text, CancellationToken ct)
    {
        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/index";
        var request = new SearchIndexRequest(
            AssetId: "__query__",
            AssetName: "__query__",
            AssetFormat: "文本",
            AssetPath: Environment.SystemDirectory,
            Description: text,
            GeneratedAt: null);

        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await Http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"后端向量化失败：{responseText}");
        }

        return JsonSerializer.Deserialize<SearchIndexResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空向量响应。");
    }

    private async Task<SearchQueryResponse> RerankAsync(
        string backendBaseUrl,
        string query,
        IReadOnlyList<VectorCandidateRecord> candidates,
        int finalTopK,
        CancellationToken ct)
    {
        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/query";
        var request = new SearchQueryRequest(
            Query: query,
            Candidates: candidates.Select(candidate => new SearchQueryCandidate(
                CandidateId: candidate.CandidateId,
                AssetId: candidate.Record.AssetUid,
                AssetName: candidate.Record.AssetName,
                AssetFormat: candidate.Record.AssetType,
                AssetPath: candidate.Record.AssetPath,
                Description: candidate.Record.SegmentText,
                Tags: candidate.Record.Tags,
                GeneratedAt: candidate.Record.GeneratedAt)).ToArray(),
            FinalTopK: candidates.Count);

        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await Http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"后端重排序失败：{responseText}");
        }

        return JsonSerializer.Deserialize<SearchQueryResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空重排序响应。");
    }

    private async Task<List<LocalVectorRecord>> LoadVectorRecordsAsync(CancellationToken ct)
    {
        await AssetDatabase.EnsureSchemaAsync(ct).ConfigureAwait(false);
        await using var connection = await AssetDatabase.OpenConnectionAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                v.asset_id,
                COALESCE(NULLIF(TRIM(v.angle_type), ''), '全面') AS angle_type,
                COALESCE(a.asset_name, d.asset_name, '') AS asset_name,
                COALESCE(a.asset_type, d.asset_type, '') AS asset_type,
                COALESCE(a.current_path, d.asset_path, '') AS asset_path,
                COALESCE(d.description, '') AS raw_description,
                COALESCE(m.tags_json, '[]') AS tags_json,
                d.generated_at,
                v.vectorized_at,
                v.embedding_model,
                v.vector_dim,
                v.vector_blob
            FROM asset_description_vectors AS v
            LEFT JOIN asset_descriptions AS d ON d.asset_id = v.asset_id
            LEFT JOIN asset_metadata AS m ON m.asset_uid = v.asset_id
            LEFT JOIN assets AS a ON a.asset_uid = v.asset_id
            ORDER BY v.asset_id, v.angle_type;
            """;

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var records = new List<LocalVectorRecord>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var rawDescription = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            var angleType = reader.GetString(1);
            records.Add(new LocalVectorRecord(
                AssetUid: reader.GetString(0),
                AngleType: angleType,
                AssetName: reader.GetString(2),
                AssetType: reader.GetString(3),
                AssetPath: reader.GetString(4),
                PrimaryDescription: StructuredDescriptionHelper.ExtractPrimaryText(rawDescription),
                SegmentText: StructuredDescriptionHelper.ExtractTextByAngle(rawDescription, angleType),
                Tags: DeserializeTags(reader.IsDBNull(6) ? null : reader.GetString(6)),
                GeneratedAt: reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
                VectorizedAt: reader.IsDBNull(8) ? DateTimeOffset.MinValue : DateTimeOffset.Parse(reader.GetString(8)),
                EmbeddingModel: reader.GetString(9),
                Vector: DeserializeVector(reader.GetFieldValue<byte[]>(11), reader.GetInt32(10))));
        }

        return records;
    }

    private async Task<AssetSearchWarmupDocument> WarmupAsync(
        string backendBaseUrl,
        string modelKind,
        CancellationToken ct)
    {
        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/warmup/{modelKind}";
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
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

    private static float[] DeserializeVector(byte[] bytes, int expectedDim)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        if (vector.Length != expectedDim)
        {
            throw new InvalidOperationException($"向量维度不匹配，期望 {expectedDim}，实际 {vector.Length}。");
        }

        return vector;
    }

    private static string[] DeserializeTags(string? tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(tagsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static LocalVectorIndexState BuildIndexState(IReadOnlyList<LocalVectorRecord> records)
    {
        var latestUpdatedAt = records
            .Select(record => record.VectorizedAt.ToString("O"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .LastOrDefault() ?? string.Empty;
        return new LocalVectorIndexState(records.Count, latestUpdatedAt);
    }

    private static float[] NormalizeScores(IReadOnlyList<float> scores)
    {
        if (scores.Count == 0)
        {
            return [];
        }

        var minScore = scores.Min();
        var maxScore = scores.Max();
        if (Math.Abs(maxScore - minScore) < float.Epsilon)
        {
            return Enumerable.Repeat(1f, scores.Count).ToArray();
        }

        var scale = maxScore - minScore;
        return scores.Select(score => (score - minScore) / scale).ToArray();
    }

    private static float CombineScores(float embeddingSimilarity, float normalizedRerankScore)
    {
        const float vectorWeight = 0.35f;
        const float rerankWeight = 0.65f;
        return (embeddingSimilarity * vectorWeight) + (normalizedRerankScore * rerankWeight);
    }

    private static float AngleWeight(string angleType)
    {
        return angleType switch
        {
            "全面" => 0.4f,
            "风格" => 0.25f,
            "乐器" => 0.2f,
            "情感" => 0.15f,
            _ => 0.1f,
        };
    }

    private static List<AssetSearchDocument> AggregateCandidates(IReadOnlyList<ScoredVectorCandidateRecord> candidates, int candidateTopK)
    {
        var grouped = new Dictionary<string, Dictionary<string, ScoredVectorCandidateRecord>>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!grouped.TryGetValue(candidate.Record.AssetUid, out var assetCandidates))
            {
                assetCandidates = new Dictionary<string, ScoredVectorCandidateRecord>(StringComparer.Ordinal);
                grouped[candidate.Record.AssetUid] = assetCandidates;
            }

            if (!assetCandidates.TryGetValue(candidate.Record.AngleType, out var existing)
                || candidate.CombinedScore > existing.CombinedScore)
            {
                assetCandidates[candidate.Record.AngleType] = candidate;
            }
        }

        var results = new List<AssetSearchDocument>();
        foreach (var assetCandidates in grouped.Values)
        {
            var selectedCandidates = assetCandidates.Values.ToArray();
            var totalWeight = selectedCandidates.Sum(item => AngleWeight(item.Record.AngleType));
            if (totalWeight <= 0)
            {
                continue;
            }

            var displayCandidate = selectedCandidates.FirstOrDefault(item => item.Record.AngleType == "全面")
                ?? selectedCandidates.OrderByDescending(item => item.CombinedScore).First();

            var result = new AssetSearchDocument(
                assetUid: displayCandidate.Record.AssetUid,
                assetName: displayCandidate.Record.AssetName,
                assetType: displayCandidate.Record.AssetType,
                currentPath: displayCandidate.Record.AssetPath,
                description: displayCandidate.Record.PrimaryDescription,
                generatedAt: displayCandidate.Record.GeneratedAt,
                embeddingSimilarity: WeightedAverage(selectedCandidates, totalWeight, item => item.EmbeddingSimilarity),
                vectorDistance: WeightedAverage(selectedCandidates, totalWeight, item => item.VectorDistance),
                rerankScore: WeightedAverage(selectedCandidates, totalWeight, item => item.RerankScore),
                tags: displayCandidate.Record.Tags)
            {
                CombinedScore = WeightedAverage(selectedCandidates, totalWeight, item => item.CombinedScore),
            };
            results.Add(result);
        }

        return results
            .OrderByDescending(item => item.CombinedScore ?? item.RerankScore)
            .Take(candidateTopK)
            .ToList();
    }

    private static float WeightedAverage(
        IReadOnlyList<ScoredVectorCandidateRecord> candidates,
        float totalWeight,
        Func<ScoredVectorCandidateRecord, float> selector)
    {
        if (totalWeight <= 0)
        {
            return 0f;
        }

        return candidates.Sum(candidate => selector(candidate) * AngleWeight(candidate.Record.AngleType)) / totalWeight;
    }

    private sealed record SearchIndexRequest(
        [property: JsonPropertyName("asset_id")] string AssetId,
        [property: JsonPropertyName("asset_name")] string AssetName,
        [property: JsonPropertyName("asset_format")] string AssetFormat,
        [property: JsonPropertyName("asset_path")] string AssetPath,
        [property: JsonPropertyName("description")] string Description,
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

    private sealed record SearchQueryRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("candidates")] SearchQueryCandidate[] Candidates,
        [property: JsonPropertyName("final_top_k")] int FinalTopK);

    private sealed record SearchQueryCandidate(
        [property: JsonPropertyName("candidate_id")] string CandidateId,
        [property: JsonPropertyName("asset_id")] string AssetId,
        [property: JsonPropertyName("asset_name")] string AssetName,
        [property: JsonPropertyName("asset_format")] string AssetFormat,
        [property: JsonPropertyName("asset_path")] string AssetPath,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("tags")] string[] Tags,
        [property: JsonPropertyName("generated_at")] DateTimeOffset? GeneratedAt);

    private sealed record SearchQueryResponse(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("final_top_k")] int FinalTopK,
        [property: JsonPropertyName("rerank_model")] string RerankModel,
        [property: JsonPropertyName("results")] SearchQueryResult[] Results);

    private sealed record SearchQueryResult(
        [property: JsonPropertyName("candidate_id")] string? CandidateId,
        [property: JsonPropertyName("rerank_score")] float RerankScore);

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

    private sealed record LocalVectorRecord(
        string AssetUid,
        string AngleType,
        string AssetName,
        string AssetType,
        string AssetPath,
        string PrimaryDescription,
        string SegmentText,
        string[] Tags,
        DateTimeOffset? GeneratedAt,
        DateTimeOffset VectorizedAt,
        string EmbeddingModel,
        float[] Vector);

    private sealed record VectorCandidateRecord(
        string CandidateId,
        LocalVectorRecord Record,
        float EmbeddingSimilarity,
        float VectorDistance);

    private sealed record ScoredVectorCandidateRecord(
        string CandidateId,
        LocalVectorRecord Record,
        float EmbeddingSimilarity,
        float VectorDistance,
        float RerankScore,
        float NormalizedRerankScore,
        float CombinedScore);
}
