using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetSearch;

namespace AssetsLibrarySystem.Application.UseCases.AssetOperations;

public sealed class RebuildSearchIndexUseCase
{
    private IAssetSearchService AssetSearchService { get; }

    public RebuildSearchIndexUseCase(IAssetSearchService assetSearchService)
    {
        AssetSearchService = assetSearchService;
    }

    public Task<AssetReindexResponseDocument> ExecuteAsync(CancellationToken ct = default)
    {
        return AssetSearchService.ReindexAsync(ct);
    }
}
