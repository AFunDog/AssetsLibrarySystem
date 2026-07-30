using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

public sealed class AssetSearchService : IAssetSearchService
{
    private IAssetDatabase AssetDatabase { get; }
    private ISearchModelOptionsProvider SearchModelOptionsProvider { get; }
    private IVectorRecordRepository VectorRecordRepository { get; }
    private IAssetSearchPipeline SearchPipeline { get; }

    public AssetSearchService(
        IAssetDatabase assetDatabase,
        ISearchModelOptionsProvider searchModelOptionsProvider,
        IVectorRecordRepository vectorRecordRepository,
        IAssetSearchPipeline searchPipeline)
    {
        AssetDatabase = assetDatabase;
        SearchModelOptionsProvider = searchModelOptionsProvider;
        VectorRecordRepository = vectorRecordRepository;
        SearchPipeline = searchPipeline;
    }

    public async Task<AssetSearchResponseDocument> SearchAsync(
        string backendBaseUrl,
        string query,
        int candidateTopK = 20,
        int finalTopK = 5,
        string? assetFormat = null,
        int expandedCandidateTopK = 160,
        int rerankTopK = 50,
        CancellationToken ct = default)
    {
        return await SearchPipeline.ExecuteAsync(
            new AssetSearchPipelineRequest(
                backendBaseUrl,
                query,
                candidateTopK,
                finalTopK,
                assetFormat,
                expandedCandidateTopK,
                rerankTopK),
            ct).ConfigureAwait(false);
    }

    public async Task<AssetReindexResponseDocument> ReindexAsync(CancellationToken ct = default)
    {
        var searchModels = SearchModelOptionsProvider.Current;
        var embeddingModelKey = searchModels.EmbeddingModelKey;
        var indexManager = LocalHnswSearchIndexManagerCache.Get(embeddingModelKey);
        var records = await VectorRecordRepository.LoadAsync(embeddingModelKey, ct).ConfigureAwait(false);
        if (records.Count == 0)
        {
            // 删除最后一批向量后也允许 reindex：清空 HNSW 文件并返回 0 文档结果。
            indexManager.Clear();
            Log.Information(
                "本地检索索引已清空（无向量数据）: databasePath={DatabasePath}, indexPath={IndexPath}",
                AssetDatabase.DatabasePath,
                indexManager.IndexPath);

            return new AssetReindexResponseDocument(
                DocumentCount: 0,
                VectorDim: 0,
                DatabasePath: AssetDatabase.DatabasePath,
                IndexPath: indexManager.IndexPath,
                MetadataPath: indexManager.MetadataPath,
                EmbeddingModels: []);
        }

        var state = BuildIndexState(records);
        indexManager.Rebuild(
            records.Select(record => record.Vector).ToArray(),
            records.Select(BuildVectorKey).ToArray(),
            state);

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
            IndexPath: indexManager.IndexPath,
            MetadataPath: indexManager.MetadataPath,
            EmbeddingModels: embeddingModels);
    }

    private static LocalVectorIndexState BuildIndexState(System.Collections.Generic.IReadOnlyList<LocalVectorRecord> records)
    {
        var latestUpdatedAt = records
            .Select(record => record.VectorizedAt.ToString("O"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .LastOrDefault() ?? string.Empty;
        return new LocalVectorIndexState(records.Count, latestUpdatedAt);
    }

    private static string BuildVectorKey(LocalVectorRecord record) => $"{record.AssetUid}::{record.AngleType}";
}