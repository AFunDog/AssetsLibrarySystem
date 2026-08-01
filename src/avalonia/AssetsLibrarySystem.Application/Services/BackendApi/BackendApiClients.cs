using System.Threading;
using System.Threading.Tasks;

namespace AssetsLibrarySystem.Application.Services.BackendApi;

public interface IBackendSearchClient
{
    Task<BackendSearchIndexResponse> IndexAsync(
        string backendBaseUrl,
        BackendSearchIndexRequest request,
        CancellationToken ct = default);

    Task<BackendSearchQueryResponse> RerankAsync(
        string backendBaseUrl,
        BackendSearchQueryRequest request,
        CancellationToken ct = default);
}

public interface IBackendModelClient
{
    Task<BackendModelGenerateResponse> GenerateAsync(
        string backendBaseUrl,
        BackendModelGenerateRequest request,
        CancellationToken ct = default);
}
