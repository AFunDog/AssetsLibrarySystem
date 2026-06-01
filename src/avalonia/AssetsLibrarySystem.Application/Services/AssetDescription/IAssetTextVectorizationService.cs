using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;

namespace AssetsLibrarySystem.Avalonia.Services.AssetDescription;

public interface IAssetTextVectorizationService
{
    Task<AssetDescriptionVectorDocument> VectorizeAsync(AssetDescriptionDocument document, string backendBaseUrl, CancellationToken ct = default);
}
