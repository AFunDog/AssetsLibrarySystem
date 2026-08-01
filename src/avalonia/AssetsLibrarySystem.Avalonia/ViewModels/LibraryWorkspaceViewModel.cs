using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.AssetLibrary;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using AssetsLibrarySystem.Application.Services.Python;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Settings;
using AssetsLibrarySystem.Avalonia.Services.Thumbnail;
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
    private IAssetLibraryService CatalogService { get; }
    private IAssetDescriptionStore? DescriptionStore { get; }
    private IAssetDatabase? AssetDatabase { get; }
    private AngleProfileManager? AngleProfileManager { get; }
    private ActivityFeedService ActivityFeedService { get; }
    private ThumbnailCacheService? ThumbnailCache { get; }
    private VideoFrameService? VideoFrameService { get; }
    private IUserSettingsService? UserSettings { get; }
    private List<ManagedAssetRecord> AllAssets { get; } = [];
    private int DescriptionLoadGeneration { get; set; }
    private int SegmentThumbnailGeneration { get; set; }

    // ===== 导航历史（返回/前进） =====
    private readonly Stack<AssetLibraryTreeNode?> _backStack = [];
    private readonly Stack<AssetLibraryTreeNode?> _forwardStack = [];
    private AssetLibraryTreeNode? _historyCurrent;
    private bool _isHistoryNavigation;

    public LibraryWorkspaceViewModel(
        IAssetLibraryService catalogService,
        IAssetDescriptionStore? descriptionStore,
        IAssetDatabase? assetDatabase,
        AngleProfileManager? angleProfileManager,
        ActivityFeedService activityFeedService,
        ThumbnailCacheService? thumbnailCache = null,
        VideoFrameService? videoFrameService = null,
        IUserSettingsService? userSettings = null)
    {
        CatalogService = catalogService;
        DescriptionStore = descriptionStore;
        AssetDatabase = assetDatabase;
        AngleProfileManager = angleProfileManager;
        ActivityFeedService = activityFeedService;
        ThumbnailCache = thumbnailCache;
        VideoFrameService = videoFrameService;
        UserSettings = userSettings;

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
        : this(new NullAssetLibraryService(), null, null, null, new ActivityFeedService())
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

    /// <summary>片段列表项（已分割剪辑素材；空=未分割或无描述）</summary>
    public ObservableCollection<SegmentListItemViewModel> SelectedAssetSegmentItems { get; } = [];

    /// <summary>片段描述分组（剪辑素材：每段时间切片一组，组内含各角度描述；空=非剪辑或未描述）</summary>
    public ObservableCollection<SegmentDescriptionGroupViewModel> SelectedAssetSegmentDescriptionGroups { get; } = [];

    /// <summary>当前素材是否展示片段列表</summary>
    [ObservableProperty] public partial bool HasSelectedAssetSegments { get; set; }
    /// <summary>面包屑导航段（库根 › 目录 › … › 当前）</summary>
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

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
    [ObservableProperty] public partial bool CanGoBack { get; set; }
    [ObservableProperty] public partial bool CanGoForward { get; set; }
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

    // ===== 筛选与排序 =====
    [ObservableProperty] public partial string FilterAssetType { get; set; } = "全部";
    [ObservableProperty] public partial string FilterStatus { get; set; } = "全部";
    [ObservableProperty] public partial string FilterSortBy { get; set; } = "名称";
    [ObservableProperty] public partial bool FilterSortAscending { get; set; } = true;

    /// <summary>当前视图模式</summary>
    public ExplorerViewMode ViewMode
    {
        get => UserSettings?.ViewMode ?? ExplorerViewMode.Icon;
        set
        {
            if (UserSettings is not null)
                UserSettings.ViewMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsIconView));
            OnPropertyChanged(nameof(IsListView));
            OnPropertyChanged(nameof(IsDetailView));
        }
    }

    public bool IsIconView => ViewMode == ExplorerViewMode.Icon;
    public bool IsListView => ViewMode == ExplorerViewMode.List;
    public bool IsDetailView => ViewMode == ExplorerViewMode.Detail;

    [RelayCommand]
    private void SwitchToIconView() => ViewMode = ExplorerViewMode.Icon;
    [RelayCommand]
    private void SwitchToListView() => ViewMode = ExplorerViewMode.List;
    [RelayCommand]
    private void SwitchToDetailView() => ViewMode = ExplorerViewMode.Detail;

    public static string[] FilterAssetTypeOptions => ["全部", "文本", "图片", "视频", "音频", "视频剪辑"];
    public static string[] FilterStatusOptions => ["全部", "已描述", "未描述", "已向量化", "待处理"];
    public static string[] FilterSortByOptions => ["名称", "类型", "大小"];

    /// <summary>经过筛选和排序后的资源管理器项</summary>
    public ObservableCollection<AssetLibraryTreeNode> FilteredExplorerItems { get; } = [];

    partial void OnFilterAssetTypeChanged(string value) => ApplyFilterAndSort();
    partial void OnFilterStatusChanged(string value) => ApplyFilterAndSort();
    partial void OnFilterSortByChanged(string value) => ApplyFilterAndSort();
    partial void OnFilterSortAscendingChanged(bool value) => ApplyFilterAndSort();

    [RelayCommand]
    private void ToggleSortDirection()
    {
        FilterSortAscending = !FilterSortAscending;
    }

    private void ApplyFilterAndSort()
    {
        FilteredExplorerItems.Clear();

        // 从 CurrentExplorerItems 中筛选
        var items = CurrentExplorerItems.AsEnumerable();

        // 类型筛选
        if (FilterAssetType != "全部")
        {
            items = items.Where(item =>
                item.Kind == AssetLibraryTreeNodeKind.Directory ||
                item.Kind == AssetLibraryTreeNodeKind.Library ||
                string.Equals(item.TypeLabel, FilterAssetType, StringComparison.Ordinal));
        }

        // 状态筛选（仅对文件节点有效）
        if (FilterStatus != "全部")
        {
            items = items.Where(item =>
            {
                if (item.Kind != AssetLibraryTreeNodeKind.File || item.Asset is null)
                    return true; // 目录和库始终显示
                return FilterStatus switch
                {
                    "已描述" => item.Asset.IsDescribed,
                    "未描述" => !item.Asset.IsDescribed,
                    "已向量化" => item.Asset.IsVectorized,
                    "待处理" => !item.Asset.IsDescribed && !item.Asset.IsVectorized,
                    _ => true
                };
            });
        }

        // 排序：大小按数值，避免 "2 KB" / "10 KB" 字典序错误
        items = FilterSortBy switch
        {
            "类型" => FilterSortAscending
                ? items.OrderBy(item => item.Kind).ThenBy(item => item.TypeLabel, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(item => item.Kind).ThenByDescending(item => item.TypeLabel, StringComparer.OrdinalIgnoreCase),
            "大小" => FilterSortAscending
                ? items.OrderBy(item => item.Kind).ThenBy(item => item.Asset?.FileSize ?? 0L)
                : items.OrderBy(item => item.Kind).ThenByDescending(item => item.Asset?.FileSize ?? 0L),
            _ => FilterSortAscending // 默认按名称
                ? items.OrderBy(item => item.Kind).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(item => item.Kind).ThenByDescending(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
        };

        foreach (var item in items)
            FilteredExplorerItems.Add(item);

        LoadThumbnailsForCurrentItems();
    }

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
            if (SelectedLibrary?.Id == library.Id)
            {
                WorkspaceTitle = library.Name;
                WorkspaceSummary = library.RootPath;
                AssetSummary = library.Summary;
            }
        }

        // 全部库扫描完成后一次性重建树，避免扫描期间反复整体重建
        // （反复重建会令选中节点引用失效，中央列表停留在旧数据）
        RebuildAssetTree();
        RebuildMetrics();
        OperatorNotice = "全部素材库文件数据已加载完成。";
    }

    public async Task AddLibraryDirectoryAsync(string folderPath, LibraryKind kind = LibraryKind.Standard)
    {
        var library = await CatalogService.AddLibraryAsync(folderPath, kind);
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
        // node 为 null 时回到素材库列表（面包屑根项）
        SelectedAssetTreeNode = node;
    }

    [RelayCommand]
    private void NavigateBack()
    {
        if (_backStack.Count == 0) return;
        _forwardStack.Push(_historyCurrent);
        _historyCurrent = _backStack.Pop();
        _isHistoryNavigation = true;
        SelectedAssetTreeNode = _historyCurrent;
    }

    [RelayCommand]
    private void NavigateForward()
    {
        if (_forwardStack.Count == 0) return;
        _backStack.Push(_historyCurrent);
        _historyCurrent = _forwardStack.Pop();
        _isHistoryNavigation = true;
        SelectedAssetTreeNode = _historyCurrent;
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
        RebuildAssetTree();
        CurrentExplorerItems.Clear();
        FilteredExplorerItems.Clear();
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

    /// <summary>
    /// 修改选中素材类型（视频 ↔ 视频剪辑）。
    /// 转换后旧描述/向量已失效，本地状态同步为「未描述」。
    /// </summary>
    public async Task UpdateSelectedAssetTypeAsync(string newType)
    {
        if (SelectedAsset is null) return;
        await CatalogService.UpdateAssetTypeAsync(SelectedAsset.DatabaseId, newType);

        SelectedAsset.AssetType = newType;
        SelectedAsset.IsDescribed = false;
        SelectedAsset.IsVectorized = false;
        SelectedAsset.Stage = "已识别";
        SelectedAsset.AiState = "未描述";
        SelectedAssetType = newType;
        ResetSelectedAssetDescription();
        SelectedAssetDescriptionStorePath = DescriptionStore?.DatabasePath ?? "SQLite 存储未就绪";
        SelectedAssetDescriptionText = "类型已变更，旧描述已过期，请重新生成描述。";
        RebuildAssetTree();
        RebuildMetrics();
        SyncSelectedAssetFields();
        Log.Information("素材类型已修改: assetUid={AssetUid}, newType={NewType}", SelectedAsset.AssetUid, newType);
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
        SelectedAssetDescriptionState = "已描述（已编辑）";
        SelectedAsset.IsDescribed = true;
        SelectedAsset.AiState = SelectedAssetDescriptionState;
        SelectedAssetAiState = SelectedAssetDescriptionState;
        RefreshDescriptionAngles(SelectedAsset, newDescription);
    }

    // ===== 辅助方法 =====
    public void SetOperatorNotice(string message)
    {
        OperatorNotice = message;
    }

    public IReadOnlyList<ManagedAssetRecord> GetDescriptionSelectionAssets()
    {
        // 右键/范围描述以树节点为准；仅当节点是文件时用 SelectedAsset。
        if (SelectedAssetTreeNode is not null)
        {
            if (SelectedAssetTreeNode.Kind == AssetLibraryTreeNodeKind.File && SelectedAssetTreeNode.Asset is not null)
                return [SelectedAssetTreeNode.Asset];
            if (SelectedAssetTreeNode.Kind == AssetLibraryTreeNodeKind.Library && SelectedAssetTreeNode.Library is not null)
                return AllAssets.Where(a => a.LibraryName == SelectedAssetTreeNode.Library.Name).ToList();
            if (SelectedAssetTreeNode.Kind == AssetLibraryTreeNodeKind.Directory)
            {
                var prefix = NormalizePathPrefix(SelectedAssetTreeNode.FullPath);
                return AllAssets
                    .Where(a => NormalizePathPrefix(a.LocalPath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        if (SelectedAsset is not null)
            return [SelectedAsset];

        return [];
    }

    public IReadOnlyList<ManagedAssetRecord> GetAllLibraryAssets() => AllAssets.ToList();

    public void MarkAssetDescriptionQueued(ManagedAssetRecord asset)
    {
        asset.Stage = "描述中";
        asset.AiState = "描述生成中";
        if (ReferenceEquals(SelectedAsset, asset)) SyncSelectedAssetFields();
        else RebuildAssetTree();
    }

    public void CompleteAssetDescription(ManagedAssetRecord asset, AssetDescriptionDocument document)
    {
        // slicing 模式文档 = 仅完成场景分割（骨架），不算已描述；
        // mock 模式文档 = 未配置可用 LLM Key 的占位响应，也不算已描述。
        var isSlicingOnly = string.Equals(document.Mode, "slicing", StringComparison.OrdinalIgnoreCase);
        var isMock = string.Equals(document.Mode, "mock", StringComparison.OrdinalIgnoreCase);
        asset.Stage = isSlicingOnly
            ? $"已分割 {StructuredDescriptionHelper.GetSegmentCount(document.Description)} 个片段，待描述"
            : isMock
                ? "未配置模型 API Key（mock 占位）"
                : document.Mode == "live" ? "已描述" : "已描述（占位）";
        asset.AiState = asset.Stage;
        asset.IsDescribed = !isSlicingOnly && !isMock;
        if (ReferenceEquals(SelectedAsset, asset))
        {
            ApplySelectedAssetDescription(document);
            SyncSelectedAssetFields();
        }
        else RebuildAssetTree();
        RebuildMetrics();
    }

    public void MarkAssetVectorized(ManagedAssetRecord asset)
    {
        asset.IsVectorized = true;
        if (ReferenceEquals(SelectedAsset, asset))
            SyncSelectedAssetFields();
        else
            RebuildAssetTree();
        RebuildMetrics();
    }

    public void FailAssetDescription(ManagedAssetRecord asset, string error)
    {
        asset.Stage = "描述失败";
        asset.AiState = "调用后端失败";
        if (ReferenceEquals(SelectedAsset, asset)) SyncSelectedAssetFields();
        else RebuildAssetTree();
    }

    /// <summary>场景分割完成：刷新骨架展示（片段卡片显示时间点，但未描述）</summary>
    public async Task RefreshAssetDescriptionAfterSplit(ManagedAssetRecord asset, int segmentCount)
    {
        if (DescriptionStore is null) return;
        try
        {
            // ConfigureAwait(true)：调用方（progress 回调）已派发到 UI 线程，
            // await 后回到 UI 上下文再刷新集合与状态。
            var document = await DescriptionStore.TryGetForAssetAsync(asset).ConfigureAwait(true);
            if (document is null) return;

            asset.Stage = $"已分割 {segmentCount} 个片段";
            asset.AiState = "待描述";
            if (ReferenceEquals(SelectedAsset, asset))
            {
                SelectedAssetDescriptionState = $"已分割 {segmentCount} 个片段，待描述";
                SelectedAssetDescriptionStorePath = DescriptionStore.DatabasePath;
                SelectedAssetDescriptionGeneratedAt = document.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                SelectedAssetDescriptionMode = document.Mode;
                SelectedAssetDescriptionText = "场景分割完成，片段时间点已保存；请执行「描述」补全各片段描述。";
                RefreshDescriptionAngles(asset, document.Description);
                SyncSelectedAssetFields();
            }
            else
            {
                RebuildAssetTree();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "分割后刷新素材描述失败: assetId={AssetId}, assetName={AssetName}", asset.DatabaseId, asset.Name);
            RebuildAssetTree();
        }
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
            SelectedAssetDescriptionStorePath = DescriptionStore?.DatabasePath ?? "SQLite 存储未就绪";
            SelectedAssetDescriptionText = "当前素材的描述记录已删除。";
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
            DescriptionLoadGeneration++;
            SelectedAssetName = "尚未选择素材";
            SelectedAssetLibrary = "请先扫描一个素材库";
            SelectedAssetPath = "当前未加载本地文件路径";
            SelectedAssetType = "未选择";
            SelectedAssetStage = "待选择";
            SelectedAssetAiState = "未描述";
            SelectedAssetDetail = "当前素材还没有可显示的 AI 描述。";
            SelectedAssetSubtype = "";
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
        SelectedAssetSubtype = string.IsNullOrWhiteSpace(value.Subtype) ? "" : value.Subtype;
        ResetSelectedAssetDescription();
        _ = LoadSelectedAssetDescriptionAsync(value);
    }

    private async Task LoadSelectedAssetDescriptionAsync(ManagedAssetRecord asset)
    {
        var generation = ++DescriptionLoadGeneration;

        if (DescriptionStore is null)
        {
            if (generation != DescriptionLoadGeneration || !ReferenceEquals(SelectedAsset, asset))
                return;

            SelectedAssetDescriptionState = "描述存储未就绪";
            SelectedAssetDescriptionStorePath = "SQLite 存储未就绪";
            SelectedAssetDescriptionText = "当前环境尚未注入描述 SQLite 存储。";
            SelectedAssetAiState = "描述存储未就绪";
            return;
        }

        try
        {
            var document = await DescriptionStore.TryGetForAssetAsync(asset).ConfigureAwait(true);
            if (generation != DescriptionLoadGeneration || !ReferenceEquals(SelectedAsset, asset))
                return;

            if (document is null)
            {
                SelectedAssetDescriptionState = "未描述";
                SelectedAssetDescriptionStorePath = DescriptionStore.DatabasePath;
                SelectedAssetDescriptionText = "点击“描述当前素材”后，这里会展示 AI 返回的中文描述。";
                SelectedAssetAiState = asset.IsDescribed ? asset.AiState : "未描述";
                return;
            }

            ApplySelectedAssetDescription(document);
        }
        catch (Exception ex)
        {
            if (generation != DescriptionLoadGeneration || !ReferenceEquals(SelectedAsset, asset))
                return;

            Log.Error(
                ex,
                "读取素材描述失败: assetId={AssetId}, assetUid={AssetUid}, assetName={AssetName}",
                asset.DatabaseId,
                asset.AssetUid,
                asset.Name);
            ResetSelectedAssetDescription();
            SelectedAssetDescriptionState = "描述记录读取失败";
            SelectedAssetDescriptionStorePath = DescriptionStore.DatabasePath;
            SelectedAssetDescriptionText = ex.Message;
            SelectedAssetAiState = "描述读取失败";
        }
    }

    private void ApplySelectedAssetDescription(AssetDescriptionDocument document)
    {
        var tokenUsage = document.TokenUsage is null
            ? "未返回 token 用量"
            : FormatTokenUsage(document.TokenUsage);

        // slicing 模式文档 = 仅完成场景分割（骨架），尚未生成描述文本；
        // mock 模式文档 = 未配置可用 LLM Key 的占位响应。
        var isSlicingOnly = string.Equals(document.Mode, "slicing", StringComparison.OrdinalIgnoreCase);
        var isMock = string.Equals(document.Mode, "mock", StringComparison.OrdinalIgnoreCase);

        SelectedAssetDescriptionState = isSlicingOnly
            ? $"已分割 {StructuredDescriptionHelper.GetSegmentCount(document.Description)} 个片段，待描述"
            : isMock
                ? "未配置模型 API Key（mock 占位）"
                : document.Mode == "live" ? "已描述" : "已描述（占位）";
        SelectedAssetDescriptionStorePath = DescriptionStore?.DatabasePath ?? "SQLite 存储未就绪";
        SelectedAssetDescriptionGeneratedAt = document.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        SelectedAssetDescriptionMode = document.Mode;
        SelectedAssetDescriptionTokenUsage = tokenUsage;
        SelectedAssetDescriptionPrompt = string.IsNullOrWhiteSpace(document.Prompt)
            ? "使用配置中的默认 prompt。"
            : document.Prompt;
        SelectedAssetDescriptionSystemPrompt = string.IsNullOrWhiteSpace(document.SystemPrompt)
            ? "使用配置中的默认 system prompt。"
            : document.SystemPrompt;
        SelectedAssetDescriptionText = isSlicingOnly
            ? "场景分割完成，片段时间点已保存；请执行「描述」补全各片段描述。"
            : isMock
                ? "当前未配置模型 API Key，本次为占位描述；请在 src/backend/.env 配置 DASHSCOPE_API_KEY 后重新描述。"
                : document.PrimaryDescription;
        SelectedAssetAiState = SelectedAssetDescriptionState;
        SelectedAssetDetail = isSlicingOnly ? SelectedAssetDescriptionText : document.PrimaryDescription;

        var subtype = document.Subtype;
        if (string.IsNullOrWhiteSpace(subtype) && SelectedAsset is not null)
            subtype = SelectedAsset.Subtype;
        if (string.IsNullOrWhiteSpace(subtype))
            subtype = "默认";
        SelectedAssetSubtype = subtype;

        if (SelectedAsset is not null)
        {
            // slicing/mock 文档只是分割骨架或占位响应，不算已描述
            SelectedAsset.IsDescribed = !isSlicingOnly && !isMock;
            SelectedAsset.AiState = SelectedAssetDescriptionState;
            // 与 CompleteAssetDescription 保持一致：状态字段始终同步为当前描述状态
            // （slicing→"已分割 N 段，待描述"，mock→"未配置模型 API Key（mock 占位）"）
            SelectedAsset.Stage = SelectedAssetDescriptionState;
            RefreshDescriptionAngles(SelectedAsset, document.Description);
        }
    }

    private void RefreshDescriptionAngles(ManagedAssetRecord asset, string? descriptionJson)
    {
        SelectedAssetDescriptionAngles.Clear();
        SelectedAssetSegmentDescriptionGroups.Clear();
        RefreshSelectedAssetSegments(asset, descriptionJson);
        if (string.IsNullOrWhiteSpace(descriptionJson))
            return;

        var subtype = SelectedAssetSubtype;
        if (string.IsNullOrWhiteSpace(subtype))
            subtype = "默认";

        try
        {
            var tagsByAngle = new Dictionary<string, string[]>(StringComparer.Ordinal);
            try
            {
                using var doc = JsonDocument.Parse(descriptionJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Object
                            && prop.Value.TryGetProperty("tags", out var tagsEl)
                            && tagsEl.ValueKind == JsonValueKind.Array)
                        {
                            tagsByAngle[prop.Name] = tagsEl.EnumerateArray()
                                .Where(t => t.ValueKind == JsonValueKind.String)
                                .Select(t => t.GetString() ?? "")
                                .Where(t => !string.IsNullOrEmpty(t))
                                .ToArray()!;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // tags 解析失败不影响主体描述
            }

            var segments = StructuredDescriptionHelper.ExtractSegments(descriptionJson);
            var profile = AngleProfileManager?.GetProfile(asset.AssetType, subtype);

            foreach (var segment in segments)
            {
                var angleDef = profile?.Angles.FirstOrDefault(a => a.Key == segment.NormalizedAngleType);
                var tags = tagsByAngle.GetValueOrDefault(segment.NormalizedAngleType, []);
                SelectedAssetDescriptionAngles.Add(new AngleDescriptionRecord(
                    AngleKey: segment.NormalizedAngleType,
                    Label: angleDef?.Label ?? segment.NormalizedAngleType,
                    Text: segment.NormalizedText,
                    Tags: tags,
                    MaxLength: angleDef?.MaxLength ?? 120));
            }

            // 剪辑素材：描述详情按片段分组展示（每段时间切片一组，组内含各角度记录）；
            // 未描述的骨架片段保留为「待描述」分组。
            var isClip = string.Equals(asset.AssetType, "视频剪辑", StringComparison.Ordinal);
            if (isClip)
            {
                var groups = new Dictionary<int, SegmentDescriptionGroupViewModel>();
                foreach (var skeleton in StructuredDescriptionHelper.EnumerateSegmentSkeletons(descriptionJson))
                {
                    var group = new SegmentDescriptionGroupViewModel(skeleton.SegmentIndex, skeleton.Start, skeleton.End);
                    groups[skeleton.SegmentIndex] = group;
                    SelectedAssetSegmentDescriptionGroups.Add(group);
                }

                foreach (var segmentRecord in StructuredDescriptionHelper.EnumerateSegmentAngleTexts(descriptionJson))
                {
                    if (!groups.TryGetValue(segmentRecord.SegmentIndex, out var group))
                    {
                        continue;
                    }

                    var angleDef = profile?.Angles.FirstOrDefault(a => a.Key == segmentRecord.AngleType);
                    var tags = tagsByAngle.GetValueOrDefault(segmentRecord.AngleType, []);
                    group.Angles.Add(new AngleDescriptionRecord(
                        AngleKey: SegmentAngleType.Build(segmentRecord.SegmentIndex, segmentRecord.AngleType),
                        Label: angleDef?.Label ?? segmentRecord.AngleType,
                        Text: segmentRecord.Text,
                        Tags: tags,
                        MaxLength: angleDef?.MaxLength ?? 120)
                    {
                        SegmentIndex = segmentRecord.SegmentIndex,
                        StartTime = segmentRecord.StartTime,
                        EndTime = segmentRecord.EndTime,
                    });
                }

                // 有角度文本的片段不再视为待描述；
                // 分组集合已刷新（空态引导依赖此通知）
                foreach (var group in SelectedAssetSegmentDescriptionGroups)
                {
                    group.IsMissing = group.Angles.Count == 0;
                }
            }

            // 角度集合已刷新（空态引导依赖此通知）
            OnPropertyChanged(nameof(SelectedAssetDescriptionAngles));
            OnPropertyChanged(nameof(SelectedAssetSegmentDescriptionGroups));
        }
        catch (Exception ex)
        {
            Log.Debug("解析角度描述失败: {Error}", ex.Message);
        }
    }

    private static string FormatTokenUsage(AssetDescriptionTokenUsage usage)
    {
        var baseText = $"input={usage.InputTokens}, output={usage.OutputTokens}, total={usage.TotalTokens}";
        return usage.ImageTokens is null && usage.VideoTokens is null && usage.AudioTokens is null
            ? baseText
            : $"{baseText}; image={usage.ImageTokens ?? 0}, video={usage.VideoTokens ?? 0}, audio={usage.AudioTokens ?? 0}";
    }

    /// <summary>构建片段列表数据（已分割剪辑素材展示列表项并异步加载缩略图）</summary>
    private void RefreshSelectedAssetSegments(ManagedAssetRecord asset, string? descriptionJson)
    {
        SelectedAssetSegmentItems.Clear();
        var data = SegmentTimelineHelper.Build(descriptionJson);
        if (data is null)
        {
            HasSelectedAssetSegments = false;
            return;
        }

        foreach (var block in data.Blocks)
        {
            SelectedAssetSegmentItems.Add(new SegmentListItemViewModel(block));
        }

        HasSelectedAssetSegments = true;
        LoadSegmentThumbnailsAsync(asset);
    }

    /// <summary>异步加载各片段起始帧缩略图（generation 防选中切换竞态）</summary>
    private async void LoadSegmentThumbnailsAsync(ManagedAssetRecord asset)
    {
        var generation = ++SegmentThumbnailGeneration;
        if (VideoFrameService is null || !File.Exists(asset.LocalPath))
        {
            return;
        }

        var videoPath = asset.LocalPath;
        foreach (var item in SelectedAssetSegmentItems)
        {
            var timestamp = item.Start;
            var jpegBytes = await Task.Run(() => VideoFrameService.ExtractFrame(videoPath, timestamp)).ConfigureAwait(true);
            if (generation != SegmentThumbnailGeneration)
            {
                return;
            }

            item.SetThumbnail(jpegBytes);
        }
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
        SelectedAssetSegmentDescriptionGroups.Clear();
        // 取消进行中的缩略图加载并清空片段列表
        SegmentThumbnailGeneration++;
        SelectedAssetSegmentItems.Clear();
        HasSelectedAssetSegments = false;
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
        Metrics.Add(new DashboardMetric("视频剪辑", AllAssets.Count(a => a.AssetType == "视频剪辑").ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("已描述", AllAssets.Count(a => a.IsDescribed).ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("已向量化", AllAssets.Count(a => a.IsVectorized).ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("待描述", AllAssets.Count(a => !a.IsDescribed).ToString("D2"), ""));
    }

    // ===== 树形导航方法 =====

    private void RebuildAssetTree()
    {
        // 重建前记录当前选中路径：旧树节点引用在重建后失效，需按路径重新解析
        var selectedPath = SelectedAssetTreeNode?.FullPath;
        AssetTreeRoots.Clear();
        foreach (var library in Libraries.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            AssetTreeRoots.Add(BuildLibraryTree(library));

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            UpdateExplorerView(null);
        }
        else if (FindTreeNodeByPath(selectedPath) is { } reResolved)
        {
            // 原地刷新：重新赋值会经 OnChanged 刷新 SelectedAsset（新实例）与详情面板，
            // _isHistoryNavigation 避免把重建过程记入导航历史
            _isHistoryNavigation = true;
            SelectedAssetTreeNode = reResolved;
        }
        else
        {
            // 原选中节点已不存在（目录/素材被删除）：回根视图
            SelectedAssetTreeNode = null;
        }

        _ = RefreshClipSegmentCountsAsync();
    }

    /// <summary>
    /// 为「已分割但未描述」的剪辑素材异步补充片段数状态（卡片显示"已分割 N 段 · 待描述"），
    /// 不阻塞树构建；解析失败或未分割的素材保持原有"未描述"。
    /// </summary>
    private bool _clipCountsRefreshing;
    private bool _clipCountsRefreshQueued;

    private async Task RefreshClipSegmentCountsAsync()
    {
        // 防重入：树重建可能连续触发（扫描/状态同步），合并为一次刷新；
        // 运行期间的新请求在完成后补跑一轮，保证拿到最新树。
        if (_clipCountsRefreshing)
        {
            _clipCountsRefreshQueued = true;
            return;
        }

        _clipCountsRefreshing = true;
        try
        {
            do
            {
                _clipCountsRefreshQueued = false;
                await RefreshClipSegmentCountsCoreAsync();
            }
            while (_clipCountsRefreshQueued);
        }
        finally
        {
            _clipCountsRefreshing = false;
        }
    }

    private async Task RefreshClipSegmentCountsCoreAsync()
    {
        if (DescriptionStore is null)
        {
            return;
        }

        var clipNodes = AssetTreeRoots
            .SelectMany(EnumerateAssetNodes)
            .Where(node => node.Asset is { AssetType: "视频剪辑", IsDescribed: false })
            .ToList();
        if (clipNodes.Count == 0)
        {
            return;
        }

        foreach (var node in clipNodes)
        {
            try
            {
                var document = await DescriptionStore.TryGetForAssetAsync(node.Asset!).ConfigureAwait(true);
                if (document is null)
                {
                    continue;
                }

                var segmentCount = StructuredDescriptionHelper.EnumerateSegmentSkeletons(document.Description).Count();
                if (segmentCount > 0)
                {
                    node.MetaLabel = $"已分割 {segmentCount} 段 · 待描述";
                }
            }
            catch (Exception ex)
            {
                Log.Debug("读取剪辑素材片段数失败: {AssetName}, {Error}", node.DisplayName, ex.Message);
            }
        }
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
        UpdateBreadcrumbs(container);
        if (container is null)
        {
            ExplorerTitle = "素材库";
            ExplorerSummary = "选择一个素材库后，中央区域会显示该库下的目录和文件。";
            ExplorerPath = "未选择";
            CanNavigateUp = false;
            foreach (var r in AssetTreeRoots) CurrentExplorerItems.Add(r);
            ApplyFilterAndSort();
            LoadThumbnailsForCurrentItems();
            return;
        }
        foreach (var item in container.Children) CurrentExplorerItems.Add(item);
        ExplorerTitle = container.DisplayName;
        ExplorerSummary = container.Kind == AssetLibraryTreeNodeKind.Library ? container.Summary : $"{container.MetaLabel} · {container.CategorySummary}";
        ExplorerPath = container.FullPath;
        CanNavigateUp = container.Kind != AssetLibraryTreeNodeKind.Library && FindParentTreeNode(container) is not null;
        ApplyFilterAndSort();
        LoadThumbnailsForCurrentItems();
    }

    /// <summary>
    /// 生成面包屑链条：素材库 › 库根 › 各级目录 › 当前容器，每级可点击跳转。
    /// 链首固定为“素材库”根项，点击返回素材库列表。
    /// </summary>
    private void UpdateBreadcrumbs(AssetLibraryTreeNode? container)
    {
        Breadcrumbs.Clear();

        // 根项：素材库列表入口（Node 为 null，点击后回到库列表）
        Breadcrumbs.Add(new BreadcrumbSegment
        {
            Name = "素材库",
            Node = null,
            IsCurrent = container is null,
            NavigateCommand = OpenExplorerItemCommand,
        });

        if (container is null) return;

        var chain = new List<AssetLibraryTreeNode>();
        var walk = container;
        while (walk is not null && walk.Kind != AssetLibraryTreeNodeKind.Library)
        {
            chain.Insert(0, walk);
            walk = FindParentTreeNode(walk);
        }
        if (walk is not null) chain.Insert(0, walk);

        for (var i = 0; i < chain.Count; i++)
        {
            var node = chain[i];
            Breadcrumbs.Add(new BreadcrumbSegment
            {
                Name = node.DisplayName,
                Node = node,
                IsCurrent = i == chain.Count - 1,
                NavigateCommand = OpenExplorerItemCommand,
            });
        }
    }

    private async void LoadThumbnailsForCurrentItems()
    {
        if (ThumbnailCache is null)
            return;

        // 在 Filtered 集合上也加载：UI 绑定的是 FilteredExplorerItems
        var candidates = FilteredExplorerItems
            .Concat(CurrentExplorerItems)
            .Distinct()
            .Where(item => item.Kind == AssetLibraryTreeNodeKind.File
                           && item.Asset is not null
                           && !item.HasThumbnail
                           && string.Equals(item.Asset.AssetType, "图片", StringComparison.Ordinal))
            .ToList();

        foreach (var item in candidates)
        {
            var thumbnail = await ThumbnailCache.GetThumbnailAsync(
                item.FullPath, item.Asset!.AssetType);
            if (thumbnail is not null)
            {
                // AssetLibraryTreeNode 已实现 INotifyPropertyChanged，直接赋值即可刷新绑定
                item.Thumbnail = thumbnail;
            }
        }
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
        // 树重建后旧节点引用失效（扫描/任务完成等会整体重建树）：
        // 统一按路径重解析到当前树，点击、导航、重建赋值任一入口都生效；
        // 原节点已不存在（目录/素材被删除）时按未选中处理。
        if (value is not null && !string.IsNullOrWhiteSpace(value.FullPath))
        {
            if (FindTreeNodeByPath(value.FullPath) is { } current)
            {
                value = current;
            }
            else
            {
                value = null;
            }
        }

        // 导航历史：普通导航把旧位置压入返回栈并清空前进栈；返回/前进操作不重复记录
        if (!_isHistoryNavigation && !ReferenceEquals(_historyCurrent, value))
        {
            _backStack.Push(_historyCurrent);
            _forwardStack.Clear();
            _historyCurrent = value;
        }
        _isHistoryNavigation = false;
        CanGoBack = _backStack.Count > 0;
        CanGoForward = _forwardStack.Count > 0;

        if (value is null)
        {
            SelectedAsset = null;
            UpdateExplorerView(null);
            return;
        }
        if (value.Library is not null && !ReferenceEquals(SelectedLibrary, value.Library))
            SelectedLibrary = value.Library;
        WorkspaceTitle = value.DisplayName;
        WorkspaceSummary = value.FullPath;
        AssetSummary = value.Summary;
        // 通过 SelectedAsset setter 统一触发详情与描述加载
        SelectedAsset = value.Asset;
        UpdateExplorerView(value);
    }

    partial void OnSelectedAssetChanged(ManagedAssetRecord? value)
    {
        UpdateSelectedAssetDetails(value);
    }
}

/// <summary>空实现，用于设计时模式，不访问数据库</summary>
file sealed class NullAssetLibraryService : IAssetLibraryService
{
    public Task<IReadOnlyList<LibraryWorkspace>> GetLibrariesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LibraryWorkspace>>([]);
    public Task<LibraryWorkspace> AddLibraryAsync(string folderPath, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<LibraryWorkspace> AddLibraryAsync(string folderPath, LibraryKind kind, CancellationToken ct = default)
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
    public Task UpdateAssetTypeAsync(long assetId, string newType, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>面包屑导航段：一个可点击的路径层级。</summary>
public sealed class BreadcrumbSegment
{
    /// <summary>层级显示名称（库名或目录名）</summary>
    public required string Name { get; init; }

    /// <summary>对应的树节点，供点击跳转使用；null 表示素材库列表根项</summary>
    public AssetLibraryTreeNode? Node { get; init; }

    /// <summary>是否为当前所在层级（末项，不可点击）</summary>
    public bool IsCurrent { get; init; }

    /// <summary>点击跳转命令（由素材库工作台注入）</summary>
    public IRelayCommand<AssetLibraryTreeNode?>? NavigateCommand { get; init; }
}