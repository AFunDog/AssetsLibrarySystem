using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;

namespace AssetsLibrarySystem.Avalonia.Services.AssetSearch;

public interface IAssetSearchService
{
    Task<AssetSearchResponseDocument> SearchAsync(
        string backendBaseUrl,
        string query,
        int candidateTopK = 20,
        int finalTopK = 5,
        string? assetFormat = null,
        CancellationToken ct = default);
}
