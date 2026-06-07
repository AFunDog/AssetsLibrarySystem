using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.AssetLibrary;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.Services.Library;

public sealed partial class LibraryCatalogService : ObservableObject
{
    private IAssetLibraryService? AssetLibraryService { get; }
    private IAssetDescriptionStore? AssetDescriptionStore { get; }
    private IBackgroundTaskService? BackgroundTaskService { get; }
    private ActivityFeedService ActivityFeedService { get; }
    private List<ManagedAssetRecord> AllAssets { get; } = [];
    private bool SuppressLibrarySelectionLoad { get; set; }
    private bool IsLibraryScanRunning { get; set; }

    public LibraryCatalogService()
        : this(null, null, null, new ActivityFeedService())
    {
    }

    public LibraryCatalogService(
        IAssetLibraryService? assetLibraryService,
        IAssetDescriptionStore? assetDescriptionStore,
        IBackgroundTaskService? backgroundTaskService,
        ActivityFeedService activityFeedService)
    {
        AssetLibraryService = assetLibraryService;
        AssetDescriptionStore = assetDescriptionStore;
        BackgroundTaskService = backgroundTaskService;
        ActivityFeedService = activityFeedService;

        Metrics = [];
        AssetTreeRoots = [];
        Libraries = [];
        CurrentExplorerItems = [];

        WorkspaceTitle = "本地素材工作台";
        WorkspaceSummary = "先登记素材库目录，再扫描本地文件，桌面端负责目录和元数据展示。";
        AssetSummary = "当前还没有扫描结果。选择一个素材库后，点击“扫描当前素材库”加载文件。";
        ExplorerTitle = "素材库";
        ExplorerSummary = "选择一个素材库或目录后，中央区域会显示当前内容。";
        ExplorerPath = "尚未选择";
        CanNavigateUp = false;
        OperatorNotice = "先在桌面端选择一个文件夹并登记为素材库目录，再触发扫描。";
        SelectedAssetName = "尚未选择素材";
        SelectedAssetLibrary = "请先添加并扫描一个素材库";
        SelectedAssetPath = "当前未加载本地文件路径";
        SelectedAssetType = "未选择";
        SelectedAssetStage = "待选择";
        SelectedAssetAiState = "未排队";
        SelectedAssetDetail = "右侧详情区域会展示当前素材的路径、类型和扫描结果。";
        DescriptionSelectionSummary = "请选择左侧素材库、目录或单个素材，再安排描述任务。";

        RebuildMetrics();
        SetEmptyWorkspaceState();
        ResetSelectedAssetDescription();
    }

    public ObservableCollection<DashboardMetric> Metrics { get; }
    public ObservableCollection<AssetLibraryTreeNode> AssetTreeRoots { get; }
    public ObservableCollection<LibraryWorkspace> Libraries { get; }
    public ObservableCollection<AssetLibraryTreeNode> CurrentExplorerItems { get; }

    [ObservableProperty]
    public partial LibraryWorkspace? SelectedLibrary { get; set; }

    [ObservableProperty]
    public partial ManagedAssetRecord? SelectedAsset { get; set; }

    [ObservableProperty]
    public partial AssetLibraryTreeNode? SelectedAssetTreeNode { get; set; }

    [ObservableProperty]
    public partial string WorkspaceTitle { get; set; }

    [ObservableProperty]
    public partial string WorkspaceSummary { get; set; }

    [ObservableProperty]
    public partial string AssetSummary { get; set; }

    [ObservableProperty]
    public partial string ExplorerTitle { get; set; }

    [ObservableProperty]
    public partial string ExplorerSummary { get; set; }

    [ObservableProperty]
    public partial string ExplorerPath { get; set; }

    [ObservableProperty]
    public partial bool CanNavigateUp { get; set; }

    [ObservableProperty]
    public partial string OperatorNotice { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetName { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetLibrary { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetPath { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetType { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetStage { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetAiState { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetDetail { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetDescriptionState { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetDescriptionStorePath { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetDescriptionGeneratedAt { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetDescriptionMode { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetDescriptionTokenUsage { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetDescriptionPrompt { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetDescriptionSystemPrompt { get; set; }

    [ObservableProperty]
    public partial string SelectedAssetDescriptionText { get; set; }

    [ObservableProperty]
    public partial string DescriptionSelectionSummary { get; set; }

    partial void OnSelectedLibraryChanged(LibraryWorkspace? value)
    {
        if (SuppressLibrarySelectionLoad)
        {
            return;
        }

        _ = LoadSelectedLibraryAsync(value);
    }

    partial void OnSelectedAssetChanged(ManagedAssetRecord? value)
    {
        UpdateSelectedAssetDetails(value);
        UpdateDescriptionSelectionSummary();
        _ = LoadSelectedAssetDescriptionAsync(value);
    }

    partial void OnSelectedAssetTreeNodeChanged(AssetLibraryTreeNode? value)
    {
        ApplyTreeSelection(value);
        UpdateExplorerView(value);
        UpdateDescriptionSelectionSummary();
    }

    public async Task InitializeAsync()
    {
        Log.Information("初始化素材库工作台。");
        await LoadLibrariesAsync();
    }

    public async Task AddLibraryDirectoryAsync(string folderPath)
    {
        Log.Information("用户操作: 添加素材库目录，folderPath={FolderPath}", folderPath);
        if (AssetLibraryService is null)
        {
            OperatorNotice = "素材库服务尚未注册，当前无法保存目录。";
            Log.Warning("添加素材库目录失败：素材库服务未注册。");
            return;
        }

        var library = await AssetLibraryService.AddLibraryAsync(folderPath);
        var existing = Libraries.FirstOrDefault(item =>
            string.Equals(item.RootPath, library.RootPath, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            Libraries.Add(library);
            ActivityFeedService.Add($"已登记素材库目录：{library.RootPath}");
            Log.Information("素材库目录已登记: libraryId={LibraryId}, libraryName={LibraryName}, rootPath={RootPath}", library.Id, library.Name, library.RootPath);
            RebuildAssetTree();
        }
        else
        {
            library = existing;
            ActivityFeedService.Add($"素材库目录已存在：{library.RootPath}");
            Log.Information("素材库目录已存在: libraryId={LibraryId}, libraryName={LibraryName}, rootPath={RootPath}", library.Id, library.Name, library.RootPath);
        }

        SelectedLibrary = library;
        OperatorNotice = $"已登记素材库“{library.Name}”，下一步请执行扫描。";
        RebuildMetrics();
        await ScanLibraryAsync(library);
    }

    public void SelectLibrary(LibraryWorkspace? library)
    {
        Log.Information("用户操作: 选择素材库，libraryId={LibraryId}, libraryName={LibraryName}", library?.Id ?? "none", library?.Name ?? "none");

        if (library is null)
        {
            SelectedLibrary = null;
            SelectedAssetTreeNode = null;
            OperatorNotice = "请先选择一个素材库。";
            return;
        }

        SelectedLibrary = library;
        SelectedAssetTreeNode = FindLibraryTreeNode(library.Id);
        OperatorNotice = $"已切换到素材库“{library.Name}”。";
        ActivityFeedService.Add($"切换素材库：{library.Name}");
    }

    public void NavigateUpExplorer()
    {
        NavigateUp();
    }

    public Task RefreshSelectedLibraryAsync()
    {
        Log.Information("用户操作: 刷新当前素材库。 selectedLibrary={SelectedLibrary}", SelectedLibrary?.Name ?? "none");
        return SelectedLibrary is null ? Task.CompletedTask : ScanLibraryAsync(SelectedLibrary);
    }

    public Task ScanSelectedLibraryAsync()
    {
        Log.Information("用户操作: 扫描当前素材库。 selectedLibrary={SelectedLibrary}", SelectedLibrary?.Name ?? "none");
        return SelectedLibrary is null ? Task.CompletedTask : ScanLibraryAsync(SelectedLibrary);
    }

    public void MarkSelectedAssetManaged()
    {
        if (SelectedAsset is null)
        {
            OperatorNotice = "请先选择一个素材，再调整其桌面端管理状态。";
            Log.Warning("标记素材为已接管失败：未选择素材。");
            return;
        }

        SelectedAsset.Stage = "桌面端已接管";
        SelectedAsset.AiState = "无需后端素材处理";
        SyncSelectedAssetFields();
        OperatorNotice = $"{SelectedAsset.Name} 已切换到 .NET 素材管理视图。";
        ActivityFeedService.Add($"状态更新：{SelectedAsset.Name} -> 桌面端已接管");
        Log.Information("素材状态已切换为桌面端已接管: assetUid={AssetUid}, assetName={AssetName}", SelectedAsset.AssetUid, SelectedAsset.Name);
    }

    public IReadOnlyList<ManagedAssetRecord> GetDescriptionSelectionAssets()
    {
        return EnumerateDescriptionSelectionAssets().ToList();
    }

    public void MarkAssetDescriptionQueued(ManagedAssetRecord asset)
    {
        asset.Stage = "描述中";
        asset.AiState = "已发送到 Python HTTP 服务";
        Log.Information("素材进入描述队列: assetUid={AssetUid}, assetName={AssetName}", asset.AssetUid, asset.Name);

        if (ReferenceEquals(SelectedAsset, asset))
        {
            SyncSelectedAssetFields();
        }
        else
        {
            RebuildAssetTree();
            RefreshExplorerSelectionAfterTreeRebuild();
        }

        OperatorNotice = $"已为 {asset.Name} 排入描述任务，正在调用后端服务。";
        ActivityFeedService.Add($"描述任务排队：{asset.Name}");
    }

    public void CompleteAssetDescription(ManagedAssetRecord asset, AssetDescriptionDocument document)
    {
        asset.Stage = document.Mode == "live" ? "已描述" : "已描述（占位）";
        asset.AiState = $"SQLite 已保存 · {document.Mode}";
        Log.Information(
            "素材描述完成: assetUid={AssetUid}, assetName={AssetName}, storePath={StorePath}, mode={Mode}",
            asset.AssetUid,
            asset.Name,
            document.StorePath,
            document.Mode);

        if (ReferenceEquals(SelectedAsset, asset))
        {
            ApplySelectedAssetDescription(document);
            SyncSelectedAssetFields();
        }
        else
        {
            RebuildAssetTree();
            RefreshExplorerSelectionAfterTreeRebuild();
        }

        OperatorNotice = $"描述已写入 SQLite：{document.StorePath}";
        ActivityFeedService.Add($"描述完成：{asset.Name} -> {document.StorePath}");
    }

    public void FailAssetDescription(ManagedAssetRecord asset, string error)
    {
        asset.Stage = "描述失败";
        asset.AiState = "调用后端失败";
        Log.Warning("素材描述失败: assetUid={AssetUid}, assetName={AssetName}, error={Error}", asset.AssetUid, asset.Name, error);

        if (ReferenceEquals(SelectedAsset, asset))
        {
            SyncSelectedAssetFields();
        }
        else
        {
            RebuildAssetTree();
            RefreshExplorerSelectionAfterTreeRebuild();
        }

        OperatorNotice = $"描述任务失败：{error}";
        ActivityFeedService.Add($"描述失败：{asset.Name} -> {error}");
    }

    public void RemoveAssetDescription(ManagedAssetRecord asset, bool vectorDeleted)
    {
        asset.IsDescribed = false;
        asset.Stage = "已识别";
        asset.AiState = vectorDeleted ? "描述与向量已删除" : "描述已删除";
        Log.Information(
            "素材描述已删除: assetUid={AssetUid}, assetName={AssetName}, vectorDeleted={VectorDeleted}",
            asset.AssetUid,
            asset.Name,
            vectorDeleted);

        if (ReferenceEquals(SelectedAsset, asset))
        {
            ResetSelectedAssetDescription();
            SelectedAssetDescriptionState = "当前素材尚未生成 AI 描述";
            SelectedAssetDescriptionStorePath = AssetDescriptionStore?.DatabasePath ?? "SQLite 存储未就绪";
            SelectedAssetDescriptionText = "当前素材的描述记录已删除。";
            SyncSelectedAssetFields();
        }
        else
        {
            RebuildAssetTree();
            RefreshExplorerSelectionAfterTreeRebuild();
        }

        OperatorNotice = vectorDeleted
            ? $"已删除 {asset.Name} 的描述和向量记录。"
            : $"已删除 {asset.Name} 的描述记录。";
        ActivityFeedService.Add(OperatorNotice);
    }

    public void SetOperatorNotice(string message)
    {
        OperatorNotice = message;
        Log.Debug("操作提示更新: {Message}", message);
    }
}
