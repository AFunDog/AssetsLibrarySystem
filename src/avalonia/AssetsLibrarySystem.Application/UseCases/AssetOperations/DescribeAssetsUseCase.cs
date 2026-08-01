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
        double? rangeStart = null,
        double? rangeEnd = null,
        Func<DescribeAssetProgress, Task>? progress = null,
        CancellationToken ct = default)
    {
        var successCount = 0;
        var failureCount = 0;
        var totalCount = assets.Count;

        for (var index = 0; index < totalCount; index++)
        {
            ct.ThrowIfCancellationRequested();
            var asset = assets[index];

            string? taskId = null;
            try
            {
                await ReportAsync(progress, DescribeAssetProgress.Queued(asset), ct).ConfigureAwait(false);
                var isClip = string.Equals(asset.AssetType, "视频剪辑", StringComparison.Ordinal);
                var taskTitle = isClip ? "剪辑素材描述" : TaskTitle;
                taskId = BackgroundTaskService.BeginTask(taskTitle, $"正在生成素材描述：{asset.Name}", asset.LocalPath);

                // 等待后端期间没有可量化的中间进度：切到不确定进度（进度条动画），避免一直钉在 0%
                BackgroundTaskService.UpdateProgress(taskId, -1);

                AssetDescriptionDocument document;
                if (isClip)
                {
                    document = await DescriptionService
                        .DescribeClipAsync(asset, backendBaseUrl, rangeStart, rangeEnd, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    document = await DescriptionService
                        .DescribeAsync(asset, backendBaseUrl, prompt, systemPrompt, ct)
                        .ConfigureAwait(false);
                }

                successCount++;
                BackgroundTaskService.UpdateProgress(taskId, (double)(index + 1) / totalCount * 100);
                BackgroundTaskService.CompleteTask(taskId, $"描述完成：{asset.Name}", "SQLite 已保存");
                await ReportAsync(progress, DescribeAssetProgress.Completed(asset, document), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failureCount++;
                if (taskId is not null)
                {
                    BackgroundTaskService.FailTask(taskId, ex.Message, $"描述失败：{asset.Name}");
                }

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
