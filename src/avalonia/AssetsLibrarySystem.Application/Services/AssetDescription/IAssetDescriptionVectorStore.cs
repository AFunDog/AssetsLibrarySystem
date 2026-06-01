using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;

namespace AssetsLibrarySystem.Avalonia.Services.AssetDescription;

public interface IAssetDescriptionVectorStore
{
    string DatabasePath { get; }

    Task SaveAsync(AssetDescriptionVectorDocument document, CancellationToken ct = default);

    Task<AssetDescriptionVectorDocument?> TryGetAsync(string assetId, CancellationToken ct = default);
}
