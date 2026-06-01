using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;

namespace AssetsLibrarySystem.Avalonia.Services.AssetDescription;

public interface IAssetDescriptionService
{
    Task<AssetDescriptionDocument> DescribeAsync(
        ManagedAssetRecord asset,
        string backendBaseUrl,
        string? prompt,
        string? systemPrompt,
        CancellationToken ct = default);
}
