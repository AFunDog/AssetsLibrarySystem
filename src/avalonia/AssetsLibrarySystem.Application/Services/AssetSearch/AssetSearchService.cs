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
    private ISearchModelManagementClient ModelManagementClient { get; }
    private IAssetSearchPipeline SearchPipeline { get; }

    public AssetSearchService(
        IAssetDatabase assetDatabase,
        ISearchModelOptionsProvider searchModelOptionsProvider,
        IVectorRecordRepository vectorRecordRepository,
        ISearchModelManagementClient modelManagementClient,
        IAssetSearchPipeline searchPipeline)
    {
        AssetDatabase = assetDatabase;
        SearchModelOptionsProvider = searchModelOptionsProvider;
        VectorRecordRepository = vectorRecordRepository;
        ModelManagementClient = modelManagementClient;
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
        var indexManager = new LocalHnswSearchIndexManager(embeddingModelKey);
        var records = await VectorRecordRepository.LoadAsync(embeddingModelKey, ct).ConfigureAwait(false);
        if (records.Count == 0)
        {
            throw new InvalidOperationException("当前没有可用于本地检索的向量数据。");
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

    public async Task<AssetSearchWarmupDocument> WarmupEmbeddingAsync(string backendBaseUrl, CancellationToken ct = default)
    {
        return await ModelManagementClient.WarmupAsync(backendBaseUrl, "embedding", ct).ConfigureAwait(false);
    }

    public async Task<AssetSearchWarmupDocument> WarmupRerankAsync(string backendBaseUrl, CancellationToken ct = default)
    {
        return await ModelManagementClient.WarmupAsync(backendBaseUrl, "rerank", ct).ConfigureAwait(false);
    }

    public async Task<AssetSearchModelStatusDocument> GetModelStatusAsync(string backendBaseUrl, CancellationToken ct = default)
    {
        return await ModelManagementClient.GetModelStatusAsync(backendBaseUrl, ct).ConfigureAwait(false);
    }

    public async Task<AssetSearchModelCloseDocument> CloseModelAsync(string backendBaseUrl, string modelKind, CancellationToken ct = default)
    {
        return await ModelManagementClient.CloseModelAsync(backendBaseUrl, modelKind, ct).ConfigureAwait(false);
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
