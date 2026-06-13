using System;
using System.Diagnostics;
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
    private ISearchParameterNormalizer ParameterNormalizer { get; }
    private IAssetFormatResolver AssetFormatResolver { get; }
    private IVectorRecordRepository VectorRecordRepository { get; }
    private IAssetSearchBackendClient BackendClient { get; }
    private IVectorCandidateRetriever VectorCandidateRetriever { get; }
    private IScoreFusionService ScoreFusionService { get; }
    private ISearchResultAggregator SearchResultAggregator { get; }

    public AssetSearchService(
        IAssetDatabase assetDatabase,
        ISearchModelOptionsProvider searchModelOptionsProvider,
        ISearchParameterNormalizer parameterNormalizer,
        IAssetFormatResolver assetFormatResolver,
        IVectorRecordRepository vectorRecordRepository,
        IAssetSearchBackendClient backendClient,
        IVectorCandidateRetriever vectorCandidateRetriever,
        IScoreFusionService scoreFusionService,
        ISearchResultAggregator searchResultAggregator)
    {
        AssetDatabase = assetDatabase;
        SearchModelOptionsProvider = searchModelOptionsProvider;
        ParameterNormalizer = parameterNormalizer;
        AssetFormatResolver = assetFormatResolver;
        VectorRecordRepository = vectorRecordRepository;
        BackendClient = backendClient;
        VectorCandidateRetriever = vectorCandidateRetriever;
        ScoreFusionService = scoreFusionService;
        SearchResultAggregator = searchResultAggregator;
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
        var stopwatch = Stopwatch.StartNew();
        var parameters = ParameterNormalizer.Normalize(
            query,
            candidateTopK,
            finalTopK,
            expandedCandidateTopK,
            rerankTopK);
        var formatResolution = AssetFormatResolver.Resolve(parameters.Query, assetFormat);
        var searchModels = SearchModelOptionsProvider.Current;
        var embeddingModelKey = searchModels.EmbeddingModelKey;

        var records = await VectorRecordRepository.LoadAsync(embeddingModelKey, ct).ConfigureAwait(false);
        if (records.Count == 0)
        {
            throw new InvalidOperationException("当前没有可检索的素材描述。");
        }

        var filteredRecords = AssetFormatResolver.Filter(records, formatResolution.AssetFormat);
        if (filteredRecords.Count == 0)
        {
            throw new InvalidOperationException("未找到符合条件的素材。");
        }

        var queryVectorResponse = await BackendClient.EmbedQueryAsync(
            backendBaseUrl,
            parameters.Query,
            searchModels,
            ct).ConfigureAwait(false);
        var vectorRetrieval = VectorCandidateRetriever.Retrieve(
            embeddingModelKey,
            filteredRecords,
            queryVectorResponse.Vector,
            parameters.ExpandedCandidateTopK);
        if (vectorRetrieval.Candidates.Count == 0)
        {
            throw new InvalidOperationException("未找到符合条件的素材。");
        }

        Log.Information(
            "素材搜索过滤: query={Query}, assetFormatMode={AssetFormatMode}, assetFormat={AssetFormat}, totalRecords={TotalRecords}, searchRecords={SearchRecords}, candidateTopK={CandidateTopK}, expandedCandidateTopK={ExpandedCandidateTopK}, finalTopK={FinalTopK}, rerankTopK={RerankTopK}, searchStrategy={SearchStrategy}",
            parameters.Query,
            formatResolution.Mode,
            formatResolution.AssetFormat ?? "(all)",
            records.Count,
            filteredRecords.Count,
            parameters.CandidateTopK,
            vectorRetrieval.EffectiveExpandedCandidateTopK,
            parameters.FinalTopK,
            parameters.RerankTopK,
            vectorRetrieval.SearchStrategy);

        var rerankCandidates = vectorRetrieval.Candidates
            .Take(Math.Min(vectorRetrieval.Candidates.Count, parameters.RerankTopK))
            .ToList();
        var rerankResponse = await BackendClient.RerankAsync(
            backendBaseUrl,
            parameters.Query,
            rerankCandidates,
            rerankCandidates.Count,
            searchModels,
            ct).ConfigureAwait(false);
        var scoredCandidates = ScoreFusionService.Score(rerankCandidates, rerankResponse);
        var aggregatedResults = SearchResultAggregator.Aggregate(
            scoredCandidates,
            parameters.CandidateTopK,
            parameters.FinalTopK).ToArray();
        stopwatch.Stop();

        var totalTokenUsage = SumTokenUsage(queryVectorResponse.TokenUsage, rerankResponse.TokenUsage);
        Log.Information(
            "素材搜索完成: elapsedMs={ElapsedMs}, tokenUsage={TokenUsage}, embeddingTokenUsage={EmbeddingTokenUsage}, rerankTokenUsage={RerankTokenUsage}, candidateTopK={CandidateTopK}, expandedCandidateTopK={ExpandedCandidateTopK}, finalTopK={FinalTopK}, localCandidates={LocalCandidates}, rerankCandidates={RerankCandidates}, returned={ReturnedCount}, assetFormatMode={AssetFormatMode}, assetFormat={AssetFormat}, embeddingModel={EmbeddingModel}, rerankModel={RerankModel}, searchStrategy={SearchStrategy}",
            stopwatch.Elapsed.TotalMilliseconds,
            totalTokenUsage,
            queryVectorResponse.TokenUsage,
            rerankResponse.TokenUsage,
            parameters.CandidateTopK,
            vectorRetrieval.EffectiveExpandedCandidateTopK,
            parameters.FinalTopK,
            vectorRetrieval.Candidates.Count,
            rerankCandidates.Count,
            aggregatedResults.Length,
            formatResolution.Mode,
            formatResolution.AssetFormat ?? "(all)",
            queryVectorResponse.EmbeddingModel,
            rerankResponse.RerankModel,
            vectorRetrieval.SearchStrategy);

        return new AssetSearchResponseDocument(
            Query: parameters.Query,
            CandidateTopK: parameters.CandidateTopK,
            FinalTopK: parameters.FinalTopK,
            AssetFormat: formatResolution.AssetFormat,
            AssetFormatMode: formatResolution.Mode,
            EmbeddingModel: queryVectorResponse.EmbeddingModel,
            RerankModel: rerankResponse.RerankModel,
            SearchStrategy: vectorRetrieval.SearchStrategy,
            TotalVectorRecordCount: records.Count,
            FilteredVectorRecordCount: filteredRecords.Count,
            ExpandedCandidateTopK: vectorRetrieval.EffectiveExpandedCandidateTopK,
            VectorCandidateCount: vectorRetrieval.Candidates.Count,
            RerankCandidateCount: rerankCandidates.Count,
            ReturnedCount: aggregatedResults.Length,
            ElapsedMs: stopwatch.Elapsed.TotalMilliseconds,
            EmbeddingTokenUsage: queryVectorResponse.TokenUsage,
            RerankTokenUsage: rerankResponse.TokenUsage,
            TotalTokenUsage: totalTokenUsage,
            Results: aggregatedResults);
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
        return await BackendClient.WarmupAsync(backendBaseUrl, "embedding", ct).ConfigureAwait(false);
    }

    public async Task<AssetSearchWarmupDocument> WarmupRerankAsync(string backendBaseUrl, CancellationToken ct = default)
    {
        return await BackendClient.WarmupAsync(backendBaseUrl, "rerank", ct).ConfigureAwait(false);
    }

    public async Task<AssetSearchModelStatusDocument> GetModelStatusAsync(string backendBaseUrl, CancellationToken ct = default)
    {
        return await BackendClient.GetModelStatusAsync(backendBaseUrl, ct).ConfigureAwait(false);
    }

    public async Task<AssetSearchModelCloseDocument> CloseModelAsync(string backendBaseUrl, string modelKind, CancellationToken ct = default)
    {
        return await BackendClient.CloseModelAsync(backendBaseUrl, modelKind, ct).ConfigureAwait(false);
    }

    private static int? SumTokenUsage(int? left, int? right) =>
        left is null && right is null ? null : (left ?? 0) + (right ?? 0);

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
