using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public interface IAssetTextVectorizationService
{
    Task<IReadOnlyList<AssetDescriptionVectorDocument>> VectorizeAsync(AssetDescriptionDocument document, string backendBaseUrl, CancellationToken ct = default);
}
