using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public interface IAssetTextVectorizationService
{
    Task<IReadOnlyList<AssetDescriptionVectorDocument>> VectorizeAsync(
        AssetDescriptionDocument document,
        string backendBaseUrl,
        string provider,
        string model,
        int embeddingDimensions,
        string embeddingModelKey,
        IReadOnlyDictionary<string, AssetDescriptionVectorDocument>? existingByAngle = null,
        CancellationToken ct = default);
}
