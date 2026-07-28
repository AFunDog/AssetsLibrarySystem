using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.AssetLibrary;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

/// <summary>
/// 素材库工作台 ViewModel，持有所有 UI 状态。
/// 代替原来 LibraryCatalogService 中的 ObservableProperty 职责。
/// </summary>
public sealed partial class LibraryWorkspaceViewModel : ObservableObject
{
    // ===== 依赖 =====
    private ILibraryCatalogService CatalogService { get; }
    private IAssetDescriptionStore? DescriptionStore { get; }
    private IAssetDatabase? AssetDatabase { get; }
    private AngleProfileManager? AngleProfileManager { get; }
    private ActivityFeedService ActivityFeedService { get; }
    private List<ManagedAssetRecord> AllAssets { get; } = [];

    public LibraryWorkspaceViewModel(
        ILibraryCatalogService catalogService,
        IAssetDescriptionStore? descriptionStore,
        IAssetDatabase? assetDatabase,
        AngleProfileManager? angleProfileManager,
        ActivityFeedService activityFeedService)
    {
        CatalogService = catalogService;
        DescriptionStore = descriptionStore;
        AssetDatabase = assetDatabase;
        AngleProfileManager = angleProfileManager;
        ActivityFeedService = activityFeedService;

        Metrics = [];
        AssetTreeRoots = [];
        Libraries = [];
        CurrentExplorerItems = [];
        SelectedAssetDescriptionAngles = [];
        SetEmptyWorkspaceState();
    }

    // ===== 设计时构造函数 =====
    [Obsolete("仅供设计器使用")]
    public LibraryWorkspaceViewModel()
        : this(new NullLibraryCatalogService(), null, null, null, new ActivityFeedService())
    {
        Libraries.Add(new LibraryWorkspace(1, "示例素材库", @"D:\素材", "示例", "已登记", 42));
        SelectedLibrary = Libraries[0];
        WorkspaceTitle = "示例素材库";
        WorkspaceSummary = @"D:\素材";
        SelectedAssetName = "示例素材.mp3";
        SelectedAssetType = "音频";
    }

    // ===== Observable 状态 =====
    public ObservableCollection<DashboardMetric> Metrics { get; }
    public ObservableCollection<AssetLibraryTreeNode> AssetTreeRoots { get; }
    public ObservableCollection<LibraryWorkspace> Libraries { get; }
    public ObservableCollection<AssetLibraryTreeNode> CurrentExplorerItems { get; }
    public ObservableCollection<AngleDescriptionRecord> SelectedAssetDescriptionAngles { get; }

    [ObservableProperty] public partial LibraryWorkspace? SelectedLibrary { get; set; }
    [ObservableProperty] public partial ManagedAssetRecord? SelectedAsset { get; set; }
    [ObservableProperty] public partial AssetLibraryTreeNode? SelectedAssetTreeNode { get; set; }
    [ObservableProperty] public partial string WorkspaceTitle { get; set; } = "本地素材工作台";
    [ObservableProperty] public partial string WorkspaceSummary { get; set; } = "先登记素材库目录，再扫描本地文件。";
    [ObservableProperty] public partial string AssetSummary { get; set; } = "当前还没有扫描结果。";
    [ObservableProperty] public partial string ExplorerTitle { get; set; } = "素材库";
    [ObservableProperty] public partial string ExplorerSummary { get; set; } = "选择一个素材库或目录后，中央区域会显示当前内容。";
    [ObservableProperty] public partial string ExplorerPath { get; set; } = "尚未选择";
    [ObservableProperty] public partial bool CanNavigateUp { get; set; }
    [ObservableProperty] public partial string OperatorNotice { get; set; } = "先在桌面端选择一个文件夹并登记为素材库目录。";
    [ObservableProperty] public partial string SelectedAssetName { get; set; } = "尚未选择素材";
    [ObservableProperty] public partial string SelectedAssetLibrary { get; set; } = "请先添加并扫描一个素材库";
    [ObservableProperty] public partial string SelectedAssetPath { get; set; } = "当前未加载本地文件路径";
    [ObservableProperty] public partial string SelectedAssetType { get; set; } = "未选择";
    [ObservableProperty] public partial string SelectedAssetStage { get; set; } = "待选择";
    [ObservableProperty] public partial string SelectedAssetAiState { get; set; } = "未描述";
    [ObservableProperty] public partial string SelectedAssetDetail { get; set; } = "当前素材还没有可显示的 AI 描述。";
    [ObservableProperty] public partial string SelectedAssetSubtype { get; set; } = "";
    [ObservableProperty] public partial string SelectedAssetDescriptionState { get; set; } = "未描述";
    [ObservableProperty] public partial string SelectedAssetDescriptionStorePath { get; set; } = "尚未生成描述记录";
    [ObservableProperty] public partial string SelectedAssetDescriptionGeneratedAt { get; set; } = "未生成";
    [ObservableProperty] public partial string SelectedAssetDescriptionMode { get; set; } = "未生成";
    [ObservableProperty] public partial string SelectedAssetDescriptionTokenUsage { get; set; } = "未返回 token 用量";
    [ObservableProperty] public partial string SelectedAssetDescriptionPrompt { get; set; } = "尚未生成 prompt。";
    [ObservableProperty] public partial string SelectedAssetDescriptionSystemPrompt { get; set; } = "尚未生成 system prompt。";
    [ObservableProperty] public partial string SelectedAssetDescriptionText { get; set; } = "当前素材还没有可显示的 AI 描述。";
    [ObservableProperty] public partial string DescriptionSelectionSummary { get; set; } = "请选择左侧素材库、目录或单个素材，再安排描述任务。";

    // ===== 初始化 =====
    public async Task InitializeAsync()
    {
        Log.Information("初始化素材库工作台。");
        await LoadLibrariesAsync();
    }

    private async Task LoadLibrariesAsync()
    {
        Libraries.Clear();
        AssetTreeRoots.Clear();
        AllAssets.Clear();

        var libraries = await CatalogService.GetLibrariesAsync();
        foreach (var library in libraries)
            Libraries.Add(library);

        RebuildAssetTree();
        RebuildMetrics();

        if (Libraries.Count == 0)
        {
            SetEmptyWorkspaceState();
            ActivityFeedService.Add("当前尚未登记素材库目录。");
            return;
        }

        SelectedLibrary = Libraries[0];
        WorkspaceTitle = SelectedLibrary.Name;
        WorkspaceSummary = SelectedLibrary.RootPath;
        UpdateExplorerView(null);
        _ = LoadAllLibraryDataAsync();
    }

    private async Task LoadAllLibraryDataAsync()
    {
        foreach (var library in Libraries.ToList())
        {
            var assets = await CatalogService.ScanLibraryAsync(library);
            AllAssets.RemoveAll(a => a.LibraryName == library.Name);
            AllAssets.AddRange(assets);
            library.AssetCount = assets.Count;
            RebuildAssetTree();
            RebuildMetrics();
            if (SelectedLibrary?.Id == library.Id)
            {
                WorkspaceTitle = library.Name;
                WorkspaceSummary = library.RootPath;
                AssetSummary = library.Summary;
            }
        }
        OperatorNotice = "全部素材库文件数据已加载完成。";
    }

    public async Task AddLibraryDirectoryAsync(string folderPath)
    {
        var library = await CatalogService.AddLibraryAsync(folderPath);
        var existing = Libraries.FirstOrDefault(l => l.RootPath == library.RootPath);
        if (existing is null)
        {
            Libraries.Add(library);
            RebuildAssetTree();
        }
        else
        {
            library = existing;
        }
        SelectedLibrary = library;
        if (AllAssets.All(a => a.LibraryName != library.Name))
            _ = LoadLibraryDataForAsync(library);
    }

    private async Task LoadLibraryDataForAsync(LibraryWorkspace library)
    {
        var assets = await CatalogService.ScanLibraryAsync(library);
        AllAssets.RemoveAll(a => a.LibraryName == library.Name);
        AllAssets.AddRange(assets);
        library.AssetCount = assets.Count;
        RebuildAssetTree();
        RebuildMetrics();
    }

    [RelayCommand]
    public async Task ScanSelectedLibraryAsync()
    {
        if (SelectedLibrary is null) return;
        var assets = await CatalogService.ScanLibraryAsync(SelectedLibrary);
        AllAssets.RemoveAll(a => a.LibraryName == SelectedLibrary.Name);
        AllAssets.AddRange(assets);
        SelectedLibrary.AssetCount = assets.Count;
        RebuildAssetTree();
        RebuildMetrics();
    }

    // ===== 导航命令 =====
    [RelayCommand]
    private void NavigateUp()
    {
        var container = GetExplorerContainerNode(SelectedAssetTreeNode);
        if (container is null) return;
        if (container.Kind == AssetLibraryTreeNodeKind.Library)
        {
            SelectedAssetTreeNode = null;
            return;
        }
        SelectedAssetTreeNode = FindParentTreeNode(container);
    }

    [RelayCommand]
    private void OpenExplorerItem(AssetLibraryTreeNode? node)
    {
        if (node is not null)
            SelectedAssetTreeNode = node;
    }

    public void SelectLibrary(LibraryWorkspace? library)
    {
        if (library is null) return;
        SelectedLibrary = library;
        SelectedAssetTreeNode = FindLibraryTreeNode(library.Id);
    }

    // ===== CRUD 命令 =====
    [RelayCommand]
    private async Task DeleteAssetAsync()
    {
        if (SelectedAsset is null) return;
        await CatalogService.DeleteAssetAsync(SelectedAsset.DatabaseId);
        AllAssets.Remove(SelectedAsset);
        SelectedAsset = null;
        ResetSelectedAssetDescription();
        RebuildAssetTree();
        RebuildMetrics();
    }

    [RelayCommand]
    private async Task DeleteLibraryAsync()
    {
        if (SelectedLibrary is null) return;
        var id = SelectedLibrary.Id;
        var name = SelectedLibrary.Name;
        AllAssets.RemoveAll(a => a.LibraryName == name);
        await CatalogService.DeleteLibraryAsync(id);
        Libraries.Remove(SelectedLibrary);
        SelectedLibrary = null;
        SelectedAsset = null;
        SelectedAssetTreeNode = null;
        SetEmptyWorkspaceState();
        RebuildMetrics();
    }

    public async Task UpdateSelectedAssetTagsAsync(string[] tags)
    {
        if (SelectedAsset is null) return;
        await CatalogService.UpdateAssetTagsAsync(SelectedAsset.DatabaseId, tags);
        SelectedAsset.Tags.Clear();
        foreach (var tag in tags)
            SelectedAsset.Tags.Add(tag);
    }

    public async Task UpdateSelectedAssetNameAsync(string newName)
    {
        if (SelectedAsset is null) return;
        await CatalogService.UpdateAssetNameAsync(SelectedAsset.DatabaseId, newName);
        RebuildAssetTree();
        SyncSelectedAssetFields();
    }

    public async Task UpdateSelectedLibraryNameAsync(string newName)
    {
        if (SelectedLibrary is null) return;
        await CatalogService.UpdateLibraryAsync(SelectedLibrary.Id, newName);
        RebuildAssetTree();
        WorkspaceTitle = newName;
    }

    // ===== 描述相关 =====
    public async Task UpdateSelectedAssetDescriptionAsync(string newDescription)
    {
        if (SelectedAsset is null || DescriptionStore is null) return;
        await DescriptionStore.UpdateDescriptionAsync(SelectedAsset.DatabaseId, newDescription);
        SelectedAssetDescriptionText = newDescription;
    }

    // ===== 辅助方法 =====
    public void SetOperatorNotice(string message)
    {
        OperatorNotice = message;
    }

    public IReadOnlyList<ManagedAssetRecord> GetDescriptionSelectionAssets()
    {
        if (SelectedAsset is not null) return [SelectedAsset];
        if (SelectedAssetTreeNode is null) return [];
        if (SelectedAssetTreeNode.Kind == AssetLibraryTreeNodeKind.Library && SelectedAssetTreeNode.Library is not null)
            return AllAssets.Where(a => a.LibraryName == SelectedAssetTreeNode.Library.Name).ToList();
        var prefix = NormalizePathPrefix(SelectedAssetTreeNode.FullPath);
        return AllAssets.Where(a => NormalizePathPrefix(a.LocalPath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public IReadOnlyList<ManagedAssetRecord> GetAllLibraryAssets() => AllAssets.ToList();

    public void MarkAssetDescriptionQueued(ManagedAssetRecord asset)
    {
        asset.Stage = "描述中";
        asset.AiState = "描述生成中";
        if (ReferenceEquals(SelectedAsset, asset)) SyncSelectedAssetFields();
        else RebuildAssetTree();
    }

    public void CompleteAssetDescription(ManagedAssetRecord asset, string description)
    {
        asset.Stage = "已描述";
        asset.AiState = "已描述";
        asset.IsDescribed = true;
        if (ReferenceEquals(SelectedAsset, asset))
        {
            SelectedAssetDescriptionText = description;
            SyncSelectedAssetFields();
        }
        else RebuildAssetTree();
        RebuildMetrics();
    }

    public void MarkAssetVectorized(ManagedAssetRecord asset)
    {
        asset.IsVectorized = true;
    }

    public void FailAssetDescription(ManagedAssetRecord asset, string error)
    {
        asset.Stage = "描述失败";
        asset.AiState = "调用后端失败";
        if (ReferenceEquals(SelectedAsset, asset)) SyncSelectedAssetFields();
        else RebuildAssetTree();
    }

    public void RemoveAssetDescription(ManagedAssetRecord asset, bool vectorDeleted)
    {
        asset.IsDescribed = false;
        asset.IsVectorized = false;
        asset.Stage = "已识别";
        asset.AiState = "未描述";
        if (ReferenceEquals(SelectedAsset, asset))
        {
            ResetSelectedAssetDescription();
            SyncSelectedAssetFields();
        }
        else RebuildAssetTree();
        RebuildMetrics();
    }

    public void RefreshMetrics() => RebuildMetrics();

    // ===== 内部方法 =====

    private void UpdateSelectedAssetDetails(ManagedAssetRecord? value)
    {
        if (value is null)
        {
            SelectedAssetName = "尚未选择素材";
            SelectedAssetLibrary = "请先扫描一个素材库";
            SelectedAssetPath = "当前未加载本地文件路径";
            SelectedAssetType = "未选择";
            SelectedAssetStage = "待选择";
            SelectedAssetAiState = "未描述";
            SelectedAssetDetail = "当前素材还没有可显示的 AI 描述。";
            ResetSelectedAssetDescription();
            return;
        }
        SelectedAssetName = value.Name;
        SelectedAssetLibrary = value.LibraryName;
        SelectedAssetPath = value.LocalPath;
        SelectedAssetType = value.AssetType;
        SelectedAssetStage = value.Stage;
        SelectedAssetAiState = value.AiState;
        SelectedAssetDetail = value.Summary;
        ResetSelectedAssetDescription();
    }

    private void ResetSelectedAssetDescription()
    {
        SelectedAssetDescriptionState = "未描述";
        SelectedAssetDescriptionStorePath = "尚未生成描述记录";
        SelectedAssetDescriptionGeneratedAt = "未生成";
        SelectedAssetDescriptionMode = "未生成";
        SelectedAssetDescriptionTokenUsage = "未返回 token 用量";
        SelectedAssetDescriptionPrompt = "尚未生成 prompt。";
        SelectedAssetDescriptionSystemPrompt = "尚未生成 system prompt。";
        SelectedAssetDescriptionText = "当前素材还没有可显示的 AI 描述。";
        SelectedAssetDescriptionAngles.Clear();
    }

    private void SetEmptyWorkspaceState()
    {
        WorkspaceTitle = "尚未添加素材库";
        WorkspaceSummary = "请选择一个本地文件夹并登记为素材库目录。";
        AssetSummary = "支持扫描文本、图片、视频和音频文件。";
        SelectedAsset = null;
    }

    private void SyncSelectedAssetFields()
    {
        if (SelectedAsset is null) return;
        SelectedAssetStage = SelectedAsset.Stage;
        SelectedAssetAiState = SelectedAsset.AiState;
        RebuildAssetTree();
    }

    private void RebuildMetrics()
    {
        Metrics.Clear();
        var total = AllAssets.Count;
        Metrics.Add(new DashboardMetric("素材总数", total.ToString("D2"), $"{Libraries.Count} 个本地素材库"));
        Metrics.Add(new DashboardMetric("文本", AllAssets.Count(a => a.AssetType == "文本").ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("图片", AllAssets.Count(a => a.AssetType == "图片").ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("视频", AllAssets.Count(a => a.AssetType == "视频").ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("音频", AllAssets.Count(a => a.AssetType == "音频").ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("已描述", AllAssets.Count(a => a.IsDescribed).ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("已向量化", AllAssets.Count(a => a.IsVectorized).ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("待描述", AllAssets.Count(a => !a.IsDescribed).ToString("D2"), ""));
    }

    // ===== 树形导航方法 =====

    private void RebuildAssetTree()
    {
        AssetTreeRoots.Clear();
        foreach (var library in Libraries.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            AssetTreeRoots.Add(BuildLibraryTree(library));
        UpdateExplorerView(SelectedAssetTreeNode);
    }

    private AssetLibraryTreeNode BuildLibraryTree(LibraryWorkspace library)
    {
        var libraryAssets = AllAssets
            .Where(a => a.LibraryName == library.Name)
            .OrderBy(a => a.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var root = new AssetLibraryTreeNode
        {
            DisplayName = library.Name,
            MetaLabel = BuildCountLabel(libraryAssets.Count),
            CategorySummary = string.Join(" / ", libraryAssets.Select(a => a.AssetType).Distinct()),
            TypeLabel = "素材库",
            StatusLabel = library.SyncMode,
            PathLabel = library.RootPath,
            Summary = library.Summary,
            IconKind = "Folder",
            FullPath = library.RootPath,
            Kind = AssetLibraryTreeNodeKind.Library,
            Library = library
        };

        var directories = new Dictionary<string, AssetLibraryTreeNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in libraryAssets)
        {
            var currentNode = root;
            var segments = asset.RelativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length - 1; i++)
            {
                var key = string.Join('/', segments.Take(i + 1));
                if (!directories.TryGetValue(key, out var folderNode))
                {
                    folderNode = new AssetLibraryTreeNode
                    {
                        DisplayName = segments[i], TypeLabel = "目录",
                        PathLabel = key, Summary = $"目录 · {key}",
                        IconKind = "Folder",
                        FullPath = Path.Combine(library.RootPath, key.Replace('/', Path.DirectorySeparatorChar)),
                        Kind = AssetLibraryTreeNodeKind.Directory, Library = library
                    };
                    directories[key] = folderNode;
                    currentNode.Children.Add(folderNode);
                }
                currentNode = folderNode;
            }
            currentNode.Children.Add(new AssetLibraryTreeNode
            {
                DisplayName = asset.Name, MetaLabel = asset.DescriptionStatusLabel,
                CategorySummary = asset.Stage, TypeLabel = asset.AssetType,
                StatusLabel = asset.FileSizeLabel, PathLabel = asset.RelativePath,
                Summary = asset.Summary, IconKind = "File",
                FullPath = asset.LocalPath, Kind = AssetLibraryTreeNodeKind.File,
                Library = library, Asset = asset
            });
        }
        PopulateDirectoryStatistics(root);
        return root;
    }

    private void PopulateDirectoryStatistics(AssetLibraryTreeNode node)
    {
        foreach (var child in node.Children) PopulateDirectoryStatistics(child);
        if (node.Kind == AssetLibraryTreeNodeKind.File) return;
        var assetNodes = EnumerateAssetNodes(node).ToList();
        var described = assetNodes.Count(n => n.Asset?.IsDescribed == true);
        node.MetaLabel = $"{described}/{assetNodes.Count} 已描述";
        node.CategorySummary = string.Join(" / ", assetNodes.Select(n => n.TypeLabel).Distinct());
    }

    private static IEnumerable<AssetLibraryTreeNode> EnumerateAssetNodes(AssetLibraryTreeNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.Kind == AssetLibraryTreeNodeKind.File) yield return child;
            else foreach (var d in EnumerateAssetNodes(child)) yield return d;
        }
    }

    private void UpdateExplorerView(AssetLibraryTreeNode? node)
    {
        var container = GetExplorerContainerNode(node);
        CurrentExplorerItems.Clear();
        if (container is null)
        {
            ExplorerTitle = "素材库";
            ExplorerSummary = "选择一个素材库后，中央区域会显示该库下的目录和文件。";
            ExplorerPath = "未选择";
            CanNavigateUp = false;
            foreach (var r in AssetTreeRoots) CurrentExplorerItems.Add(r);
            return;
        }
        foreach (var item in container.Children) CurrentExplorerItems.Add(item);
        ExplorerTitle = container.DisplayName;
        ExplorerSummary = container.Kind == AssetLibraryTreeNodeKind.Library ? container.Summary : $"{container.MetaLabel} · {container.CategorySummary}";
        ExplorerPath = container.FullPath;
        CanNavigateUp = container.Kind != AssetLibraryTreeNodeKind.Library && FindParentTreeNode(container) is not null;
    }

    private AssetLibraryTreeNode? GetExplorerContainerNode(AssetLibraryTreeNode? node)
    {
        if (node is null) return null;
        return node.Kind != AssetLibraryTreeNodeKind.File ? node : FindParentTreeNode(node);
    }

    private AssetLibraryTreeNode? FindParentTreeNode(AssetLibraryTreeNode node)
    {
        if (node.Kind == AssetLibraryTreeNodeKind.Library || string.IsNullOrWhiteSpace(node.FullPath)) return null;
        var parentPath = Path.GetDirectoryName(node.FullPath);
        return string.IsNullOrWhiteSpace(parentPath) ? null : FindTreeNodeByPath(parentPath);
    }

    private AssetLibraryTreeNode? FindTreeNodeByPath(string path)
    {
        var normalized = NormalizePath(path);
        foreach (var root in AssetTreeRoots)
        {
            var match = FindTreeNodeByPathRecursive(root, normalized);
            if (match is not null) return match;
        }
        return null;
    }

    private static AssetLibraryTreeNode? FindTreeNodeByPathRecursive(AssetLibraryTreeNode node, string normalizedPath)
    {
        if (string.Equals(NormalizePath(node.FullPath), normalizedPath, StringComparison.OrdinalIgnoreCase))
            return node;
        foreach (var child in node.Children)
        {
            var match = FindTreeNodeByPathRecursive(child, normalizedPath);
            if (match is not null) return match;
        }
        return null;
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
    }

    private static string NormalizePathPrefix(string value) =>
        value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private AssetLibraryTreeNode? FindLibraryTreeNode(long libraryId) =>
        AssetTreeRoots.FirstOrDefault(n => n.Library?.Id == libraryId);

    private static string BuildCountLabel(int count) =>
        count == 0 ? "空目录" : $"{count} 项";

    partial void OnSelectedAssetTreeNodeChanged(AssetLibraryTreeNode? value)
    {
        if (value is null) return;
        if (value.Library is not null && !ReferenceEquals(SelectedLibrary, value.Library))
            SelectedLibrary = value.Library;
        WorkspaceTitle = value.DisplayName;
        WorkspaceSummary = value.FullPath;
        AssetSummary = value.Summary;
        SelectedAsset = value.Asset;
        UpdateSelectedAssetDetails(value.Asset);
        UpdateExplorerView(value);
    }

    partial void OnSelectedAssetChanged(ManagedAssetRecord? value)
    {
        UpdateSelectedAssetDetails(value);
    }
}

/// <summary>空实现，用于设计时模式，不访问数据库</summary>
file sealed class NullLibraryCatalogService : ILibraryCatalogService
{
    public Task<IReadOnlyList<LibraryWorkspace>> GetLibrariesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LibraryWorkspace>>([]);
    public Task<LibraryWorkspace> AddLibraryAsync(string folderPath, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<IReadOnlyList<ManagedAssetRecord>> ScanLibraryAsync(LibraryWorkspace library, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ManagedAssetRecord>>([]);
    public Task DeleteLibraryAsync(long libraryId, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task UpdateLibraryAsync(long libraryId, string newName, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task DeleteAssetAsync(long assetId, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task UpdateAssetTagsAsync(long assetId, string[] tags, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task UpdateAssetNameAsync(long assetId, string newName, CancellationToken ct = default)
        => Task.CompletedTask;
}