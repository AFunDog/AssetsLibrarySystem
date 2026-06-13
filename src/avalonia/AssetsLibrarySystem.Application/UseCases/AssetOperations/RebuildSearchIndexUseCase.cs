using System;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;

namespace AssetsLibrarySystem.Application.UseCases.AssetOperations;

public sealed class RebuildSearchIndexUseCase
{
    private IAssetSearchService AssetSearchService { get; }
    private IBackgroundTaskService BackgroundTaskService { get; }

    public RebuildSearchIndexUseCase(
        IAssetSearchService assetSearchService,
        IBackgroundTaskService backgroundTaskService)
    {
        AssetSearchService = assetSearchService;
        BackgroundTaskService = backgroundTaskService;
    }

    public async Task<AssetReindexResponseDocument> ExecuteAsync(CancellationToken ct = default)
    {
        var taskId = BackgroundTaskService.BeginTask(
            "向量索引",
            "正在重建本地向量索引",
            "从 SQLite 向量表刷新本地 HNSW 索引与元数据");

        try
        {
            var response = await AssetSearchService.ReindexAsync(ct).ConfigureAwait(false);
            BackgroundTaskService.CompleteTask(
                taskId,
                "向量索引重建完成",
                $"共 {response.DocumentCount} 条，{response.VectorDim} 维");
            return response;
        }
        catch (OperationCanceledException)
        {
            BackgroundTaskService.FailTask(taskId, "任务已取消", "向量索引重建取消");
            throw;
        }
        catch (Exception ex)
        {
            BackgroundTaskService.FailTask(taskId, ex.Message, "向量索引重建失败");
            throw;
        }
    }
}
