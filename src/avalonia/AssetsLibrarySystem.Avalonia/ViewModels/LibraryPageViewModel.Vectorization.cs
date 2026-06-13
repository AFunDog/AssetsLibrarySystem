using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.UseCases.AssetOperations;
using AssetsLibrarySystem.Avalonia.Models;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class LibraryPageViewModel
{
    public async Task VectorizeDescriptionsForNodeAsync(AssetLibraryTreeNode? node)
    {
        if (node is null)
        {
            LibraryCatalogService.SetOperatorNotice("请先右键选择一个素材库、目录或素材文件。");
            return;
        }

        LibraryCatalogService.SelectedAssetTreeNode = node;
        var assets = LibraryCatalogService.GetDescriptionSelectionAssets();
        if (assets.Count == 0)
        {
            LibraryCatalogService.SetOperatorNotice("当前节点下没有可向量化的素材。");
            Log.Warning(
                "右键向量化失败：节点下没有素材，nodeName={NodeName}, nodeKind={NodeKind}, path={Path}",
                node.DisplayName,
                node.Kind,
                node.FullPath);
            return;
        }

        var scopeName = node.Kind == AssetLibraryTreeNodeKind.File
            ? $"当前素材“{node.DisplayName}”"
            : $"当前文件夹“{node.DisplayName}”";

        await VectorizeAssetsAsync(assets, scopeName, "右键向量化");
    }

    private async Task VectorizeAssetsAsync(
        IReadOnlyList<ManagedAssetRecord> assets,
        string scopeName,
        string activityPrefix)
    {
        if (!BackendSessionService.IsBackendReady)
        {
            LibraryCatalogService.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            Log.Warning("{ActivityPrefix}失败：后端未就绪，assetCount={AssetCount}", activityPrefix, assets.Count);
            return;
        }

        if (VectorizeDescriptionsUseCase is null)
        {
            LibraryCatalogService.SetOperatorNotice("向量化服务未注册，当前无法执行向量化。");
            Log.Warning("{ActivityPrefix}失败：向量化服务未注册，assetCount={AssetCount}", activityPrefix, assets.Count);
            return;
        }

        LibraryCatalogService.SetOperatorNotice($"正在增量向量化{scopeName}：{assets.Count} 个素材");
        ActivityFeed.Insert(0, $"{activityPrefix}开始：{scopeName}，共 {assets.Count} 个素材");
        Log.Information(
            "用户触发节点级向量化: scopeName={ScopeName}, assetCount={AssetCount}",
            scopeName,
            assets.Count);

        try
        {
            var result = await VectorizeDescriptionsUseCase.ExecuteAsync(
                assets,
                BackendSessionService.BaseUrl,
                progress =>
                {
                    if (progress.Kind == VectorizeDescriptionProgressKind.Completed)
                    {
                        LibraryCatalogService.MarkAssetVectorized(progress.Asset);
                    }

                    return Task.CompletedTask;
                });

            LibraryCatalogService.RefreshMetrics();
            LibraryCatalogService.SetOperatorNotice(
                $"{scopeName}向量化完成：成功 {result.SuccessCount}，跳过 {result.SkipCount}，失败 {result.FailureCount}。");
            ActivityFeed.Insert(0, $"{activityPrefix}完成：{scopeName}，成功 {result.SuccessCount}，跳过 {result.SkipCount}，失败 {result.FailureCount}");
            Log.Information(
                "节点级向量化完成: scopeName={ScopeName}, success={SuccessCount}, skipped={SkipCount}, failed={FailureCount}",
                scopeName,
                result.SuccessCount,
                result.SkipCount,
                result.FailureCount);
        }
        catch (Exception ex)
        {
            LibraryCatalogService.SetOperatorNotice($"{scopeName}向量化失败：{ex.Message}");
            ActivityFeed.Insert(0, $"{activityPrefix}失败：{scopeName} -> {ex.Message}");
            Log.Error(ex, "节点级向量化失败: scopeName={ScopeName}, assetCount={AssetCount}", scopeName, assets.Count);
        }
    }
}
