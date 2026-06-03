using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public interface IAssetDescriptionVectorStore
{
    string DatabasePath { get; }

    Task SaveAsync(AssetDescriptionVectorDocument document, CancellationToken ct = default);

    Task<AssetDescriptionVectorDocument?> TryGetAsync(string assetId, CancellationToken ct = default);
}
