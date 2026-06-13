using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public interface IAssetDescriptionStore
{
    string DatabasePath { get; }

    Task SaveAsync(AssetDescriptionDocument document, CancellationToken ct = default);

    Task<AssetDescriptionDocument?> TryGetAsync(long assetId, CancellationToken ct = default);

    Task<AssetDescriptionDocument?> TryGetForAssetAsync(ManagedAssetRecord asset, CancellationToken ct = default);

    Task<bool> DeleteAsync(long assetId, CancellationToken ct = default);
}
