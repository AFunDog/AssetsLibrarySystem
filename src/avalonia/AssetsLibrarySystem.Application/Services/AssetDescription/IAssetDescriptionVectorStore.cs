using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public interface IAssetDescriptionVectorStore
{
    string DatabasePath { get; }

    Task ReplaceForAssetAsync(string assetId, IReadOnlyList<AssetDescriptionVectorDocument> documents, CancellationToken ct = default);

    Task<IReadOnlyList<AssetDescriptionVectorDocument>> ListByAssetIdAsync(string assetId, CancellationToken ct = default);

    Task<bool> DeleteAsync(string assetId, CancellationToken ct = default);
}
