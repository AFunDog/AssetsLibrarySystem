using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.UseCases.AssetOperations;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class AssetVectorizationPanelViewModel : ObservableObject
{
    private BackendSessionService BackendSessionService { get; }
    private LibraryCatalogService LibraryCatalogService { get; }
    private VectorizeDescriptionsUseCase? VectorizeDescriptionsUseCase { get; }
    private ObservableCollection<string> ActivityFeed { get; }

    public AssetVectorizationPanelViewModel()
        : this(new BackendSessionService(), new LibraryCatalogService(), null, new ActivityFeedService())
    {
    }

    public AssetVectorizationPanelViewModel(
        BackendSessionService backendSessionService,
        LibraryCatalogService libraryCatalogService,
        VectorizeDescriptionsUseCase? vectorizeDescriptionsUseCase,
        ActivityFeedService activityFeedService)
    {
        BackendSessionService = backendSessionService;
        LibraryCatalogService = libraryCatalogService;
        VectorizeDescriptionsUseCase = vectorizeDescriptionsUseCase;
        ActivityFeed = activityFeedService.Entries;

        VectorizeDescriptionsCommand = new AsyncRelayCommand(VectorizeDescriptionsAsync);
    }

    public IAsyncRelayCommand VectorizeDescriptionsCommand { get; }

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

    private async Task VectorizeDescriptionsAsync()
    {
        var assets = LibraryCatalogService.GetAllLibraryAssets();
        if (assets.Count == 0)
        {
            LibraryCatalogService.SetOperatorNotice("当前没有可向量化的素材。");
            return;
        }

        await VectorizeAssetsAsync(assets, "全部素材库", "批量向量化");
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
            "用户触发向量化: activityPrefix={ActivityPrefix}, scopeName={ScopeName}, assetCount={AssetCount}",
            activityPrefix,
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
                "向量化完成: activityPrefix={ActivityPrefix}, scopeName={ScopeName}, success={SuccessCount}, skipped={SkipCount}, failed={FailureCount}",
                activityPrefix,
                scopeName,
                result.SuccessCount,
                result.SkipCount,
                result.FailureCount);
        }
        catch (Exception ex)
        {
            LibraryCatalogService.SetOperatorNotice($"{scopeName}向量化失败：{ex.Message}");
            ActivityFeed.Insert(0, $"{activityPrefix}失败：{scopeName} -> {ex.Message}");
            Log.Error(ex, "向量化失败: activityPrefix={ActivityPrefix}, scopeName={ScopeName}, assetCount={AssetCount}", activityPrefix, scopeName, assets.Count);
        }
    }
}
