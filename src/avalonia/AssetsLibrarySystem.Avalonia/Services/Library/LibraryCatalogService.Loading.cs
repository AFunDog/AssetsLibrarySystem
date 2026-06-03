using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.Services.Library;

public sealed partial class LibraryCatalogService
{
    private async Task LoadLibrariesAsync()
    {
        Log.Information("开始加载素材库列表。");
        Libraries.Clear();
        AssetTreeRoots.Clear();
        AllAssets.Clear();

        if (AssetLibraryService is null)
        {
            SetEmptyWorkspaceState();
            Log.Warning("素材库服务未注册，无法加载素材库列表。");
            return;
        }

        var libraries = await AssetLibraryService.GetLibrariesAsync();
        foreach (var library in libraries)
        {
            Libraries.Add(library);
        }

        RebuildAssetTree();
        RebuildMetrics();

        if (Libraries.Count == 0)
        {
            SetEmptyWorkspaceState();
            ActivityFeedService.Add("当前尚未登记素材库目录。");
            Log.Information("素材库列表为空。");
            return;
        }

        var firstLibrary = Libraries[0];
        SuppressLibrarySelectionLoad = true;
        SelectedLibrary = firstLibrary;
        SuppressLibrarySelectionLoad = false;

        WorkspaceTitle = firstLibrary.Name;
        WorkspaceSummary = firstLibrary.RootPath;
        AssetSummary = "素材库目录已载入，素材文件正在后台异步加载。";
        OperatorNotice = "素材库列表已加载完成，文件数据正在后台同步。";
        SelectedAssetTreeNode = null;
        UpdateExplorerView(null);

        _ = LoadAllLibraryDataAsync();
    }

    private Task LoadSelectedLibraryAsync(LibraryWorkspace? library)
    {
        if (library is null)
        {
            SetEmptyWorkspaceState();
            return Task.CompletedTask;
        }

        Log.Debug("切换当前素材库: libraryId={LibraryId}, libraryName={LibraryName}", library.Id, library.Name);
        WorkspaceTitle = library.Name;
        WorkspaceSummary = library.RootPath;
        AssetSummary = library.AssetCount > 0
            ? $"当前素材库已加载 {library.AssetCount} 个支持的素材文件。"
            : library.Summary;

        return Task.CompletedTask;
    }

    private async Task LoadAllLibraryDataAsync()
    {
        if (AssetLibraryService is null || Libraries.Count == 0)
        {
            return;
        }

        var taskId = BackgroundTaskService?.BeginTask("素材库加载", "正在加载素材库数据");
        try
        {
            Log.Information("开始后台加载全部素材库数据，libraryCount={LibraryCount}", Libraries.Count);
            OperatorNotice = "正在后台异步加载全部素材库文件数据...";

            foreach (var library in Libraries.ToList())
            {
                UpdateTask(taskId, $"正在加载素材库：{library.Name}", library.RootPath);
                var assets = await AssetLibraryService.ScanLibraryAsync(library);
                Log.Information("素材库加载完成: libraryId={LibraryId}, libraryName={LibraryName}, assetCount={AssetCount}", library.Id, library.Name, assets.Count);

                AllAssets.RemoveAll(asset => asset.LibraryName == library.Name);
                AllAssets.AddRange(assets);

                library.AssetCount = assets.Count;
                library.SyncMode = "已加载";
                library.Summary = assets.Count == 0
                    ? "目录中没有找到受支持的文本、图片、视频或音频文件。"
                    : $"已加载 {assets.Count} 个素材文件，可在右侧列表查看。";

                RebuildAssetTree();
                RefreshExplorerSelectionAfterTreeRebuild();
                RebuildMetrics();

                if (SelectedLibrary?.Id == library.Id)
                {
                    WorkspaceTitle = library.Name;
                    WorkspaceSummary = library.RootPath;
                    AssetSummary = library.Summary;
                }

                ActivityFeedService.Add($"素材库数据已加载：{library.Name}，共 {assets.Count} 个素材文件。");
            }

            OperatorNotice = "全部素材库文件数据已加载完成。";
            Log.Information("全部素材库数据加载完成。");
            CompleteTask(taskId, "全部素材库文件数据已加载完成");
        }
        catch (Exception ex)
        {
            OperatorNotice = $"素材库数据加载失败：{ex.Message}";
            ActivityFeedService.Add($"素材库数据加载失败：{ex.Message}");
            Log.Error(ex, "素材库数据加载失败。");
            FailTask(taskId, "素材库数据加载失败", ex.Message);
        }
    }

    private async Task ScanLibraryAsync(LibraryWorkspace library)
    {
        if (AssetLibraryService is null || IsLibraryScanRunning)
        {
            return;
        }

        var taskId = BackgroundTaskService?.BeginTask("素材库扫描", $"正在扫描素材库：{library.Name}", library.RootPath);
        try
        {
            IsLibraryScanRunning = true;
            Log.Information("开始扫描素材库: libraryId={LibraryId}, libraryName={LibraryName}, rootPath={RootPath}", library.Id, library.Name, library.RootPath);
            library.SyncMode = "扫描中";
            library.Summary = "正在扫描目录下的文本、图片、视频和音频文件。";
            OperatorNotice = $"正在扫描素材库：{library.RootPath}";

            var assets = await AssetLibraryService.ScanLibraryAsync(library);
            Log.Information("素材库扫描完成: libraryId={LibraryId}, libraryName={LibraryName}, assetCount={AssetCount}", library.Id, library.Name, assets.Count);
            AllAssets.RemoveAll(asset => asset.LibraryName == library.Name);
            AllAssets.AddRange(assets);

            library.AssetCount = assets.Count;
            library.SyncMode = "已扫描";
            library.Summary = assets.Count == 0
                ? "目录中没有找到受支持的文本、图片、视频或音频文件。"
                : $"已扫描 {assets.Count} 个素材文件，可在右侧列表查看。";

            WorkspaceTitle = library.Name;
            WorkspaceSummary = library.RootPath;
            AssetSummary = library.Summary;

            RebuildAssetTree();
            RestoreTreeSelection(library);
            RebuildMetrics();
            ActivityFeedService.Add($"扫描完成：{library.Name}，共 {assets.Count} 个素材文件。");
            CompleteTask(taskId, $"扫描完成：{library.Name}", $"共 {assets.Count} 个素材文件");
        }
        catch (Exception ex)
        {
            library.SyncMode = "扫描失败";
            library.Summary = ex.Message;
            OperatorNotice = $"扫描失败：{ex.Message}";
            ActivityFeedService.Add($"扫描失败：{library.Name} -> {ex.Message}");
            Log.Error(ex, "素材库扫描失败: libraryId={LibraryId}, libraryName={LibraryName}", library.Id, library.Name);
            FailTask(taskId, $"扫描失败：{library.Name}", ex.Message);
        }
        finally
        {
            IsLibraryScanRunning = false;
        }
    }
}
