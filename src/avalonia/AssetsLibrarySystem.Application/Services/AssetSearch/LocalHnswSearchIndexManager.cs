using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AssetsLibrarySystem.Application.Infrastructure;
using HNSW.Net;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

internal sealed class LocalHnswSearchIndexManager
{
    private const string IndexFileName = "asset_search_vectors.hnsw";
    private const string MetadataFileName = "asset_search_vectors.meta.json";

    private SmallWorld<float[], float>? _graph;
    private int? _dim;

    public LocalHnswSearchIndexManager()
    {
        IndexPath = SharedDataPathHelper.GetDataFilePath(IndexFileName);
        MetadataPath = SharedDataPathHelper.GetDataFilePath(MetadataFileName);
    }

    internal LocalHnswSearchIndexManager(string indexPath, string metadataPath)
    {
        IndexPath = indexPath;
        MetadataPath = metadataPath;
    }

    public string IndexPath { get; }

    public string MetadataPath { get; }

    public void EnsureCurrent(IReadOnlyList<float[]> vectors, LocalVectorIndexState state)
    {
        if (vectors.Count == 0)
        {
            throw new InvalidOperationException("当前没有可检索的向量数据。");
        }

        if (IsCurrent(state))
        {
            Load(vectors);
            return;
        }

        Build(vectors, state);
    }

    public void Rebuild(IReadOnlyList<float[]> vectors, LocalVectorIndexState state)
    {
        if (vectors.Count == 0)
        {
            throw new InvalidOperationException("当前没有可重建索引的向量数据。");
        }

        ResetFiles();
        Build(vectors, state);
    }

    public IReadOnlyList<(int Index, float Similarity)> Search(float[] queryVector, int topK)
    {
        if (_graph is null)
        {
            throw new InvalidOperationException("本地 HNSW 索引尚未加载。");
        }

        if (_dim is null || queryVector.Length != _dim.Value)
        {
            throw new InvalidOperationException($"查询向量维度不匹配，期望 {_dim ?? 0}，实际 {queryVector.Length}。");
        }

        var results = _graph.KNNSearch(queryVector, topK, item => true, default);
        return results
            .Select(item => (item.Id, Math.Max(0f, 1f - item.Distance)))
            .ToArray();
    }

    private void Build(IReadOnlyList<float[]> vectors, LocalVectorIndexState state)
    {
        var dim = vectors[0].Length;
        if (vectors.Any(vector => vector.Length != dim))
        {
            throw new InvalidOperationException("存在维度不一致的向量，无法构建 HNSW 索引。");
        }

        var parameters = new SmallWorldParameters
        {
            M = 16,
            LevelLambda = 1 / Math.Log(16),
            EfSearch = Math.Max(50, Math.Min(vectors.Count, 100)),
            InitialItemsSize = Math.Max(vectors.Count, 16),
        };

        var graph = new SmallWorld<float[], float>(
            CosineDistance.NonOptimized,
            DefaultRandomGenerator.Instance,
            parameters,
            false);
        graph.AddItems(vectors, null);

        Directory.CreateDirectory(Path.GetDirectoryName(IndexPath)!);
        using (var stream = File.Create(IndexPath))
        {
            graph.SerializeGraph(stream);
        }

        File.WriteAllText(
            MetadataPath,
            JsonSerializer.Serialize(
                new LocalHnswMetadata(dim, state.DocumentCount, state.LatestUpdatedAt),
                new JsonSerializerOptions { WriteIndented = true }));

        _graph = graph;
        _dim = dim;
    }

    private void Load(IReadOnlyList<float[]> vectors)
    {
        if (!File.Exists(IndexPath) || !File.Exists(MetadataPath))
        {
            throw new FileNotFoundException("本地 HNSW 索引文件不存在。");
        }

        var metadata = JsonSerializer.Deserialize<LocalHnswMetadata>(File.ReadAllText(MetadataPath))
            ?? throw new InvalidOperationException("本地 HNSW 元数据为空。");
        if (vectors.Any(vector => vector.Length != metadata.Dim))
        {
            throw new InvalidOperationException("当前向量数据与本地 HNSW 索引维度不一致。");
        }

        using var stream = File.OpenRead(IndexPath);
        var (graph, _) = SmallWorld<float[], float>.DeserializeGraph(
            vectors,
            CosineDistance.NonOptimized,
            DefaultRandomGenerator.Instance,
            stream,
            false);
        _graph = graph;
        _dim = metadata.Dim;
    }

    private bool IsCurrent(LocalVectorIndexState state)
    {
        if (!File.Exists(IndexPath) || !File.Exists(MetadataPath))
        {
            return false;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<LocalHnswMetadata>(File.ReadAllText(MetadataPath));
            return metadata is not null
                && metadata.DocumentCount == state.DocumentCount
                && string.Equals(metadata.LatestUpdatedAt, state.LatestUpdatedAt, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private void ResetFiles()
    {
        _graph = null;
        _dim = null;

        if (File.Exists(IndexPath))
        {
            File.Delete(IndexPath);
        }

        if (File.Exists(MetadataPath))
        {
            File.Delete(MetadataPath);
        }
    }

    private sealed record LocalHnswMetadata(int Dim, int DocumentCount, string LatestUpdatedAt);
}

internal sealed record LocalVectorIndexState(int DocumentCount, string LatestUpdatedAt);
