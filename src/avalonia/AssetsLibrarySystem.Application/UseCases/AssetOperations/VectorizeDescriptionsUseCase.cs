using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;

namespace AssetsLibrarySystem.Application.UseCases.AssetOperations;

public sealed class VectorizeDescriptionsUseCase
{
    private IAssetDescriptionStore DescriptionStore { get; }
    private IAssetDescriptionVectorStore VectorStore { get; }
    private IAssetTextVectorizationService TextVectorizationService { get; }

    public VectorizeDescriptionsUseCase(
        IAssetDescriptionStore descriptionStore,
        IAssetDescriptionVectorStore vectorStore,
        IAssetTextVectorizationService textVectorizationService)
    {
        DescriptionStore = descriptionStore;
        VectorStore = vectorStore;
        TextVectorizationService = textVectorizationService;
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

            if (await VectorStore.TryGetAsync(asset.Id, ct).ConfigureAwait(false) is not null)
            {
                skipCount++;
                await ReportAsync(progress, VectorizeDescriptionProgress.Skipped(asset, "向量已存在"), ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                var vectorDocument = await TextVectorizationService
                    .VectorizeAsync(description, backendBaseUrl, ct)
                    .ConfigureAwait(false);
                await VectorStore.SaveAsync(vectorDocument, ct).ConfigureAwait(false);
                successCount++;
                await ReportAsync(progress, VectorizeDescriptionProgress.Completed(asset, vectorDocument), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failureCount++;
                await ReportAsync(progress, VectorizeDescriptionProgress.Failed(asset, ex), ct).ConfigureAwait(false);
            }
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
}

public sealed record VectorizeDescriptionsResult(int SuccessCount, int SkipCount, int FailureCount);

public sealed record VectorizeDescriptionProgress(
    ManagedAssetRecord Asset,
    VectorizeDescriptionProgressKind Kind,
    string? SkipReason = null,
    AssetDescriptionVectorDocument? VectorDocument = null,
    Exception? Error = null)
{
    public static VectorizeDescriptionProgress Skipped(ManagedAssetRecord asset, string reason)
    {
        return new VectorizeDescriptionProgress(asset, VectorizeDescriptionProgressKind.Skipped, reason);
    }

    public static VectorizeDescriptionProgress Completed(ManagedAssetRecord asset, AssetDescriptionVectorDocument document)
    {
        return new VectorizeDescriptionProgress(asset, VectorizeDescriptionProgressKind.Completed, VectorDocument: document);
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
