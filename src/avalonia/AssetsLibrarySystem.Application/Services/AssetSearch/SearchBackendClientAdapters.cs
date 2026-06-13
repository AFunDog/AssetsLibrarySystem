using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

public sealed class QueryEmbeddingClient : IQueryEmbeddingClient
{
    private IAssetSearchBackendClient BackendClient { get; }

    public QueryEmbeddingClient(IAssetSearchBackendClient backendClient)
    {
        BackendClient = backendClient;
    }

    public Task<QueryEmbeddingResult> EmbedQueryAsync(
        string backendBaseUrl,
        string text,
        SearchModelOptions searchModels,
        CancellationToken ct = default) =>
        BackendClient.EmbedQueryAsync(backendBaseUrl, text, searchModels, ct);
}

public sealed class RerankClient : IRerankClient
{
    private IAssetSearchBackendClient BackendClient { get; }

    public RerankClient(IAssetSearchBackendClient backendClient)
    {
        BackendClient = backendClient;
    }

    public Task<RerankResult> RerankAsync(
        string backendBaseUrl,
        string query,
        IReadOnlyList<VectorCandidateRecord> candidates,
        int rerankTopK,
        SearchModelOptions searchModels,
        CancellationToken ct = default) =>
        BackendClient.RerankAsync(backendBaseUrl, query, candidates, rerankTopK, searchModels, ct);
}

public sealed class SearchModelManagementClient : ISearchModelManagementClient
{
    private IAssetSearchBackendClient BackendClient { get; }

    public SearchModelManagementClient(IAssetSearchBackendClient backendClient)
    {
        BackendClient = backendClient;
    }

    public Task<AssetSearchWarmupDocument> WarmupAsync(
        string backendBaseUrl,
        string modelKind,
        CancellationToken ct = default) =>
        BackendClient.WarmupAsync(backendBaseUrl, modelKind, ct);

    public Task<AssetSearchModelStatusDocument> GetModelStatusAsync(
        string backendBaseUrl,
        CancellationToken ct = default) =>
        BackendClient.GetModelStatusAsync(backendBaseUrl, ct);

    public Task<AssetSearchModelCloseDocument> CloseModelAsync(
        string backendBaseUrl,
        string modelKind,
        CancellationToken ct = default) =>
        BackendClient.CloseModelAsync(backendBaseUrl, modelKind, ct);
}
