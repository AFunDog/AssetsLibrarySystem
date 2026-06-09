using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;

namespace AssetsLibrarySystem.Application.UseCases.AssetOperations;

public sealed class DescribeAssetsUseCase
{
    private const string TaskTitle = "素材描述";

    private IAssetDescriptionService DescriptionService { get; }
    private IBackgroundTaskService BackgroundTaskService { get; }

    public DescribeAssetsUseCase(
        IAssetDescriptionService descriptionService,
        IBackgroundTaskService backgroundTaskService)
    {
        DescriptionService = descriptionService;
        BackgroundTaskService = backgroundTaskService;
    }

    public async Task<DescribeAssetsResult> ExecuteAsync(
        IReadOnlyList<ManagedAssetRecord> assets,
        string backendBaseUrl,
        string? prompt = null,
        string? systemPrompt = null,
        Func<DescribeAssetProgress, Task>? progress = null,
        CancellationToken ct = default)
    {
        var successCount = 0;
        var failureCount = 0;

        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();

            await ReportAsync(progress, DescribeAssetProgress.Queued(asset), ct).ConfigureAwait(false);
            var taskId = BackgroundTaskService.BeginTask(TaskTitle, $"正在生成素材描述：{asset.Name}", asset.LocalPath);

            try
            {
                var document = await DescriptionService
                    .DescribeAsync(asset, backendBaseUrl, prompt, systemPrompt, ct)
                    .ConfigureAwait(false);

                successCount++;
                BackgroundTaskService.CompleteTask(taskId, $"描述完成：{asset.Name}", "SQLite 已保存");
                await ReportAsync(progress, DescribeAssetProgress.Completed(asset, document), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failureCount++;
                BackgroundTaskService.FailTask(taskId, ex.Message, $"描述失败：{asset.Name}");
                await ReportAsync(progress, DescribeAssetProgress.Failed(asset, ex), ct).ConfigureAwait(false);
            }
        }

        return new DescribeAssetsResult(successCount, failureCount);
    }

    private static Task ReportAsync(
        Func<DescribeAssetProgress, Task>? progress,
        DescribeAssetProgress value,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return progress?.Invoke(value) ?? Task.CompletedTask;
    }
}

public sealed record DescribeAssetsResult(int SuccessCount, int FailureCount);

public sealed record DescribeAssetProgress(
    ManagedAssetRecord Asset,
    DescribeAssetProgressKind Kind,
    AssetDescriptionDocument? Document = null,
    Exception? Error = null)
{
    public static DescribeAssetProgress Queued(ManagedAssetRecord asset)
    {
        return new DescribeAssetProgress(asset, DescribeAssetProgressKind.Queued);
    }

    public static DescribeAssetProgress Completed(ManagedAssetRecord asset, AssetDescriptionDocument document)
    {
        return new DescribeAssetProgress(asset, DescribeAssetProgressKind.Completed, document);
    }

    public static DescribeAssetProgress Failed(ManagedAssetRecord asset, Exception error)
    {
        return new DescribeAssetProgress(asset, DescribeAssetProgressKind.Failed, Error: error);
    }
}

public enum DescribeAssetProgressKind
{
    Queued,
    Completed,
    Failed
}
