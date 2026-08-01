using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;

namespace AssetsLibrarySystem.Application.UseCases.AssetOperations;

/// <summary>
/// 仅执行场景分割：对剪辑素材做场景检测并保存片段时间点骨架（不调用 LLM）。
/// 已分割素材幂等跳过；支持指定时间范围（秒）。
/// </summary>
public sealed class SplitClipSegmentsUseCase
{
    private const string TaskTitle = "素材分割";

    private IAssetDescriptionService DescriptionService { get; }
    private IBackgroundTaskService BackgroundTaskService { get; }

    public SplitClipSegmentsUseCase(
        IAssetDescriptionService descriptionService,
        IBackgroundTaskService backgroundTaskService)
    {
        DescriptionService = descriptionService;
        BackgroundTaskService = backgroundTaskService;
    }

    public async Task<SplitClipSegmentsResult> ExecuteAsync(
        IReadOnlyList<ManagedAssetRecord> assets,
        string backendBaseUrl,
        double? rangeStart = null,
        double? rangeEnd = null,
        Func<SplitClipProgress, Task>? progress = null,
        CancellationToken ct = default)
    {
        var successCount = 0;
        var skipCount = 0;
        var failureCount = 0;
        var totalCount = assets.Count;

        for (var index = 0; index < totalCount; index++)
        {
            ct.ThrowIfCancellationRequested();
            var asset = assets[index];

            await ReportAsync(progress, SplitClipProgress.Queued(asset), ct).ConfigureAwait(false);
            var taskId = BackgroundTaskService.BeginTask(TaskTitle, $"正在场景分割：{asset.Name}", asset.LocalPath);
            BackgroundTaskService.UpdateProgress(taskId, (double)index / totalCount * 100);

            try
            {
                var result = await DescriptionService
                    .SplitOnlyAsync(asset, backendBaseUrl, rangeStart, rangeEnd, ct)
                    .ConfigureAwait(false);

                if (result.AlreadySplit)
                {
                    skipCount++;
                    BackgroundTaskService.CompleteTask(taskId, $"已存在分割结果：{asset.Name}", $"片段数 {result.SegmentCount}");
                    await ReportAsync(progress, SplitClipProgress.Skipped(asset, result.SegmentCount), ct).ConfigureAwait(false);
                }
                else
                {
                    successCount++;
                    BackgroundTaskService.UpdateProgress(taskId, (double)(index + 1) / totalCount * 100);
                    BackgroundTaskService.CompleteTask(taskId, $"分割完成：{asset.Name}", $"已保存 {result.SegmentCount} 个片段时间点");
                    await ReportAsync(progress, SplitClipProgress.Completed(asset, result.SegmentCount), ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failureCount++;
                BackgroundTaskService.FailTask(taskId, ex.Message, $"分割失败：{asset.Name}");
                await ReportAsync(progress, SplitClipProgress.Failed(asset, ex), ct).ConfigureAwait(false);
            }
        }

        return new SplitClipSegmentsResult(successCount, skipCount, failureCount);
    }

    private static Task ReportAsync(
        Func<SplitClipProgress, Task>? progress,
        SplitClipProgress value,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return progress?.Invoke(value) ?? Task.CompletedTask;
    }
}

public sealed record SplitClipSegmentsResult(int SuccessCount, int SkipCount, int FailureCount);

public sealed record SplitClipProgress(
    ManagedAssetRecord Asset,
    SplitClipProgressKind Kind,
    int? SegmentCount = null,
    Exception? Error = null)
{
    public static SplitClipProgress Queued(ManagedAssetRecord asset) =>
        new(asset, SplitClipProgressKind.Queued);

    public static SplitClipProgress Completed(ManagedAssetRecord asset, int segmentCount) =>
        new(asset, SplitClipProgressKind.Completed, segmentCount);

    public static SplitClipProgress Skipped(ManagedAssetRecord asset, int segmentCount) =>
        new(asset, SplitClipProgressKind.Skipped, segmentCount);

    public static SplitClipProgress Failed(ManagedAssetRecord asset, Exception error) =>
        new(asset, SplitClipProgressKind.Failed, Error: error);
}

public enum SplitClipProgressKind
{
    Queued,
    Completed,
    Skipped,
    Failed
}
