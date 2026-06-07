using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;

namespace AssetsLibrarySystem.Application.UseCases.AssetOperations;

public sealed class DeleteAssetDescriptionUseCase
{
    private IAssetDescriptionStore DescriptionStore { get; }
    private IAssetDescriptionVectorStore VectorStore { get; }

    public DeleteAssetDescriptionUseCase(
        IAssetDescriptionStore descriptionStore,
        IAssetDescriptionVectorStore vectorStore)
    {
        DescriptionStore = descriptionStore;
        VectorStore = vectorStore;
    }

    public async Task<DeleteAssetDescriptionResult> ExecuteAsync(
        ManagedAssetRecord asset,
        CancellationToken ct = default)
    {
        var descriptionDeleted = await DescriptionStore.DeleteAsync(asset.AssetUid, ct).ConfigureAwait(false);
        var vectorDeleted = await VectorStore.DeleteAsync(asset.AssetUid, ct).ConfigureAwait(false);
        return new DeleteAssetDescriptionResult(descriptionDeleted, vectorDeleted);
    }
}

public sealed record DeleteAssetDescriptionResult(bool DescriptionDeleted, bool VectorDeleted)
{
    public bool DeletedAny => DescriptionDeleted || VectorDeleted;
}
