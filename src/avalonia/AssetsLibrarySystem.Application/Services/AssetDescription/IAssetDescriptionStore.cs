using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;

namespace AssetsLibrarySystem.Avalonia.Services.AssetDescription;

public interface IAssetDescriptionStore
{
    string DatabasePath { get; }

    Task SaveAsync(AssetDescriptionDocument document, CancellationToken ct = default);

    Task<AssetDescriptionDocument?> TryGetAsync(string assetId, CancellationToken ct = default);

    Task<AssetDescriptionDocument?> TryGetForAssetAsync(ManagedAssetRecord asset, CancellationToken ct = default);
}
