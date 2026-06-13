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
using AssetsLibrarySystem.Application.Services.BackendApi;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

public sealed record SearchRetrievalParameters(
    string Query,
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
    int EffectiveExpandedCandidateTopK);

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
    SearchRetrievalParameters Normalize(
        string query,
        int candidateTopK,
        int finalTopK,
        int expandedCandidateTopK,
        int rerankTopK);
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

public interface IQueryEmbeddingClient
{
    Task<QueryEmbeddingResult> EmbedQueryAsync(
        string backendBaseUrl,
        string text,
        SearchModelOptions searchModels,
        CancellationToken ct = default);
}

public interface IRerankClient
{
    Task<RerankResult> RerankAsync(
        string backendBaseUrl,
        string query,
        IReadOnlyList<VectorCandidateRecord> candidates,
        int rerankTopK,
        SearchModelOptions searchModels,
        CancellationToken ct = default);
}

public interface ISearchModelManagementClient
{
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
    Task<VectorRetrievalResult> RetrieveAsync(
        string embeddingModelKey,
        IReadOnlyList<LocalVectorRecord> records,
        float[] queryVector,
        int expandedCandidateTopK,
        CancellationToken ct = default);
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
    public SearchRetrievalParameters Normalize(
        string query,
        int candidateTopK,
        int finalTopK,
        int expandedCandidateTopK,
        int rerankTopK)
    {
        query = string.IsNullOrWhiteSpace(query)
            ? throw new ArgumentException("检索词不能为空。", nameof(query))
            : query.Trim();
        candidateTopK = NormalizePositive(candidateTopK, 20, 1, 500);
        finalTopK = Math.Min(NormalizePositive(finalTopK, 5, 1, 100), candidateTopK);
        expandedCandidateTopK = Math.Max(NormalizePositive(expandedCandidateTopK, 160, 1, 5000), candidateTopK);
        rerankTopK = NormalizePositive(rerankTopK, 50, 1, 1000);
        return new SearchRetrievalParameters(query, candidateTopK, finalTopK, expandedCandidateTopK, rerankTopK);
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
