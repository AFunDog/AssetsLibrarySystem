using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.AssetDescription;
using AssetsLibrarySystem.Avalonia.Services.AssetLibrary;
using AssetsLibrarySystem.Avalonia.Services.BackgroundTasks;
using CommunityToolkit.Mvvm.ComponentModel;

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

        WorkspaceTitle = "本地素材工作台";
        WorkspaceSummary = "先登记素材库目录，再扫描本地文件，桌面端负责目录和元数据展示。";
        AssetSummary = "当前还没有扫描结果。选择一个素材库后，点击“扫描当前素材库”加载文件。";
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
        UpdateDescriptionSelectionSummary();
    }

    public async Task InitializeAsync()
    {
        await LoadLibrariesAsync();
    }

    public async Task AddLibraryDirectoryAsync(string folderPath)
    {
        if (AssetLibraryService is null)
        {
            OperatorNotice = "素材库服务尚未注册，当前无法保存目录。";
            return;
        }

        var library = await AssetLibraryService.AddLibraryAsync(folderPath);
        var existing = Libraries.FirstOrDefault(item =>
            string.Equals(item.RootPath, library.RootPath, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            Libraries.Add(library);
            ActivityFeedService.Add($"已登记素材库目录：{library.RootPath}");
            RebuildAssetTree();
        }
        else
        {
            library = existing;
            ActivityFeedService.Add($"素材库目录已存在：{library.RootPath}");
        }

        SelectedLibrary = library;
        OperatorNotice = $"已登记素材库“{library.Name}”，下一步请执行扫描。";
        RebuildMetrics();
        await ScanLibraryAsync(library);
    }

    public Task RefreshSelectedLibraryAsync()
    {
        return SelectedLibrary is null ? Task.CompletedTask : ScanLibraryAsync(SelectedLibrary);
    }

    public Task ScanSelectedLibraryAsync()
    {
        return SelectedLibrary is null ? Task.CompletedTask : ScanLibraryAsync(SelectedLibrary);
    }

    public void MarkSelectedAssetManaged()
    {
        if (SelectedAsset is null)
        {
            OperatorNotice = "请先选择一个素材，再调整其桌面端管理状态。";
            return;
        }

        SelectedAsset.Stage = "桌面端已接管";
        SelectedAsset.AiState = "无需后端素材处理";
        SyncSelectedAssetFields();
        OperatorNotice = $"{SelectedAsset.Name} 已切换到 .NET 素材管理视图。";
        ActivityFeedService.Add($"状态更新：{SelectedAsset.Name} -> 桌面端已接管");
    }

    public IReadOnlyList<ManagedAssetRecord> GetDescriptionSelectionAssets()
    {
        return EnumerateDescriptionSelectionAssets().ToList();
    }

    public void MarkAssetDescriptionQueued(ManagedAssetRecord asset)
    {
        asset.Stage = "描述中";
        asset.AiState = "已发送到 Python HTTP 服务";

        if (ReferenceEquals(SelectedAsset, asset))
        {
            SyncSelectedAssetFields();
        }
        else
        {
            RebuildAssetTree();
        }

        OperatorNotice = $"已为 {asset.Name} 排入描述任务，正在调用后端服务。";
        ActivityFeedService.Add($"描述任务排队：{asset.Name}");
    }

    public void CompleteAssetDescription(ManagedAssetRecord asset, AssetDescriptionDocument document)
    {
        asset.Stage = document.Mode == "live" ? "已描述" : "已描述（占位）";
        asset.AiState = $"SQLite 已保存 · {document.Mode}";

        if (ReferenceEquals(SelectedAsset, asset))
        {
            ApplySelectedAssetDescription(document);
            SyncSelectedAssetFields();
        }
        else
        {
            RebuildAssetTree();
        }

        OperatorNotice = $"描述已写入 SQLite：{document.StorePath}";
        ActivityFeedService.Add($"描述完成：{asset.Name} -> {document.StorePath}");
    }

    public void FailAssetDescription(ManagedAssetRecord asset, string error)
    {
        asset.Stage = "描述失败";
        asset.AiState = "调用后端失败";

        if (ReferenceEquals(SelectedAsset, asset))
        {
            SyncSelectedAssetFields();
        }
        else
        {
            RebuildAssetTree();
        }

        OperatorNotice = $"描述任务失败：{error}";
        ActivityFeedService.Add($"描述失败：{asset.Name} -> {error}");
    }

    public void SetOperatorNotice(string message)
    {
        OperatorNotice = message;
    }

    private async Task LoadLibrariesAsync()
    {
        Libraries.Clear();
        AssetTreeRoots.Clear();
        AllAssets.Clear();

        if (AssetLibraryService is null)
        {
            SetEmptyWorkspaceState();
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
        SelectedAssetTreeNode = FindLibraryTreeNode(firstLibrary.Id);

        _ = LoadAllLibraryDataAsync();
    }

    private Task LoadSelectedLibraryAsync(LibraryWorkspace? library)
    {
        if (library is null)
        {
            SetEmptyWorkspaceState();
            return Task.CompletedTask;
        }

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
            OperatorNotice = "正在后台异步加载全部素材库文件数据...";

            foreach (var library in Libraries.ToList())
            {
                UpdateTask(taskId, $"正在加载素材库：{library.Name}", library.RootPath);
                var assets = await AssetLibraryService.ScanLibraryAsync(library);

                AllAssets.RemoveAll(asset => asset.LibraryName == library.Name);
                AllAssets.AddRange(assets);

                library.AssetCount = assets.Count;
                library.SyncMode = "已加载";
                library.Summary = assets.Count == 0
                    ? "目录中没有找到受支持的文本、图片、视频或音频文件。"
                    : $"已加载 {assets.Count} 个素材文件，可在右侧列表查看。";

                RebuildAssetTree();
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
            CompleteTask(taskId, "全部素材库文件数据已加载完成");
        }
        catch (Exception ex)
        {
            OperatorNotice = $"素材库数据加载失败：{ex.Message}";
            ActivityFeedService.Add($"素材库数据加载失败：{ex.Message}");
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
            library.SyncMode = "扫描中";
            library.Summary = "正在扫描目录下的文本、图片、视频和音频文件。";
            OperatorNotice = $"正在扫描素材库：{library.RootPath}";

            var assets = await AssetLibraryService.ScanLibraryAsync(library);
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
            FailTask(taskId, $"扫描失败：{library.Name}", ex.Message);
        }
        finally
        {
            IsLibraryScanRunning = false;
        }
    }

    private void UpdateSelectedAssetDetails(ManagedAssetRecord? value)
    {
        if (value is null)
        {
            SelectedAssetName = "尚未选择素材";
            SelectedAssetLibrary = "请先扫描一个素材库";
            SelectedAssetPath = "当前未加载本地文件路径";
            SelectedAssetType = "未选择";
            SelectedAssetStage = "待选择";
            SelectedAssetAiState = "未排队";
            SelectedAssetDetail = "右侧详情区域会展示当前素材的路径、类型和扫描结果。";
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

    private IEnumerable<ManagedAssetRecord> EnumerateDescriptionSelectionAssets()
    {
        if (SelectedAsset is not null)
        {
            yield return SelectedAsset;
            yield break;
        }

        if (SelectedAssetTreeNode is null)
        {
            yield break;
        }

        if (SelectedAssetTreeNode.Kind == AssetLibraryTreeNodeKind.Library && SelectedAssetTreeNode.Library is not null)
        {
            foreach (var asset in AllAssets.Where(asset => asset.LibraryName == SelectedAssetTreeNode.Library.Name))
            {
                yield return asset;
            }

            yield break;
        }

        var fullPath = SelectedAssetTreeNode.FullPath;
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            yield break;
        }

        var normalizedPrefix = NormalizePathPrefix(fullPath);
        foreach (var asset in AllAssets.Where(asset => NormalizePathPrefix(asset.LocalPath).StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            yield return asset;
        }
    }

    private void UpdateDescriptionSelectionSummary()
    {
        var assets = EnumerateDescriptionSelectionAssets().ToList();
        if (assets.Count == 0)
        {
            DescriptionSelectionSummary = "请选择左侧素材库、目录或单个素材，再安排描述任务。";
            return;
        }

        DescriptionSelectionSummary = $"{BuildDescriptionSelectionLabel()} · 共 {assets.Count} 个素材可发送到后端描述。";
    }

    private string BuildDescriptionSelectionLabel()
    {
        if (SelectedAsset is not null)
        {
            return $"当前素材：{SelectedAsset.Name}";
        }

        if (SelectedAssetTreeNode?.Kind == AssetLibraryTreeNodeKind.Library)
        {
            return $"当前素材库：{SelectedAssetTreeNode.DisplayName}";
        }

        if (SelectedAssetTreeNode is not null)
        {
            return $"当前目录：{SelectedAssetTreeNode.DisplayName}";
        }

        return "当前选择";
    }

    private void RebuildAssetTree()
    {
        AssetTreeRoots.Clear();
        foreach (var library in Libraries.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            AssetTreeRoots.Add(BuildLibraryTree(library));
        }
    }

    private AssetLibraryTreeNode BuildLibraryTree(LibraryWorkspace library)
    {
        var libraryAssets = AllAssets
            .Where(asset => asset.LibraryName == library.Name)
            .OrderBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var root = new AssetLibraryTreeNode
        {
            DisplayName = library.Name,
            MetaLabel = BuildCountLabel(libraryAssets.Count),
            CategorySummary = BuildCategorySummary(libraryAssets.Select(asset => asset.AssetType)),
            TypeLabel = "素材库",
            StatusLabel = library.SyncMode,
            PathLabel = library.RootPath,
            Summary = library.Summary,
            FullPath = library.RootPath,
            Kind = AssetLibraryTreeNodeKind.Library,
            Library = library
        };

        var directories = new Dictionary<string, AssetLibraryTreeNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in libraryAssets)
        {
            var currentNode = root;
            var relativeSegments = asset.RelativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

            for (var index = 0; index < relativeSegments.Length - 1; index++)
            {
                var folderKey = string.Join('/', relativeSegments.Take(index + 1));
                if (!directories.TryGetValue(folderKey, out var folderNode))
                {
                    folderNode = new AssetLibraryTreeNode
                    {
                        DisplayName = relativeSegments[index],
                        TypeLabel = "目录",
                        PathLabel = folderKey,
                        Summary = $"目录 · {folderKey}",
                        FullPath = Path.Combine(library.RootPath, folderKey.Replace('/', Path.DirectorySeparatorChar)),
                        Kind = AssetLibraryTreeNodeKind.Directory,
                        Library = library
                    };

                    directories[folderKey] = folderNode;
                    currentNode.Children.Add(folderNode);
                }

                currentNode = folderNode;
            }

            currentNode.Children.Add(new AssetLibraryTreeNode
            {
                DisplayName = asset.Name,
                MetaLabel = asset.AssetType,
                CategorySummary = asset.Stage,
                TypeLabel = asset.AssetType,
                StatusLabel = asset.Stage,
                PathLabel = asset.RelativePath,
                Summary = asset.Summary,
                FullPath = asset.LocalPath,
                Kind = AssetLibraryTreeNodeKind.File,
                Library = library,
                Asset = asset
            });
        }

        PopulateDirectoryStatistics(root);
        return root;
    }

    private void PopulateDirectoryStatistics(AssetLibraryTreeNode node)
    {
        foreach (var child in node.Children)
        {
            PopulateDirectoryStatistics(child);
        }

        if (node.Kind == AssetLibraryTreeNodeKind.File)
        {
            return;
        }

        var assetNodes = EnumerateAssetNodes(node).ToList();
        node.MetaLabel = BuildCountLabel(assetNodes.Count);
        node.CategorySummary = BuildCategorySummary(assetNodes.Select(assetNode => assetNode.TypeLabel));
    }

    private IEnumerable<AssetLibraryTreeNode> EnumerateAssetNodes(AssetLibraryTreeNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.Kind == AssetLibraryTreeNodeKind.File)
            {
                yield return child;
                continue;
            }

            foreach (var descendant in EnumerateAssetNodes(child))
            {
                yield return descendant;
            }
        }
    }

    private static string BuildCountLabel(int count)
    {
        return count == 0 ? "空目录" : $"{count} 项";
    }

    private static string BuildCategorySummary(IEnumerable<string> categories)
    {
        var values = categories
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(category => category, StringComparer.Ordinal)
            .ToList();

        return values.Count == 0 ? "暂无素材" : string.Join(" / ", values);
    }

    private void RestoreTreeSelection(LibraryWorkspace library)
    {
        if (SelectedAsset is not null)
        {
            SelectedAssetTreeNode = FindAssetTreeNode(SelectedAsset.Id);
            return;
        }

        SelectedAssetTreeNode = FindLibraryTreeNode(library.Id);
    }

    private AssetLibraryTreeNode? FindLibraryTreeNode(string libraryId)
    {
        return AssetTreeRoots.FirstOrDefault(node => node.Library?.Id == libraryId);
    }

    private AssetLibraryTreeNode? FindAssetTreeNode(string assetId)
    {
        foreach (var root in AssetTreeRoots)
        {
            var match = FindAssetTreeNodeRecursive(root, assetId);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private AssetLibraryTreeNode? FindAssetTreeNodeRecursive(AssetLibraryTreeNode node, string assetId)
    {
        if (node.Asset?.Id == assetId)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var match = FindAssetTreeNodeRecursive(child, assetId);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private void ApplyTreeSelection(AssetLibraryTreeNode? node)
    {
        if (node is null)
        {
            return;
        }

        if (node.Library is not null && !ReferenceEquals(SelectedLibrary, node.Library))
        {
            SuppressLibrarySelectionLoad = true;
            SelectedLibrary = node.Library;
            SuppressLibrarySelectionLoad = false;
        }

        WorkspaceTitle = node.DisplayName;
        WorkspaceSummary = node.FullPath;
        AssetSummary = node.Summary;
        SelectedAsset = node.Asset;

        if (node.Library is not null &&
            node.Asset is null &&
            !AllAssets.Any(asset => asset.LibraryName == node.Library.Name))
        {
            _ = LoadSelectedLibraryAsync(node.Library);
        }
    }

    private void RebuildMetrics()
    {
        Metrics.Clear();

        var totalAssets = AllAssets.Count;
        var pendingModel = AllAssets.Count(asset => asset.AiState.Contains("待", StringComparison.Ordinal));
        var recognizedTypes = AllAssets
            .Select(asset => asset.AssetType)
            .Distinct(StringComparer.Ordinal)
            .Count();

        Metrics.Add(new DashboardMetric("本地素材库", Libraries.Count.ToString("D2"), "Avalonia 侧维护目录登记"));
        Metrics.Add(new DashboardMetric("已扫描素材", totalAssets.ToString("D2"), "文本 | 图片 | 视频 | 音频"));
        Metrics.Add(new DashboardMetric("待模型处理", pendingModel.ToString("D2"), "仅把提示词和推理请求交给 Python"));
        Metrics.Add(new DashboardMetric("素材类型", recognizedTypes.ToString("D2"), "当前已识别的文件类型数量"));
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
        if (SelectedAsset is null)
        {
            return;
        }

        SelectedAssetStage = SelectedAsset.Stage;
        SelectedAssetAiState = SelectedAsset.AiState;
        RebuildAssetTree();
        SelectedAssetTreeNode = FindAssetTreeNode(SelectedAsset.Id);
    }

    private async Task LoadSelectedAssetDescriptionAsync(ManagedAssetRecord? asset)
    {
        if (asset is null)
        {
            ResetSelectedAssetDescription();
            return;
        }

        if (AssetDescriptionStore is null)
        {
            ResetSelectedAssetDescription();
            SelectedAssetDescriptionState = "描述存储未注册";
            SelectedAssetDescriptionStorePath = "SQLite 存储未就绪";
            SelectedAssetDescriptionText = "当前环境尚未注入描述 SQLite 存储。";
            return;
        }

        try
        {
            var document = await AssetDescriptionStore.TryGetAsync(asset.Id);
            if (document is null)
            {
                ResetSelectedAssetDescription();
                SelectedAssetDescriptionState = "当前素材尚未生成 AI 描述";
                SelectedAssetDescriptionStorePath = AssetDescriptionStore.DatabasePath;
                SelectedAssetDescriptionText = "点击“排入描述任务”后，这里会展示 AI 返回的中文描述。";
                return;
            }

            ApplySelectedAssetDescription(document);
        }
        catch (Exception ex)
        {
            ResetSelectedAssetDescription();
            SelectedAssetDescriptionState = "描述记录读取失败";
            SelectedAssetDescriptionStorePath = AssetDescriptionStore.DatabasePath;
            SelectedAssetDescriptionText = ex.Message;
        }
    }

    private void ApplySelectedAssetDescription(AssetDescriptionDocument? document)
    {
        if (document is null)
        {
            ResetSelectedAssetDescription();
            return;
        }

        var tokenUsage = document.TokenUsage is null
            ? "未返回 token 用量"
            : FormatTokenUsage(document.TokenUsage);

        SelectedAssetDescriptionState = document.Mode == "live" ? "已生成" : "已生成（占位）";
        SelectedAssetDescriptionStorePath = document.StorePath;
        SelectedAssetDescriptionGeneratedAt = document.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        SelectedAssetDescriptionMode = document.Mode;
        SelectedAssetDescriptionTokenUsage = tokenUsage;
        SelectedAssetDescriptionPrompt = string.IsNullOrWhiteSpace(document.Prompt)
            ? "使用配置中的默认 prompt。"
            : document.Prompt;
        SelectedAssetDescriptionSystemPrompt = string.IsNullOrWhiteSpace(document.SystemPrompt)
            ? "使用配置中的默认 system prompt。"
            : document.SystemPrompt;
        SelectedAssetDescriptionText = document.Description;
        SelectedAssetAiState = $"SQLite 已保存 · {tokenUsage}";
        SelectedAssetDetail = document.Description;
    }

    private void ResetSelectedAssetDescription()
    {
        SelectedAssetDescriptionState = "未生成 AI 描述";
        SelectedAssetDescriptionStorePath = "尚未生成描述记录";
        SelectedAssetDescriptionGeneratedAt = "未生成";
        SelectedAssetDescriptionMode = "未生成";
        SelectedAssetDescriptionTokenUsage = "未返回 token 用量";
        SelectedAssetDescriptionPrompt = "尚未生成 prompt。";
        SelectedAssetDescriptionSystemPrompt = "尚未生成 system prompt。";
        SelectedAssetDescriptionText = "当前素材还没有可显示的 AI 描述。";
    }

    private static string NormalizePathPrefix(string value)
    {
        return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private void UpdateTask(string? taskId, string stageText, string? detailText = null)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        BackgroundTaskService?.UpdateTask(taskId, stageText, detailText);
    }

    private void CompleteTask(string? taskId, string? stageText = null, string? detailText = null)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        BackgroundTaskService?.CompleteTask(taskId, stageText, detailText);
    }

    private void FailTask(string? taskId, string stageText, string detailText)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        BackgroundTaskService?.FailTask(taskId, detailText, stageText);
    }

    private static string FormatTokenUsage(AssetDescriptionTokenUsage usage)
    {
        var baseText = $"input={usage.InputTokens}, output={usage.OutputTokens}, total={usage.TotalTokens}";
        return usage.ImageTokens is null && usage.VideoTokens is null && usage.AudioTokens is null
            ? baseText
            : $"{baseText}; image={usage.ImageTokens ?? 0}, video={usage.VideoTokens ?? 0}, audio={usage.AudioTokens ?? 0}";
    }
}
