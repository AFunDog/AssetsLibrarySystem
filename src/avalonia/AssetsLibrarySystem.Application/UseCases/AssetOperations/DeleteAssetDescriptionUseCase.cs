using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.AssetSearch;

namespace AssetsLibrarySystem.Application.UseCases.AssetOperations;

public sealed class DeleteAssetDescriptionUseCase
{
    private IAssetDescriptionStore DescriptionStore { get; }
    private IAssetDescriptionVectorStore VectorStore { get; }
    private IAssetSearchService AssetSearchService { get; }

    public DeleteAssetDescriptionUseCase(
        IAssetDescriptionStore descriptionStore,
        IAssetDescriptionVectorStore vectorStore,
        IAssetSearchService assetSearchService)
    {
        DescriptionStore = descriptionStore;
        VectorStore = vectorStore;
        AssetSearchService = assetSearchService;
    }

    public async Task<DeleteAssetDescriptionResult> ExecuteAsync(
        ManagedAssetRecord asset,
        CancellationToken ct = default)
    {
        var descriptionDeleted = await DescriptionStore.DeleteAsync(asset.DatabaseId, ct).ConfigureAwait(false);
        var vectorDeleted = await VectorStore.DeleteAsync(asset.DatabaseId, ct).ConfigureAwait(false);
        if (descriptionDeleted || vectorDeleted)
        {
            await AssetSearchService.ReindexAsync(ct).ConfigureAwait(false);
        }

        return new DeleteAssetDescriptionResult(descriptionDeleted, vectorDeleted);
    }
}

public sealed record DeleteAssetDescriptionResult(bool DescriptionDeleted, bool VectorDeleted)
{
    public bool DeletedAny => DescriptionDeleted || VectorDeleted;
}
