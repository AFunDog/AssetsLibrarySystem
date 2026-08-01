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
                BackgroundTaskService.UpdateProgress(taskId, 0);

                // 合并调用方取消令牌与任务取消按钮令牌
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    ct, BackgroundTaskService.GetCancellationToken(taskId));
                var taskCt = linkedCts.Token;

                AssetDescriptionDocument document;
                if (isClip)
                {
                    // 剪辑素材：分割阶段透传进度百分比，描述（LLM）阶段无细分进度
                    document = await DescriptionService
                        .DescribeClipAsync(asset, backendBaseUrl, rangeStart, rangeEnd, taskCt,
                            progress: percent => BackgroundTaskService.UpdateProgress(taskId, percent))
                        .ConfigureAwait(false);
                }
                else
                {
                    document = await DescriptionService
                        .DescribeAsync(asset, backendBaseUrl, prompt, systemPrompt, taskCt)
                        .ConfigureAwait(false);
                }

                successCount++;
                BackgroundTaskService.UpdateProgress(taskId, (double)(index + 1) / totalCount * 100);
                BackgroundTaskService.CompleteTask(taskId, $"描述完成：{asset.Name}", "SQLite 已保存");
                await ReportAsync(progress, DescribeAssetProgress.Completed(asset, document), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 任务被取消（调用方或取消按钮）：标记状态后重新抛出，由调用方提示
                if (taskId is not null)
                {
                    BackgroundTaskService.FailTask(taskId, "任务已取消", $"描述已取消：{asset.Name}");
                }

                throw;
            }
            catch (Exception ex)
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
