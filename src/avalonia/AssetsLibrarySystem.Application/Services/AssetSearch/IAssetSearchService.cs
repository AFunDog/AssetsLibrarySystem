using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

public interface IAssetSearchService
{
    Task<AssetSearchResponseDocument> SearchAsync(
        string backendBaseUrl,
        string query,
        int candidateTopK = 20,
        int finalTopK = 5,
        string? assetFormat = null,
        int expandedCandidateTopK = 160,
        int rerankTopK = 50,
        CancellationToken ct = default);

    Task<AssetReindexResponseDocument> ReindexAsync(
        CancellationToken ct = default);
}