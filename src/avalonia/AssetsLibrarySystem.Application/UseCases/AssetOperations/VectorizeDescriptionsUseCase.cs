using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.AssetSearch;

namespace AssetsLibrarySystem.Application.UseCases.AssetOperations;

public sealed class VectorizeDescriptionsUseCase
{
    private IAssetDescriptionStore DescriptionStore { get; }
    private IAssetDescriptionVectorStore VectorStore { get; }
    private IAssetTextVectorizationService TextVectorizationService { get; }
    private IAssetSearchService AssetSearchService { get; }

    public VectorizeDescriptionsUseCase(
        IAssetDescriptionStore descriptionStore,
        IAssetDescriptionVectorStore vectorStore,
        IAssetTextVectorizationService textVectorizationService,
        IAssetSearchService assetSearchService)
    {
        DescriptionStore = descriptionStore;
        VectorStore = vectorStore;
        TextVectorizationService = textVectorizationService;
        AssetSearchService = assetSearchService;
    }

    public async Task<VectorizeDescriptionsResult> ExecuteAsync(
        IReadOnlyList<ManagedAssetRecord> assets,
        string backendBaseUrl,
        Func<VectorizeDescriptionProgress, Task>? progress = null,
        CancellationToken ct = default)
    {
        var successCount = 0;
        var skipCount = 0;
        var failureCount = 0;

        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();

            var description = await DescriptionStore.TryGetForAssetAsync(asset, ct).ConfigureAwait(false);
            if (description is null)
            {
                skipCount++;
                await ReportAsync(progress, VectorizeDescriptionProgress.Skipped(asset, "尚未生成描述"), ct).ConfigureAwait(false);
                continue;
            }

            var existingVectors = await VectorStore.ListByAssetIdAsync(asset.Id, ct).ConfigureAwait(false);
            if (existingVectors.Count > 0 && !ShouldRefreshVectors(description, existingVectors))
            {
                skipCount++;
                await ReportAsync(progress, VectorizeDescriptionProgress.Skipped(asset, "向量已是最新"), ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                var vectorDocuments = await TextVectorizationService
                    .VectorizeAsync(description, backendBaseUrl, ct)
                    .ConfigureAwait(false);
                await VectorStore.ReplaceForAssetAsync(asset.Id, vectorDocuments, ct).ConfigureAwait(false);
                successCount++;
                await ReportAsync(progress, VectorizeDescriptionProgress.Completed(asset, vectorDocuments), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failureCount++;
                await ReportAsync(progress, VectorizeDescriptionProgress.Failed(asset, ex), ct).ConfigureAwait(false);
            }
        }

        if (successCount > 0)
        {
            await AssetSearchService.ReindexAsync(ct).ConfigureAwait(false);
        }

        return new VectorizeDescriptionsResult(successCount, skipCount, failureCount);
    }

    private static Task ReportAsync(
        Func<VectorizeDescriptionProgress, Task>? progress,
        VectorizeDescriptionProgress value,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return progress?.Invoke(value) ?? Task.CompletedTask;
    }

    private static bool ShouldRefreshVectors(
        AssetDescriptionDocument description,
        IReadOnlyList<AssetDescriptionVectorDocument> vectorDocuments)
    {
        var segments = StructuredDescriptionHelper.ExtractSegments(description.Description);
        if (segments.Count != vectorDocuments.Count)
        {
            return true;
        }

        var expectedAngleTypes = new HashSet<string>(
            segments.Select(segment => segment.NormalizedAngleType),
            StringComparer.Ordinal);

        foreach (var vectorDocument in vectorDocuments)
        {
            var sameContentHash = string.Equals(
                description.ContentHash ?? string.Empty,
                vectorDocument.ContentHash ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            if (!sameContentHash)
            {
                return true;
            }

            if (vectorDocument.VectorizedAt < description.GeneratedAt)
            {
                return true;
            }

            if (!expectedAngleTypes.Remove(vectorDocument.AngleType))
            {
                return true;
            }
        }

        return expectedAngleTypes.Count != 0;
    }
}

public sealed record VectorizeDescriptionsResult(int SuccessCount, int SkipCount, int FailureCount);

public sealed record VectorizeDescriptionProgress(
    ManagedAssetRecord Asset,
    VectorizeDescriptionProgressKind Kind,
    string? SkipReason = null,
    IReadOnlyList<AssetDescriptionVectorDocument>? VectorDocuments = null,
    Exception? Error = null)
{
    public static VectorizeDescriptionProgress Skipped(ManagedAssetRecord asset, string reason)
    {
        return new VectorizeDescriptionProgress(asset, VectorizeDescriptionProgressKind.Skipped, reason);
    }

    public static VectorizeDescriptionProgress Completed(ManagedAssetRecord asset, IReadOnlyList<AssetDescriptionVectorDocument> documents)
    {
        return new VectorizeDescriptionProgress(asset, VectorizeDescriptionProgressKind.Completed, VectorDocuments: documents);
    }

    public static VectorizeDescriptionProgress Failed(ManagedAssetRecord asset, Exception error)
    {
        return new VectorizeDescriptionProgress(asset, VectorizeDescriptionProgressKind.Failed, Error: error);
    }
}

public enum VectorizeDescriptionProgressKind
{
    Skipped,
    Completed,
    Failed
}
