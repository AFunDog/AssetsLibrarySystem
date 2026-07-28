using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.UseCases.AssetOperations;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class AssetDescriptionPanelViewModel : ObservableObject
{
    private BackendStatusViewModel BackendStatus { get; }
    private LibraryWorkspaceViewModel Workspace { get; }
    private DescribeAssetsUseCase? DescribeAssetsUseCase { get; }
    private DeleteAssetDescriptionUseCase? DeleteAssetDescriptionUseCase { get; }
    private ObservableCollection<string> ActivityFeed { get; }

    public AssetDescriptionPanelViewModel()
        : this(new BackendStatusViewModel(), new LibraryWorkspaceViewModel(), null, null, new ActivityFeedService())
    {
    }

    public AssetDescriptionPanelViewModel(
        BackendStatusViewModel backendStatus,
        LibraryWorkspaceViewModel workspace,
        DescribeAssetsUseCase? describeAssetsUseCase,
        DeleteAssetDescriptionUseCase? deleteAssetDescriptionUseCase,
        ActivityFeedService activityFeedService)
    {
        BackendStatus = backendStatus;
        Workspace = workspace;
        DescribeAssetsUseCase = describeAssetsUseCase;
        DeleteAssetDescriptionUseCase = deleteAssetDescriptionUseCase;
        ActivityFeed = activityFeedService.Entries;

        QueueDescriptionsForSelectionCommand = new AsyncRelayCommand(QueueDescriptionsForSelectionAsync);
        QueueSelectedDescriptionCommand = new AsyncRelayCommand(QueueSelectedDescriptionAsync);
        DeleteSelectedDescriptionCommand = new AsyncRelayCommand(DeleteSelectedDescriptionAsync);
    }

    public IAsyncRelayCommand QueueDescriptionsForSelectionCommand { get; }
    public IAsyncRelayCommand QueueSelectedDescriptionCommand { get; }
    public IAsyncRelayCommand DeleteSelectedDescriptionCommand { get; }

    public async Task QueueDescriptionForNodeAsync(AssetLibraryTreeNode? node)
    {
        if (node is null)
        {
            Workspace.SetOperatorNotice("请先选择一个素材库、目录或素材文件。");
            return;
        }

        Workspace.SelectedAssetTreeNode = node;
        var assets = Workspace.GetDescriptionSelectionAssets();
        if (assets.Count == 0)
        {
            Workspace.SetOperatorNotice("当前节点下没有可发送到后端描述的素材。");
            Log.Warning(
                "右键加入描述任务失败：节点下没有素材，nodeName={NodeName}, nodeKind={NodeKind}, path={Path}",
                node.DisplayName,
                node.Kind,
                node.FullPath);
            return;
        }

        if (!BackendStatus.IsBackendReady)
        {
            Workspace.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            Log.Warning("右键加入描述任务失败：后端未就绪，assetCount={AssetCount}", assets.Count);
            return;
        }

        if (DescribeAssetsUseCase is null)
        {
            Workspace.SetOperatorNotice("描述服务未注册，当前无法调用后端。");
            Log.Warning("右键加入描述任务失败：描述服务未注册，assetCount={AssetCount}", assets.Count);
            return;
        }

        Workspace.SetOperatorNotice($"已将 {assets.Count} 个素材排入后端描述任务。");
        ActivityFeed.Insert(0, $"右键描述任务排队：{node.DisplayName}，共 {assets.Count} 个素材");
        Log.Information(
            "用户通过右键菜单加入描述任务: nodeName={NodeName}, nodeKind={NodeKind}, path={Path}, assetCount={AssetCount}",
            node.DisplayName,
            node.Kind,
            node.FullPath,
            assets.Count);

        await DescribeAssetsAsync(assets);
    }

    public async Task DeleteDescriptionForNodeAsync(AssetLibraryTreeNode? node)
    {
        if (node?.Asset is null)
        {
            Workspace.SetOperatorNotice("请右键具体素材文件，再删除它的描述记录。");
            return;
        }

        Workspace.SelectedAssetTreeNode = node;
        await DeleteDescriptionForAssetAsync(node.Asset);
    }

    private async Task QueueDescriptionsForSelectionAsync()
    {
        var assets = Workspace.GetDescriptionSelectionAssets();
        if (assets.Count == 0)
        {
            Workspace.SetOperatorNotice("当前范围内没有可描述的素材。");
            return;
        }

        await DescribeAssetsAsync(assets);
    }

    private async Task QueueSelectedDescriptionAsync()
    {
        if (Workspace.SelectedAsset is not { } asset)
        {
            Workspace.SetOperatorNotice("请先选择一个素材。");
            return;
        }

        await DescribeAssetsAsync([asset]);
    }

    private async Task DescribeAssetsAsync(IReadOnlyList<ManagedAssetRecord> assets)
    {
        if (!BackendStatus.IsBackendReady)
        {
            Workspace.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            return;
        }

        if (DescribeAssetsUseCase is null)
        {
            Workspace.SetOperatorNotice("描述服务未注册，当前无法调用后端。");
            return;
        }

        await DescribeAssetsUseCase.ExecuteAsync(
            assets,
            BackendStatus.BaseUrl,
            progress: progress =>
            {
                if (progress.Kind == DescribeAssetProgressKind.Queued)
                {
                    Workspace.MarkAssetDescriptionQueued(progress.Asset);
                }
                else if (progress.Kind == DescribeAssetProgressKind.Completed && progress.Document is not null)
                {
                    Workspace.CompleteAssetDescription(progress.Asset, progress.Document?.Description ?? "");
                }
                else if (progress.Kind == DescribeAssetProgressKind.Failed && progress.Error is not null)
                {
                    Workspace.FailAssetDescription(progress.Asset, progress.Error.Message);
                }

                return Task.CompletedTask;
            });
    }

    private async Task DeleteSelectedDescriptionAsync()
    {
        var asset = Workspace.SelectedAsset;
        if (asset is null)
        {
            Workspace.SetOperatorNotice("请先选择一个素材，再删除它的描述记录。");
            return;
        }

        await DeleteDescriptionForAssetAsync(asset);
    }

    private async Task DeleteDescriptionForAssetAsync(ManagedAssetRecord asset)
    {
        if (DeleteAssetDescriptionUseCase is null)
        {
            Workspace.SetOperatorNotice("描述删除服务未注册，当前无法删除描述记录。");
            return;
        }

        try
        {
            var result = await DeleteAssetDescriptionUseCase.ExecuteAsync(asset);
            if (!result.DeletedAny)
            {
                Workspace.SetOperatorNotice($"当前素材没有可删除的描述记录：{asset.Name}");
                ActivityFeed.Insert(0, $"描述删除跳过：{asset.Name} 没有记录");
                return;
            }

            Workspace.RemoveAssetDescription(asset, result.VectorDeleted);
            ActivityFeed.Insert(0, $"描述删除完成：{asset.Name}");
        }
        catch (Exception ex)
        {
            Workspace.SetOperatorNotice($"删除描述失败：{ex.Message}");
            ActivityFeed.Insert(0, $"描述删除失败：{asset.Name} -> {ex.Message}");
            Log.Error(ex, "删除素材描述失败: assetUid={AssetUid}, assetName={AssetName}", asset.AssetUid, asset.Name);
        }
    }
}
