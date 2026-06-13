using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;

namespace AssetsLibrarySystem.Application.UseCases.AssetOperations;

public sealed class VectorizeDescriptionsUseCase
{
    private const string TaskTitle = "素材向量化";

    private IAssetDescriptionStore DescriptionStore { get; }
    private IAssetDescriptionVectorStore VectorStore { get; }
    private IAssetTextVectorizationService TextVectorizationService { get; }
    private IAssetSearchService AssetSearchService { get; }
    private ISearchModelOptionsProvider SearchModelOptionsProvider { get; }
    private IBackgroundTaskService BackgroundTaskService { get; }

    public VectorizeDescriptionsUseCase(
        IAssetDescriptionStore descriptionStore,
        IAssetDescriptionVectorStore vectorStore,
        IAssetTextVectorizationService textVectorizationService,
        IAssetSearchService assetSearchService,
        ISearchModelOptionsProvider searchModelOptionsProvider,
        IBackgroundTaskService backgroundTaskService)
    {
        DescriptionStore = descriptionStore;
        VectorStore = vectorStore;
        TextVectorizationService = textVectorizationService;
        AssetSearchService = assetSearchService;
        SearchModelOptionsProvider = searchModelOptionsProvider;
        BackgroundTaskService = backgroundTaskService;
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
        var searchModels = SearchModelOptionsProvider.Current;
        var embeddingModelKey = searchModels.EmbeddingModelKey;
        var taskId = BackgroundTaskService.BeginTask(
            TaskTitle,
            $"正在准备向量化：共 {assets.Count} 个素材",
            $"模型：{embeddingModelKey}");

        try
        {
            for (var index = 0; index < assets.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var asset = assets[index];
                BackgroundTaskService.UpdateTask(
                    taskId,
                    $"正在向量化 {index + 1}/{assets.Count}：{asset.Name}",
                    asset.LocalPath);

                var description = await DescriptionStore.TryGetForAssetAsync(asset, ct).ConfigureAwait(false);
                if (description is null)
                {
                    skipCount++;
                    await ReportAsync(progress, VectorizeDescriptionProgress.Skipped(asset, "尚未生成描述"), ct).ConfigureAwait(false);
                    continue;
                }

                var needsVectorization = await VectorStore.NeedsVectorizationAsync(
                    asset.DatabaseId, embeddingModelKey, description.ContentHash, description.GeneratedAt, ct).ConfigureAwait(false);
                if (!needsVectorization)
                {
                    // 向量已是最新，同步 vector_state 为 'indexed' 以保持 UI 一致
                    await VectorStore.MarkAsIndexedAsync(asset.DatabaseId, ct).ConfigureAwait(false);
                    skipCount++;
                    await ReportAsync(progress, VectorizeDescriptionProgress.Skipped(asset, "向量已是最新"), ct).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    var vectorDocuments = await TextVectorizationService
                        .VectorizeAsync(
                            description,
                            backendBaseUrl,
                            searchModels.EmbeddingProvider,
                            searchModels.EmbeddingModel,
                            searchModels.EmbeddingDimensions,
                            embeddingModelKey,
                            ct)
                        .ConfigureAwait(false);
                    await VectorStore.ReplaceForAssetAsync(asset.DatabaseId, embeddingModelKey, vectorDocuments, ct).ConfigureAwait(false);
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
                BackgroundTaskService.UpdateTask(
                    taskId,
                    "正在刷新本地向量索引",
                    $"新增或更新 {successCount} 个素材向量");
                await AssetSearchService.ReindexAsync(ct).ConfigureAwait(false);
            }

            BackgroundTaskService.CompleteTask(
                taskId,
                "素材向量化完成",
                $"成功 {successCount}，跳过 {skipCount}，失败 {failureCount}");
            return new VectorizeDescriptionsResult(successCount, skipCount, failureCount);
        }
        catch (OperationCanceledException)
        {
            BackgroundTaskService.FailTask(taskId, "任务已取消", "素材向量化取消");
            throw;
        }
        catch (Exception ex)
        {
            BackgroundTaskService.FailTask(taskId, ex.Message, "素材向量化失败");
            throw;
        }
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
