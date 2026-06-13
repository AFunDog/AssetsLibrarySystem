using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.BackendApi;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

public sealed class QueryEmbeddingClient : IQueryEmbeddingClient
{
    private IBackendSearchClient BackendSearchClient { get; }

    public QueryEmbeddingClient(IBackendSearchClient backendSearchClient)
    {
        BackendSearchClient = backendSearchClient;
    }

    public async Task<QueryEmbeddingResult> EmbedQueryAsync(
        string backendBaseUrl,
        string text,
        SearchModelOptions searchModels,
        CancellationToken ct = default)
    {
        var request = new BackendSearchIndexRequest(
            Provider: searchModels.EmbeddingProvider,
            Model: searchModels.EmbeddingModel,
            EmbeddingDimensions: searchModels.IsDashScopeEmbeddingProvider ? searchModels.EmbeddingDimensions : null,
            AssetId: "__query__",
            AssetName: "__query__",
            AssetFormat: "文本",
            AssetPath: Environment.SystemDirectory,
            Description: text,
            GeneratedAt: null);
        var response = await BackendSearchClient.IndexAsync(backendBaseUrl, request, ct).ConfigureAwait(false);
        return new QueryEmbeddingResult(
            JsonSerializer.Deserialize<float[]>(response.Vector.GetRawText()) ?? [],
            response.EmbeddingModel,
            response.TokenUsage);
    }
}

public sealed class RerankClient : IRerankClient
{
    private IBackendSearchClient BackendSearchClient { get; }

    public RerankClient(IBackendSearchClient backendSearchClient)
    {
        BackendSearchClient = backendSearchClient;
    }

    public async Task<RerankResult> RerankAsync(
        string backendBaseUrl,
        string query,
        IReadOnlyList<VectorCandidateRecord> candidates,
        int rerankTopK,
        SearchModelOptions searchModels,
        CancellationToken ct = default)
    {
        var request = new BackendSearchQueryRequest(
            Provider: searchModels.RerankProvider,
            Model: searchModels.RerankModel,
            Query: query,
            Candidates: candidates.Select(candidate => new BackendSearchQueryCandidate(
                CandidateId: candidate.CandidateId,
                AssetId: candidate.Record.AssetUid,
                AssetName: candidate.Record.AssetName,
                AssetFormat: candidate.Record.AssetType,
                AssetPath: candidate.Record.AssetPath,
                Description: candidate.Record.SegmentText,
                Tags: candidate.Record.Tags,
                GeneratedAt: candidate.Record.GeneratedAt)).ToArray(),
            FinalTopK: Math.Min(rerankTopK, candidates.Count));
        var response = await BackendSearchClient.RerankAsync(backendBaseUrl, request, ct).ConfigureAwait(false);
        return new RerankResult(
            response.RerankModel,
            response.Results.Select(item => new SearchRerankScore(item.CandidateId, item.RerankScore)).ToArray(),
            response.TokenUsage);
    }
}

public sealed class SearchModelManagementClient : ISearchModelManagementClient
{
    private IBackendModelClient BackendModelClient { get; }

    public SearchModelManagementClient(IBackendModelClient backendModelClient)
    {
        BackendModelClient = backendModelClient;
    }

    public async Task<AssetSearchWarmupDocument> WarmupAsync(
        string backendBaseUrl,
        string modelKind,
        CancellationToken ct = default)
    {
        var response = await BackendModelClient.WarmupAsync(backendBaseUrl, modelKind, ct).ConfigureAwait(false);
        return new AssetSearchWarmupDocument(response.ModelKind, response.ModelName, response.Device, response.Warmed);
    }

    public async Task<AssetSearchModelStatusDocument> GetModelStatusAsync(
        string backendBaseUrl,
        CancellationToken ct = default)
    {
        var response = await BackendModelClient.GetStatusAsync(backendBaseUrl, ct).ConfigureAwait(false);
        return new AssetSearchModelStatusDocument(
            response.EmbeddingModelName,
            response.RerankModelName,
            response.Device,
            response.LoadedModelKinds.ToArray(),
            response.EmbeddingLoaded,
            response.RerankLoaded,
            response.LoadedCount);
    }

    public async Task<AssetSearchModelCloseDocument> CloseModelAsync(
        string backendBaseUrl,
        string modelKind,
        CancellationToken ct = default)
    {
        var response = await BackendModelClient
            .CloseAsync(backendBaseUrl, new BackendModelCloseRequest(modelKind), ct)
            .ConfigureAwait(false);
        return new AssetSearchModelCloseDocument(
            response.ModelKind,
            response.ModelName,
            response.Device,
            response.Closed,
            response.CudaCacheCleared,
            response.RemainingLoadedModels.ToArray());
    }
}
