using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
    private static object SyncRoot { get; } = new();

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

    public void EnsureCurrent(
        IReadOnlyList<float[]> vectors,
        IReadOnlyList<string> orderedKeys,
        LocalVectorIndexState state)
    {
        lock (SyncRoot)
        {
            ValidateInputs(vectors, orderedKeys);

            if (IsCurrent(vectors, orderedKeys, state))
            {
                Load(vectors, orderedKeys);
                return;
            }

            Build(vectors, orderedKeys, state);
        }
    }

    public void Rebuild(
        IReadOnlyList<float[]> vectors,
        IReadOnlyList<string> orderedKeys,
        LocalVectorIndexState state)
    {
        lock (SyncRoot)
        {
            ValidateInputs(vectors, orderedKeys);

            ResetFiles();
            Build(vectors, orderedKeys, state);
        }
    }

    public IReadOnlyList<(int Index, float Similarity)> Search(float[] queryVector, int topK)
    {
        lock (SyncRoot)
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
    }

    private void Build(
        IReadOnlyList<float[]> vectors,
        IReadOnlyList<string> orderedKeys,
        LocalVectorIndexState state)
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
                new LocalHnswMetadata(
                    dim,
                    state.DocumentCount,
                    state.LatestUpdatedAt,
                    BuildEntriesFingerprint(vectors, orderedKeys),
                    orderedKeys.ToArray()),
                new JsonSerializerOptions { WriteIndented = true }));

        _graph = graph;
        _dim = dim;
    }

    private void Load(IReadOnlyList<float[]> vectors, IReadOnlyList<string> orderedKeys)
    {
        if (!File.Exists(IndexPath) || !File.Exists(MetadataPath))
        {
            throw new FileNotFoundException("本地 HNSW 索引文件不存在。");
        }

        var metadata = JsonSerializer.Deserialize<LocalHnswMetadata>(File.ReadAllText(MetadataPath))
            ?? throw new InvalidOperationException("本地 HNSW 元数据为空。");
        if (!IsCompatible(metadata, vectors, orderedKeys))
        {
            throw new InvalidOperationException("本地 HNSW 索引元数据与当前向量身份不一致。");
        }

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

    private bool IsCurrent(
        IReadOnlyList<float[]> vectors,
        IReadOnlyList<string> orderedKeys,
        LocalVectorIndexState state)
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
                && string.Equals(metadata.LatestUpdatedAt, state.LatestUpdatedAt, StringComparison.Ordinal)
                && IsCompatible(metadata, vectors, orderedKeys);
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

    private static void ValidateInputs(IReadOnlyList<float[]> vectors, IReadOnlyList<string> orderedKeys)
    {
        if (vectors.Count == 0)
        {
            throw new InvalidOperationException("当前没有可检索的向量数据。");
        }

        if (vectors.Count != orderedKeys.Count)
        {
            throw new InvalidOperationException($"本地 HNSW 索引输入不一致，向量数 {vectors.Count} 与身份键数 {orderedKeys.Count} 不匹配。");
        }
    }

    private static bool IsCompatible(
        LocalHnswMetadata metadata,
        IReadOnlyList<float[]> vectors,
        IReadOnlyList<string> orderedKeys)
    {
        if (metadata.OrderedKeys is null || metadata.OrderedKeys.Length != orderedKeys.Count)
        {
            return false;
        }

        for (var index = 0; index < orderedKeys.Count; index++)
        {
            if (!string.Equals(metadata.OrderedKeys[index], orderedKeys[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        var currentFingerprint = BuildEntriesFingerprint(vectors, orderedKeys);
        return string.Equals(metadata.EntriesFingerprint, currentFingerprint, StringComparison.Ordinal);
    }

    private static string BuildEntriesFingerprint(
        IReadOnlyList<float[]> vectors,
        IReadOnlyList<string> orderedKeys)
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(vectors.Count);
        for (var index = 0; index < vectors.Count; index++)
        {
            writer.Write(orderedKeys[index]);
            writer.Write(vectors[index].Length);
            foreach (var value in vectors[index])
            {
                writer.Write(value.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        writer.Flush();
        stream.Position = 0;
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private sealed record LocalHnswMetadata(
        int Dim,
        int DocumentCount,
        string LatestUpdatedAt,
        string EntriesFingerprint,
        string[] OrderedKeys);
}

internal sealed record LocalVectorIndexState(int DocumentCount, string LatestUpdatedAt);
