using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Application.Models;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

public sealed record AssetSearchPipelineRequest(
    string BackendBaseUrl,
    string Query,
    int CandidateTopK,
    int FinalTopK,
    string? AssetFormat,
    int ExpandedCandidateTopK,
    int RerankTopK);

public interface IAssetSearchPipeline
{
    Task<AssetSearchResponseDocument> ExecuteAsync(
        AssetSearchPipelineRequest request,
        CancellationToken ct = default);
}

public interface IRerankCandidateSelector
{
    IReadOnlyList<VectorCandidateRecord> Select(
        IReadOnlyList<VectorCandidateRecord> candidates,
        SearchRetrievalParameters parameters);
}

public sealed class RerankCandidateSelector : IRerankCandidateSelector
{
    public IReadOnlyList<VectorCandidateRecord> Select(
        IReadOnlyList<VectorCandidateRecord> candidates,
        SearchRetrievalParameters parameters) =>
        candidates.Take(Math.Min(candidates.Count, parameters.RerankTopK)).ToArray();
}

public sealed class AssetSearchPipeline : IAssetSearchPipeline
{
    private ISearchModelOptionsProvider SearchModelOptionsProvider { get; }
    private ISearchParameterNormalizer ParameterNormalizer { get; }
    private IAssetFormatResolver AssetFormatResolver { get; }
    private IVectorRecordRepository VectorRecordRepository { get; }
    private IQueryEmbeddingClient QueryEmbeddingClient { get; }
    private IVectorCandidateRetriever VectorCandidateRetriever { get; }
    private IRerankCandidateSelector RerankCandidateSelector { get; }
    private IRerankClient RerankClient { get; }
    private IScoreFusionService ScoreFusionService { get; }
    private ISearchResultAggregator SearchResultAggregator { get; }

    public AssetSearchPipeline(
        ISearchModelOptionsProvider searchModelOptionsProvider,
        ISearchParameterNormalizer parameterNormalizer,
        IAssetFormatResolver assetFormatResolver,
        IVectorRecordRepository vectorRecordRepository,
        IQueryEmbeddingClient queryEmbeddingClient,
        IVectorCandidateRetriever vectorCandidateRetriever,
        IRerankCandidateSelector rerankCandidateSelector,
        IRerankClient rerankClient,
        IScoreFusionService scoreFusionService,
        ISearchResultAggregator searchResultAggregator)
    {
        SearchModelOptionsProvider = searchModelOptionsProvider;
        ParameterNormalizer = parameterNormalizer;
        AssetFormatResolver = assetFormatResolver;
        VectorRecordRepository = vectorRecordRepository;
        QueryEmbeddingClient = queryEmbeddingClient;
        VectorCandidateRetriever = vectorCandidateRetriever;
        RerankCandidateSelector = rerankCandidateSelector;
        RerankClient = rerankClient;
        ScoreFusionService = scoreFusionService;
        SearchResultAggregator = searchResultAggregator;
    }

    public async Task<AssetSearchResponseDocument> ExecuteAsync(
        AssetSearchPipelineRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var parameters = ParameterNormalizer.Normalize(
            request.Query,
            request.CandidateTopK,
            request.FinalTopK,
            request.ExpandedCandidateTopK,
            request.RerankTopK);
        var formatResolution = AssetFormatResolver.Resolve(parameters.Query, request.AssetFormat);
        var searchModels = SearchModelOptionsProvider.Current;
        var embeddingModelKey = searchModels.EmbeddingModelKey;

        var records = await VectorRecordRepository.LoadAsync(embeddingModelKey, ct).ConfigureAwait(false);
        EnsureRecordsAvailable(records, "当前没有可检索的素材描述。");

        var filteredRecords = AssetFormatResolver.Filter(records, formatResolution.AssetFormat);
        EnsureRecordsAvailable(filteredRecords, "未找到符合条件的素材。");

        var queryVectorResponse = await QueryEmbeddingClient
            .EmbedQueryAsync(request.BackendBaseUrl, parameters.Query, searchModels, ct)
            .ConfigureAwait(false);
        var vectorRetrieval = await VectorCandidateRetriever
            .RetrieveAsync(
                embeddingModelKey,
                filteredRecords,
                queryVectorResponse.Vector,
                parameters.ExpandedCandidateTopK,
                ct)
            .ConfigureAwait(false);
        EnsureRecordsAvailable(vectorRetrieval.Candidates, "未找到符合条件的素材。");

        var rerankCandidates = RerankCandidateSelector.Select(vectorRetrieval.Candidates, parameters);
        var rerankResponse = await RerankClient
            .RerankAsync(
                request.BackendBaseUrl,
                parameters.Query,
                rerankCandidates,
                rerankCandidates.Count,
                searchModels,
                ct)
            .ConfigureAwait(false);
        var scoredCandidates = ScoreFusionService.Score(rerankCandidates, BuildRerankScoreMap(rerankResponse));
        var aggregatedResults = SearchResultAggregator
            .Aggregate(scoredCandidates, parameters.CandidateTopK, parameters.FinalTopK)
            .ToArray();
        stopwatch.Stop();

        var response = new AssetSearchResponseDocument(
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
            TotalTokenUsage: SumTokenUsage(queryVectorResponse.TokenUsage, rerankResponse.TokenUsage),
            Results: aggregatedResults);
        LogCompletion(response, parameters.RerankTopK);
        return response;
    }

    private static void EnsureRecordsAvailable<T>(IReadOnlyCollection<T> records, string message)
    {
        if (records.Count == 0)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static IReadOnlyDictionary<string, float> BuildRerankScoreMap(RerankResult response) =>
        response.Results
            .Where(result => !string.IsNullOrWhiteSpace(result.CandidateId))
            .GroupBy(result => result.CandidateId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().RerankScore, StringComparer.Ordinal);

    private static int? SumTokenUsage(int? left, int? right) =>
        left is null && right is null ? null : (left ?? 0) + (right ?? 0);

    private static void LogCompletion(AssetSearchResponseDocument response, int rerankTopK)
    {
        Log.Information(
            "素材搜索完成: query={Query}, elapsedMs={ElapsedMs}, tokenUsage={TokenUsage}, candidateTopK={CandidateTopK}, expandedCandidateTopK={ExpandedCandidateTopK}, finalTopK={FinalTopK}, rerankTopK={RerankTopK}, vectorCandidates={VectorCandidates}, rerankCandidates={RerankCandidates}, returned={ReturnedCount}, assetFormatMode={AssetFormatMode}, assetFormat={AssetFormat}, embeddingModel={EmbeddingModel}, rerankModel={RerankModel}, searchStrategy={SearchStrategy}",
            response.Query,
            response.ElapsedMs,
            response.TotalTokenUsage,
            response.CandidateTopK,
            response.ExpandedCandidateTopK,
            response.FinalTopK,
            rerankTopK,
            response.VectorCandidateCount,
            response.RerankCandidateCount,
            response.ReturnedCount,
            response.AssetFormatMode,
            response.AssetFormat ?? "(all)",
            response.EmbeddingModel,
            response.RerankModel,
            response.SearchStrategy);
    }
}
