using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

public interface IVectorRetrievalStrategy
{
    string Name { get; }

    bool CanRetrieve(IReadOnlyList<LocalVectorRecord> records);

    IReadOnlyList<(int Index, float Similarity)> Retrieve(
        string embeddingModelKey,
        IReadOnlyList<LocalVectorRecord> records,
        float[] queryVector,
        int topK);
}

public sealed class ExactVectorRetriever : IVectorRetrievalStrategy
{
    public string Name => "ExactCosine";

    public bool CanRetrieve(IReadOnlyList<LocalVectorRecord> records) => true;

    public IReadOnlyList<(int Index, float Similarity)> Retrieve(
        string embeddingModelKey,
        IReadOnlyList<LocalVectorRecord> records,
        float[] queryVector,
        int topK) =>
        records
            .Select((record, index) => (Index: index, Similarity: CosineSimilarity(queryVector, record.Vector)))
            .OrderByDescending(item => item.Similarity)
            .Take(topK)
            .ToArray();

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
}

public sealed class HnswVectorRetriever : IVectorRetrievalStrategy
{
    public string Name => "Hnsw";

    public bool CanRetrieve(IReadOnlyList<LocalVectorRecord> records) => true;

    public IReadOnlyList<(int Index, float Similarity)> Retrieve(
        string embeddingModelKey,
        IReadOnlyList<LocalVectorRecord> records,
        float[] queryVector,
        int topK)
    {
        var indexManager = new LocalHnswSearchIndexManager(embeddingModelKey);
        indexManager.EnsureCurrent(
            records.Select(record => record.Vector).ToArray(),
            records.Select(BuildVectorKey).ToArray(),
            BuildIndexState(records));
        return indexManager.Search(queryVector, topK);
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

public sealed class VectorRetrieverSelector : IVectorCandidateRetriever
{
    private const int ExactSearchThreshold = 5000;

    private ExactVectorRetriever ExactRetriever { get; }
    private HnswVectorRetriever HnswRetriever { get; }

    public VectorRetrieverSelector(ExactVectorRetriever exactRetriever, HnswVectorRetriever hnswRetriever)
    {
        ExactRetriever = exactRetriever;
        HnswRetriever = hnswRetriever;
    }

    public Task<VectorRetrievalResult> RetrieveAsync(
        string embeddingModelKey,
        IReadOnlyList<LocalVectorRecord> records,
        float[] queryVector,
        int expandedCandidateTopK,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var effectiveTopK = Math.Min(records.Count, expandedCandidateTopK);
        var retriever = records.Count <= ExactSearchThreshold
            ? (IVectorRetrievalStrategy)ExactRetriever
            : HnswRetriever;
        var searchResults = retriever.Retrieve(embeddingModelKey, records, queryVector, effectiveTopK);
        var candidates = searchResults
            .Where(result => result.Index >= 0 && result.Index < records.Count)
            .Take(effectiveTopK)
            .Select(result => BuildCandidate(records[result.Index], result.Similarity))
            .ToArray();
        return Task.FromResult(new VectorRetrievalResult(candidates, retriever.Name, effectiveTopK));
    }

    private static VectorCandidateRecord BuildCandidate(LocalVectorRecord record, float similarity) =>
        new(
            CandidateId: $"{record.AssetUid}::{record.AngleType}",
            Record: record,
            EmbeddingSimilarity: similarity,
            VectorDistance: Math.Max(0f, 1f - similarity));
}
