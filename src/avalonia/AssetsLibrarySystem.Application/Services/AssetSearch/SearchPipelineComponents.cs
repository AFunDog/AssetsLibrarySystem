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
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

public sealed record SearchRetrievalParameters(
    int CandidateTopK,
    int FinalTopK,
    int ExpandedCandidateTopK,
    int RerankTopK);

public sealed record AssetFormatResolution(
    string Mode,
    string? AssetFormat);

public sealed record QueryEmbeddingResult(
    float[] Vector,
    string EmbeddingModel,
    int? TokenUsage);

public sealed record SearchRerankScore(
    string? CandidateId,
    float RerankScore);

public sealed record RerankResult(
    string RerankModel,
    SearchRerankScore[] Results,
    int? TokenUsage);

public sealed record VectorRetrievalResult(
    IReadOnlyList<VectorCandidateRecord> Candidates,
    string SearchStrategy,
    int ExpandedCandidateTopK);

public sealed record LocalVectorRecord(
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

public sealed record VectorCandidateRecord(
    string CandidateId,
    LocalVectorRecord Record,
    float EmbeddingSimilarity,
    float VectorDistance);

public sealed record ScoredVectorCandidateRecord(
    string CandidateId,
    LocalVectorRecord Record,
    float EmbeddingSimilarity,
    float VectorDistance,
    float RerankScore,
    float NormalizedRerankScore,
    float CombinedScore);

public interface ISearchParameterNormalizer
{
    SearchRetrievalParameters Normalize(int candidateTopK, int finalTopK, int expandedCandidateTopK, int rerankTopK);
}

public interface IAssetFormatResolver
{
    AssetFormatResolution Resolve(string query, string? explicitAssetFormat);

    IReadOnlyList<LocalVectorRecord> Filter(IReadOnlyList<LocalVectorRecord> records, string? assetFormat);
}

public interface IVectorRecordRepository
{
    Task<IReadOnlyList<LocalVectorRecord>> LoadAsync(string embeddingModel, CancellationToken ct = default);
}

public interface IAssetSearchBackendClient
{
    Task<QueryEmbeddingResult> EmbedQueryAsync(
        string backendBaseUrl,
        string text,
        SearchModelOptions searchModels,
        CancellationToken ct = default);

    Task<RerankResult> RerankAsync(
        string backendBaseUrl,
        string query,
        IReadOnlyList<VectorCandidateRecord> candidates,
        int rerankTopK,
        SearchModelOptions searchModels,
        CancellationToken ct = default);

    Task<AssetSearchWarmupDocument> WarmupAsync(
        string backendBaseUrl,
        string modelKind,
        CancellationToken ct = default);

    Task<AssetSearchModelStatusDocument> GetModelStatusAsync(
        string backendBaseUrl,
        CancellationToken ct = default);

    Task<AssetSearchModelCloseDocument> CloseModelAsync(
        string backendBaseUrl,
        string modelKind,
        CancellationToken ct = default);
}

public interface IVectorCandidateRetriever
{
    VectorRetrievalResult Retrieve(
        string embeddingModelKey,
        IReadOnlyList<LocalVectorRecord> records,
        float[] queryVector,
        int expandedCandidateTopK);
}

public interface IScoreFusionService
{
    IReadOnlyList<ScoredVectorCandidateRecord> Score(
        IReadOnlyList<VectorCandidateRecord> rerankCandidates,
        IReadOnlyDictionary<string, float> rerankScoreMap);
}

public interface ISearchResultAggregator
{
    IReadOnlyList<AssetSearchDocument> Aggregate(
        IReadOnlyList<ScoredVectorCandidateRecord> candidates,
        int candidateTopK,
        int finalTopK);
}

public sealed class SearchParameterNormalizer : ISearchParameterNormalizer
{
    public SearchRetrievalParameters Normalize(int candidateTopK, int finalTopK, int expandedCandidateTopK, int rerankTopK)
    {
        candidateTopK = NormalizePositive(candidateTopK, 20, 1, 500);
        finalTopK = Math.Min(NormalizePositive(finalTopK, 5, 1, 100), candidateTopK);
        expandedCandidateTopK = Math.Max(NormalizePositive(expandedCandidateTopK, 160, 1, 5000), candidateTopK);
        rerankTopK = NormalizePositive(rerankTopK, 50, 1, 1000);
        return new SearchRetrievalParameters(candidateTopK, finalTopK, expandedCandidateTopK, rerankTopK);
    }

    private static int NormalizePositive(int value, int fallback, int min, int max)
    {
        if (value <= 0)
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }
}

public sealed class AssetFormatResolver : IAssetFormatResolver
{
    private const string SmartAssetFormat = "智能类型";

    public AssetFormatResolution Resolve(string query, string? explicitAssetFormat)
    {
        var mode = ResolveMode(explicitAssetFormat);
        if (mode == "all")
        {
            return new AssetFormatResolution(mode, null);
        }

        if (mode == "explicit")
        {
            return new AssetFormatResolution(mode, explicitAssetFormat!.Trim());
        }

        if (ContainsAny(query, "图片", "图像", "照片", "插画", "壁纸", "立绘", "截图"))
        {
            return new AssetFormatResolution(mode, "图片");
        }

        if (ContainsAny(query, "音频", "音乐", "歌曲", "BGM", "bgm", "配乐", "声音"))
        {
            return new AssetFormatResolution(mode, "音频");
        }

        if (ContainsAny(query, "视频", "动画", "片段", "镜头", "录像"))
        {
            return new AssetFormatResolution(mode, "视频");
        }

        if (ContainsAny(query, "文本", "文档", "剧本", "台词", "字幕"))
        {
            return new AssetFormatResolution(mode, "文本");
        }

        return new AssetFormatResolution(mode, null);
    }

    public IReadOnlyList<LocalVectorRecord> Filter(IReadOnlyList<LocalVectorRecord> records, string? assetFormat)
    {
        return records
            .Where(record => assetFormat is null || string.Equals(record.AssetType, assetFormat, StringComparison.Ordinal))
            .ToList();
    }

    private static string ResolveMode(string? explicitAssetFormat)
    {
        if (string.IsNullOrWhiteSpace(explicitAssetFormat) || string.Equals(explicitAssetFormat.Trim(), "全部", StringComparison.Ordinal))
        {
            return "all";
        }

        return string.Equals(explicitAssetFormat.Trim(), SmartAssetFormat, StringComparison.Ordinal)
            ? "smart"
            : "explicit";
    }

    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}

public sealed class VectorRecordRepository : IVectorRecordRepository
{
    private IAssetDatabase AssetDatabase { get; }

    public VectorRecordRepository(IAssetDatabase assetDatabase)
    {
        AssetDatabase = assetDatabase;
    }

    public async Task<IReadOnlyList<LocalVectorRecord>> LoadAsync(string embeddingModel, CancellationToken ct = default)
    {
        await AssetDatabase.EnsureSchemaAsync(ct).ConfigureAwait(false);
        await using var connection = await AssetDatabase.OpenConnectionAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                v.asset_id,
                a.asset_uid,
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
            LEFT JOIN asset_metadata AS m ON m.asset_id = v.asset_id
            LEFT JOIN assets AS a ON a.id = v.asset_id
            WHERE v.embedding_model = $embedding_model
            ORDER BY v.asset_id, v.angle_type;
            """;
        command.Parameters.AddWithValue("$embedding_model", embeddingModel);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var records = new List<LocalVectorRecord>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var rawDescription = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
            var angleType = reader.GetString(2);
            records.Add(new LocalVectorRecord(
                AssetUid: reader.GetString(1),
                AngleType: angleType,
                AssetName: reader.GetString(3),
                AssetType: reader.GetString(4),
                AssetPath: reader.GetString(5),
                PrimaryDescription: StructuredDescriptionHelper.ExtractPrimaryText(rawDescription),
                SegmentText: StructuredDescriptionHelper.ExtractTextByAngle(rawDescription, angleType),
                Tags: DeserializeTags(reader.IsDBNull(7) ? null : reader.GetString(7)),
                GeneratedAt: reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
                VectorizedAt: reader.IsDBNull(9) ? DateTimeOffset.MinValue : DateTimeOffset.Parse(reader.GetString(9)),
                EmbeddingModel: reader.GetString(10),
                Vector: DeserializeVector(reader.GetFieldValue<byte[]>(12), reader.GetInt32(11))));
        }

        return records;
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
}

public sealed class AssetSearchBackendClient : IAssetSearchBackendClient
{
    private HttpClient Http { get; } = new();
    private JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<QueryEmbeddingResult> EmbedQueryAsync(
        string backendBaseUrl,
        string text,
        SearchModelOptions searchModels,
        CancellationToken ct = default)
    {
        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/index";
        var request = new SearchIndexRequest(
            Provider: searchModels.EmbeddingProvider,
            Model: searchModels.EmbeddingModel,
            EmbeddingDimensions: searchModels.IsDashScopeEmbeddingProvider ? searchModels.EmbeddingDimensions : null,
            AssetId: "__query__",
            AssetName: "__query__",
            AssetFormat: "文本",
            AssetPath: Environment.SystemDirectory,
            Description: text,
            GeneratedAt: null);

        using var content = CreateJsonContent(request);
        using var response = await Http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"后端向量化失败：{responseText}");
        }

        var backendResponse = JsonSerializer.Deserialize<SearchIndexResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空向量响应。");
        var queryVector = JsonSerializer.Deserialize<float[]>(backendResponse.Vector.GetRawText(), JsonOptions) ?? [];
        return new QueryEmbeddingResult(queryVector, backendResponse.EmbeddingModel, backendResponse.TokenUsage);
    }

    public async Task<RerankResult> RerankAsync(
        string backendBaseUrl,
        string query,
        IReadOnlyList<VectorCandidateRecord> candidates,
        int rerankTopK,
        SearchModelOptions searchModels,
        CancellationToken ct = default)
    {
        var endpoint = $"{backendBaseUrl.TrimEnd('/')}/api/v1/search/query";
        var requestedTopK = Math.Min(rerankTopK, candidates.Count);
        var request = new SearchQueryRequest(
            Provider: searchModels.RerankProvider,
            Model: searchModels.RerankModel,
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
            FinalTopK: requestedTopK);

        using var content = CreateJsonContent(request);
        using var response = await Http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"后端重排序失败：{responseText}");
        }

        var backendResponse = JsonSerializer.Deserialize<SearchQueryResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("后端返回空重排序响应。");
        return new RerankResult(
            backendResponse.RerankModel,
            backendResponse.Results.Select(item => new SearchRerankScore(item.CandidateId, item.RerankScore)).ToArray(),
            backendResponse.TokenUsage);
    }

    public async Task<AssetSearchWarmupDocument> WarmupAsync(
        string backendBaseUrl,
        string modelKind,
        CancellationToken ct = default)
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
        using var content = CreateJsonContent(request);
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

    private StringContent CreateJsonContent<T>(T request) =>
        new(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");

    private sealed record SearchIndexRequest(
        string Provider,
        string Model,
        int? EmbeddingDimensions,
        string AssetId,
        string AssetName,
        string AssetFormat,
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
        string EmbeddingModel,
        int? TokenUsage);

    private sealed record SearchQueryRequest(
        string Provider,
        string Model,
        string Query,
        SearchQueryCandidate[] Candidates,
        int FinalTopK);

    private sealed record SearchQueryCandidate(
        string CandidateId,
        string AssetId,
        string AssetName,
        string AssetFormat,
        string AssetPath,
        string Description,
        string[] Tags,
        DateTimeOffset? GeneratedAt);

    private sealed record SearchQueryResponse(
        string Query,
        int FinalTopK,
        string RerankModel,
        SearchQueryResult[] Results,
        int? TokenUsage);

    private sealed record SearchQueryResult(string? CandidateId, float RerankScore);

    private sealed record SearchWarmupResponse(string ModelKind, string ModelName, string Device, bool Warmed);

    private sealed record SearchModelStatusResponse(
        string EmbeddingModelName,
        string RerankModelName,
        string Device,
        string[] LoadedModelKinds,
        bool EmbeddingLoaded,
        bool RerankLoaded,
        int LoadedCount);

    private sealed record SearchModelCloseRequest(string ModelKind);

    private sealed record SearchModelCloseResponse(
        string ModelKind,
        string ModelName,
        string Device,
        bool Closed,
        bool CudaCacheCleared,
        string[] RemainingLoadedModels);
}

public sealed class VectorCandidateRetriever : IVectorCandidateRetriever
{
    private const int ExactSearchThreshold = 5000;

    public VectorRetrievalResult Retrieve(
        string embeddingModelKey,
        IReadOnlyList<LocalVectorRecord> records,
        float[] queryVector,
        int expandedCandidateTopK)
    {
        var effectiveExpandedCandidateTopK = Math.Min(records.Count, expandedCandidateTopK);
        var useExactSearch = records.Count <= ExactSearchThreshold;
        var searchStrategy = useExactSearch ? "ExactCosine" : "Hnsw";

        IReadOnlyList<(int Index, float Similarity)> searchResults;
        if (useExactSearch)
        {
            searchResults = SearchExact(records, queryVector, effectiveExpandedCandidateTopK);
        }
        else
        {
            var indexManager = new LocalHnswSearchIndexManager(embeddingModelKey);
            indexManager.EnsureCurrent(
                records.Select(record => record.Vector).ToArray(),
                records.Select(BuildVectorKey).ToArray(),
                BuildIndexState(records));
            searchResults = indexManager.Search(queryVector, effectiveExpandedCandidateTopK);
        }

        var candidates = new List<VectorCandidateRecord>(searchResults.Count);
        foreach (var (index, similarity) in searchResults)
        {
            if (index < 0 || index >= records.Count)
            {
                continue;
            }

            var record = records[index];
            candidates.Add(new VectorCandidateRecord(
                CandidateId: $"{record.AssetUid}::{record.AngleType}",
                Record: record,
                EmbeddingSimilarity: similarity,
                VectorDistance: Math.Max(0f, 1f - similarity)));

            if (candidates.Count >= effectiveExpandedCandidateTopK)
            {
                break;
            }
        }

        return new VectorRetrievalResult(candidates, searchStrategy, effectiveExpandedCandidateTopK);
    }

    private static IReadOnlyList<(int Index, float Similarity)> SearchExact(
        IReadOnlyList<LocalVectorRecord> records,
        float[] queryVector,
        int topK)
    {
        if (records.Count == 0 || topK <= 0)
        {
            return [];
        }

        return records
            .Select((record, index) => (Index: index, Similarity: CosineSimilarity(queryVector, record.Vector)))
            .OrderByDescending(item => item.Similarity)
            .Take(topK)
            .ToArray();
    }

    private static float CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length != right.Length)
        {
            throw new InvalidOperationException($"查询向量维度不匹配，期望 {right.Length}，实际 {left.Length}。");
        }

        var dot = 0f;
        var leftNorm = 0f;
        var rightNorm = 0f;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
        }

        if (leftNorm <= float.Epsilon || rightNorm <= float.Epsilon)
        {
            return 0f;
        }

        var denominator = MathF.Sqrt(leftNorm) * MathF.Sqrt(rightNorm);
        return denominator <= float.Epsilon ? 0f : dot / denominator;
    }

    private static LocalVectorIndexState BuildIndexState(IReadOnlyList<LocalVectorRecord> records)
    {
        var latestUpdatedAt = records
            .Select(record => record.VectorizedAt.ToString("O"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .LastOrDefault() ?? string.Empty;
        return new LocalVectorIndexState(records.Count, latestUpdatedAt);
    }

    private static string BuildVectorKey(LocalVectorRecord record) => $"{record.AssetUid}::{record.AngleType}";
}

public sealed class ScoreFusionService : IScoreFusionService
{
    public IReadOnlyList<ScoredVectorCandidateRecord> Score(
        IReadOnlyList<VectorCandidateRecord> rerankCandidates,
        IReadOnlyDictionary<string, float> rerankScoreMap)
    {
        var scoredSourceCandidates = rerankCandidates
            .Where(candidate => rerankScoreMap.ContainsKey(candidate.CandidateId))
            .ToList();
        if (scoredSourceCandidates.Count == 0)
        {
            throw new InvalidOperationException("后端没有返回任何可用的重排序分数。");
        }

        if (scoredSourceCandidates.Count != rerankCandidates.Count)
        {
            Log.Warning(
                "后端重排序结果不完整: requested={RequestedCount}, returned={ReturnedCount}",
                rerankCandidates.Count,
                scoredSourceCandidates.Count);
        }

        var rerankScores = scoredSourceCandidates
            .Select(candidate => rerankScoreMap[candidate.CandidateId])
            .ToArray();
        var normalizedRerankScores = NormalizeScores(rerankScores);

        return scoredSourceCandidates
            .Select((candidate, index) => new ScoredVectorCandidateRecord(
                candidate.CandidateId,
                candidate.Record,
                candidate.EmbeddingSimilarity,
                candidate.VectorDistance,
                rerankScores[index],
                normalizedRerankScores[index],
                CombineScores(candidate.EmbeddingSimilarity, normalizedRerankScores[index])))
            .ToList();
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
}

public sealed class SearchResultAggregator : ISearchResultAggregator
{
    public IReadOnlyList<AssetSearchDocument> Aggregate(
        IReadOnlyList<ScoredVectorCandidateRecord> candidates,
        int candidateTopK,
        int finalTopK)
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
            var bestCandidate = selectedCandidates.OrderByDescending(item => item.CombinedScore).First();
            var displayCandidate = selectedCandidates.FirstOrDefault(item => item.Record.AngleType == "全面")
                                   ?? bestCandidate;

            var result = new AssetSearchDocument(
                assetUid: displayCandidate.Record.AssetUid,
                assetName: displayCandidate.Record.AssetName,
                assetType: displayCandidate.Record.AssetType,
                currentPath: displayCandidate.Record.AssetPath,
                description: displayCandidate.Record.PrimaryDescription,
                generatedAt: displayCandidate.Record.GeneratedAt,
                embeddingSimilarity: bestCandidate.EmbeddingSimilarity,
                vectorDistance: bestCandidate.VectorDistance,
                rerankScore: bestCandidate.RerankScore,
                tags: displayCandidate.Record.Tags)
            {
                CombinedScore = bestCandidate.CombinedScore,
            };
            results.Add(result);
        }

        return results
            .OrderByDescending(item => item.CombinedScore ?? item.RerankScore)
            .Take(candidateTopK)
            .Take(finalTopK)
            .ToArray();
    }
}
