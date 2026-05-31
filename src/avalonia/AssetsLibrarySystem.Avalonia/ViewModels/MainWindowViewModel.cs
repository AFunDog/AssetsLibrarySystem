using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.AssetLibrary;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private IAssetLibraryService? AssetLibraryService { get; }
    private IBackendLauncher? BackendLauncher { get; }
    private List<ManagedAssetRecord> AllAssets { get; } = [];
    private bool SuppressLibrarySelectionLoad { get; set; }
    private bool IsLibraryScanRunning { get; set; }

    public MainWindowViewModel() : this(null, null)
    {
    }

    public MainWindowViewModel(IBackendLauncher? backendLauncher, IAssetLibraryService? assetLibraryService)
    {
        BackendLauncher = backendLauncher;
        AssetLibraryService = assetLibraryService;

        Metrics = new ObservableCollection<DashboardMetric>();
        AssetTreeRoots = [];
        Libraries = new ObservableCollection<LibraryWorkspace>();
        VisibleAssets = new ObservableCollection<ManagedAssetRecord>();
        AiCapabilities = new ObservableCollection<AiCapabilityRecord>();
        SelectedAssetTags = new ObservableCollection<string>();
        ActivityFeed = new ObservableCollection<string>();

        BackendStatusTitle = "Python 模型服务待连接";
        BackendStatusDetail = "桌面端承担素材目录、元数据和工作流编排；Python 只负责 HTTP 模型能力。";
        BackendEndpoint = "http://127.0.0.1:8000";
        WorkspaceTitle = "本地素材工作台";
        WorkspaceSummary = "先登记素材库目录，再扫描本地文件，桌面端负责目录和元数据展示。";
        AssetSummary = "当前还没有扫描结果。选择一个素材库后，点击“扫描当前素材库”加载文件。";
        OperatorNotice = "先在桌面端选择一个文件夹并登记为素材库目录，再触发扫描。";
        PromptDraft = "请基于当前素材生成一段适合检索与人工校对的中文描述。";
        SelectedAssetName = "尚未选择素材";
        SelectedAssetLibrary = "请先添加并扫描一个素材库";
        SelectedAssetPath = "当前未加载本地文件路径";
        SelectedAssetType = "未选择";
        SelectedAssetStage = "待选择";
        SelectedAssetAiState = "未排队";
        SelectedAssetDetail = "右侧详情区域会展示当前素材的路径、类型和扫描结果。";

        SeedStaticData();

        if (BackendLauncher is null && AssetLibraryService is null)
        {
            SeedDesignTimeData();
            return;
        }

        RebuildMetrics();
        SetEmptyWorkspaceState();
    }

    public ObservableCollection<DashboardMetric> Metrics { get; }
    public ObservableCollection<AssetLibraryTreeNode> AssetTreeRoots { get; }
    public ObservableCollection<LibraryWorkspace> Libraries { get; }
    public ObservableCollection<ManagedAssetRecord> VisibleAssets { get; }
    public ObservableCollection<AiCapabilityRecord> AiCapabilities { get; }
    public ObservableCollection<string> SelectedAssetTags { get; }
    public ObservableCollection<string> ActivityFeed { get; }

    [ObservableProperty]
    public partial LibraryWorkspace? SelectedLibrary { get; set; }

    [ObservableProperty]
    public partial string BackendStatusTitle { get; set; }

    [ObservableProperty]
    public partial ManagedAssetRecord? SelectedAsset { get; set; }

    [ObservableProperty]
    public partial string BackendStatusDetail { get; set; }

    [ObservableProperty]
    public partial string BackendEndpoint { get; set; }

    [ObservableProperty]
    public partial string WorkspaceTitle { get; set; }

    [ObservableProperty]
    public partial string WorkspaceSummary { get; set; }

    [ObservableProperty]
    public partial string AssetSummary { get; set; }

    [ObservableProperty]
    public partial string OperatorNotice { get; set; }

    [ObservableProperty]
    public partial string PromptDraft { get; set; }

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
    public partial AssetLibraryTreeNode? SelectedAssetTreeNode { get; set; }

    public async Task InitializeAsync()
    {
        if (BackendLauncher is null)
        {
            BackendStatusTitle = "设计时模式";
            BackendStatusDetail = "当前界面使用桌面端本地逻辑，没有注入 Python 模型服务。";
        }
        else
        {
            await InitializeBackendAsync();
        }

        await LoadLibrariesAsync();
    }

    public async Task AddLibraryDirectoryAsync(string folderPath)
    {
        if (AssetLibraryService is null)
        {
            OperatorNotice = "素材库服务尚未注册，当前无法保存目录。";
            return;
        }

        // 登记目录后立即扫描，这样右侧列表能直接展示内容。
        var library = await AssetLibraryService.AddLibraryAsync(folderPath);
        var existing = Libraries.FirstOrDefault(item =>
            string.Equals(item.RootPath, library.RootPath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            Libraries.Add(library);
            ActivityFeed.Insert(0, $"已登记素材库目录：{library.RootPath}");
            RebuildAssetTree();
        }
        else
        {
            library = existing;
            ActivityFeed.Insert(0, $"素材库目录已存在：{library.RootPath}");
        }

        SelectedLibrary = library;
        OperatorNotice = $"已登记素材库“{library.Name}”，下一步请执行扫描。";
        RebuildMetrics();
        await ScanLibraryCoreAsync(library);
    }

    [RelayCommand]
    private void SelectLibrary(LibraryWorkspace? library)
    {
        if (library is null)
        {
            return;
        }

        SelectedLibrary = library;
    }

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
    }

    partial void OnSelectedAssetTreeNodeChanged(AssetLibraryTreeNode? value)
    {
        ApplyTreeSelection(value);
    }

    private void UpdateSelectedAssetDetails(ManagedAssetRecord? value)
    {
        SelectedAssetTags.Clear();

        if (value is null)
        {
            SelectedAssetName = "尚未选择素材";
            SelectedAssetLibrary = "请先扫描一个素材库";
            SelectedAssetPath = "当前未加载本地文件路径";
            SelectedAssetType = "未选择";
            SelectedAssetStage = "待选择";
            SelectedAssetAiState = "未排队";
            SelectedAssetDetail = "右侧详情区域会展示当前素材的路径、类型和扫描结果。";
            return;
        }

        SelectedAssetName = value.Name;
        SelectedAssetLibrary = value.LibraryName;
        SelectedAssetPath = value.LocalPath;
        SelectedAssetType = value.AssetType;
        SelectedAssetStage = value.Stage;
        SelectedAssetAiState = value.AiState;
        SelectedAssetDetail = value.Summary;
        foreach (var tag in value.Tags)
        {
            SelectedAssetTags.Add(tag);
        }

        PromptDraft = $"请围绕素材“{value.Name}”输出更适合检索的中文描述和标签建议。";
    }

    [RelayCommand]
    private async Task RefreshWorkspaceAsync()
    {
        if (SelectedLibrary is null)
        {
            OperatorNotice = "请先选择一个素材库，再刷新扫描结果。";
            return;
        }

        await ScanLibraryCoreAsync(SelectedLibrary);
    }

    [RelayCommand]
    private async Task ScanSelectedLibraryAsync()
    {
        if (SelectedLibrary is null)
        {
            OperatorNotice = "请先添加或选择一个素材库。";
            return;
        }

        await ScanLibraryCoreAsync(SelectedLibrary);
    }

    [RelayCommand]
    private void QueueDescription()
    {
        if (SelectedAsset is null)
        {
            OperatorNotice = "请先选择一个素材，再把描述生成任务送入 Python 模型服务。";
            return;
        }

        SelectedAsset.Stage = "待模型描述";
        SelectedAsset.AiState = "待发送到 HTTP 服务";
        SyncSelectedAssetFields();
        OperatorNotice = $"已为 {SelectedAsset.Name} 排入描述任务，后续由 Python HTTP 服务处理提示词。";
        ActivityFeed.Insert(0, $"描述任务排队：{SelectedAsset.Name}");
    }

    [RelayCommand]
    private void QueueEmbedding()
    {
        if (SelectedAsset is null)
        {
            OperatorNotice = "请先选择一个素材，再安排索引预处理任务。";
            return;
        }

        SelectedAsset.Stage = "待索引";
        SelectedAsset.AiState = "等待桌面端索引流水线";
        SyncSelectedAssetFields();
        OperatorNotice = $"已把 {SelectedAsset.Name} 标记为待索引，后续可沿着召回 + 精排链路扩展。";
        ActivityFeed.Insert(0, $"索引任务排队：{SelectedAsset.Name}");
    }

    [RelayCommand]
    private void MarkManaged()
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
        ActivityFeed.Insert(0, $"状态更新：{SelectedAsset.Name} -> 桌面端已接管");
    }

    [RelayCommand]
    private void SubmitPrompt()
    {
        if (string.IsNullOrWhiteSpace(PromptDraft))
        {
            OperatorNotice = "请输入要发送给 Python 模型服务的提示词。";
            return;
        }

        var target = SelectedAsset?.Name ?? "当前会话";
        OperatorNotice = BackendLauncher?.IsRunning == true
            ? $"提示词已准备发送到 {BackendEndpoint}，当前先保留为桌面端联动占位。"
            : "Python 模型服务尚未就绪，当前先完成本地素材扫描与管理。";
        ActivityFeed.Insert(0, $"提示词草稿已更新：{target}");
    }

    private async Task InitializeBackendAsync()
    {
        BackendStatusTitle = "Python 模型服务启动中";
        BackendStatusDetail = "正在等待 /health 返回，就绪后桌面端可将提示词任务转发给 HTTP 后端。";

        try
        {
            await BackendLauncher!.StartAsync();
            BackendEndpoint = BackendLauncher.BaseUrl;
            BackendStatusTitle = "Python 模型服务已连接";
            BackendStatusDetail = "模型服务只负责大模型 HTTP 接口，不再承担素材库、文件扫描或目录管理。";
            ActivityFeed.Insert(0, $"模型网关就绪：{BackendEndpoint}");
        }
        catch (Exception ex)
        {
            BackendStatusTitle = "Python 模型服务未就绪";
            BackendStatusDetail = ex.Message;
            OperatorNotice = "后端启动失败，当前仍可继续使用桌面端素材库管理。";
            ActivityFeed.Insert(0, $"模型网关启动失败：{ex.Message}");
        }
    }

    private async Task LoadLibrariesAsync()
    {
        Libraries.Clear();
        AssetTreeRoots.Clear();
        AllAssets.Clear();
        VisibleAssets.Clear();

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
            ActivityFeed.Insert(0, "当前尚未登记素材库目录。");
            return;
        }

        SelectedLibrary = Libraries[0];
        SelectedAssetTreeNode = FindLibraryTreeNode(Libraries[0].Id);
    }

    private async Task LoadSelectedLibraryAsync(LibraryWorkspace? library)
    {
        if (library is null)
        {
            VisibleAssets.Clear();
            SetEmptyWorkspaceState();
            return;
        }

        // 切换库时先刷新右侧摘要，再按需触发扫描或使用缓存结果。
        WorkspaceTitle = library.Name;
        WorkspaceSummary = library.RootPath;
        AssetSummary = library.AssetCount > 0
            ? $"当前素材库已加载 {library.AssetCount} 个支持的素材文件。"
            : library.Summary;

        if (!AllAssets.Any(asset => asset.LibraryName == library.Name))
        {
            await ScanLibraryCoreAsync(library);
            return;
        }

        RebuildVisibleAssets(library);
    }

    private async Task ScanLibraryCoreAsync(LibraryWorkspace library)
    {
        if (AssetLibraryService is null || IsLibraryScanRunning)
        {
            return;
        }

        try
        {
            IsLibraryScanRunning = true;
            // 扫描期间先更新状态，避免界面看起来没有响应。
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

            RebuildVisibleAssets(library);
            RebuildAssetTree();
            RestoreTreeSelection(library);
            RebuildMetrics();
            ActivityFeed.Insert(0, $"扫描完成：{library.Name}，共 {assets.Count} 个素材文件。");
        }
        catch (Exception ex)
        {
            library.SyncMode = "扫描失败";
            library.Summary = ex.Message;
            OperatorNotice = $"扫描失败：{ex.Message}";
            ActivityFeed.Insert(0, $"扫描失败：{library.Name} -> {ex.Message}");
        }
        finally
        {
            IsLibraryScanRunning = false;
        }
    }

    private void RebuildVisibleAssets(LibraryWorkspace? library)
    {
        VisibleAssets.Clear();

        IEnumerable<ManagedAssetRecord> items = AllAssets;
        if (library is not null)
        {
            items = items.Where(asset => asset.LibraryName == library.Name);
        }

        // 右侧按类型和名称排序，便于人工浏览与后续检索。
        foreach (var asset in items.OrderBy(asset => asset.AssetType).ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase))
        {
            VisibleAssets.Add(asset);
        }
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
            var relativeSegments = asset.RelativePath
                .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

            for (var index = 0; index < relativeSegments.Length - 1; index++)
            {
                var folderKey = string.Join('/', relativeSegments.Take(index + 1));
                if (!directories.TryGetValue(folderKey, out var folderNode))
                {
                    folderNode = new AssetLibraryTreeNode
                    {
                        DisplayName = relativeSegments[index],
                        MetaLabel = string.Empty,
                        CategorySummary = string.Empty,
                        TypeLabel = "目录",
                        StatusLabel = string.Empty,
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

        // 这些指标只反映当前桌面端扫描与排队状态。
        var totalAssets = AllAssets.Count;
        var pendingModel = AllAssets.Count(asset => asset.AiState.Contains("待", StringComparison.Ordinal));
        var textImageVideoAudio = AllAssets
            .Select(asset => asset.AssetType)
            .Distinct(StringComparer.Ordinal)
            .Count();

        Metrics.Add(new DashboardMetric("本地素材库", Libraries.Count.ToString("D2"), "Avalonia 侧维护目录登记"));
        Metrics.Add(new DashboardMetric("已扫描素材", totalAssets.ToString("D2"), "文本 | 图片 | 视频 | 音频"));
        Metrics.Add(new DashboardMetric("待模型处理", pendingModel.ToString("D2"), "仅把提示词和推理请求交给 Python"));
        Metrics.Add(new DashboardMetric("素材类型", textImageVideoAudio.ToString("D2"), "当前已识别的文件类型数量"));
    }

    private void SeedStaticData()
    {
        AiCapabilities.Clear();
        AiCapabilities.Add(new AiCapabilityRecord("健康检查", "/health", "供桌面端确认 Python 模型服务是否可达。"));
        AiCapabilities.Add(new AiCapabilityRecord("能力清单", "/api/v1/model/capabilities", "返回当前模型网关的槽位、模式和占位能力。"));
        AiCapabilities.Add(new AiCapabilityRecord("文本生成", "/api/v1/model/generate", "只负责提示词转发与模型输出，不管理素材目录。"));

        ActivityFeed.Clear();
        ActivityFeed.Add("桌面端作为素材管理主入口，先固定本地工作流边界。");
        ActivityFeed.Add("本地素材库目录会持久化为 JSON，并由 .NET 负责目录扫描与文件展示。");
        ActivityFeed.Add("Python 进程仅暴露 HTTP 模型能力，避免再次把素材管理逻辑塞回后端。");
    }

    private void SeedDesignTimeData()
    {
        BackendStatusTitle = "设计时模式";
        BackendStatusDetail = "当前展示的是设计时素材树与右侧详情示例。";
        OperatorNotice = "设计器中使用样例素材库、目录层级和素材节点，便于直接调整 TreeView UI。";

        var musicLibrary = new LibraryWorkspace(
            "lib-music",
            "music",
            @"D:\Data\全资源\music",
            "已扫描 615 个素材文件，可在树中继续展开目录。",
            "已扫描",
            615);

        var illustrationLibrary = new LibraryWorkspace(
            "lib-illustration",
            "illustration",
            @"D:\Assets\Illustrations",
            "已扫描 248 个插画与参考图像。",
            "已扫描",
            248);

        Libraries.Add(musicLibrary);
        Libraries.Add(illustrationLibrary);

        var designAssets = new[]
        {
            new ManagedAssetRecord(
                "asset-001",
                "Boki Boki Literature Club!! .mp3",
                musicLibrary.Name,
                "音频",
                @"DDLC\Boki Boki Literature Club!! .mp3",
                @"D:\Data\全资源\music\DDLC\Boki Boki Literature Club!! .mp3",
                "音频文件 · 8.4 MB · 修改于 2026-05-12 19:42",
                "已扫描",
                "未提交模型",
                ["音频", "mp3", "背景音乐"]),
            new ManagedAssetRecord(
                "asset-002",
                "1.wav",
                musicLibrary.Name,
                "音频",
                @"DDLC\DDLC_PLUS\1.wav",
                @"D:\Data\全资源\music\DDLC\DDLC_PLUS\1.wav",
                "音频文件 · 2.1 MB · 修改于 2026-05-13 09:15",
                "已扫描",
                "已标注",
                ["音频", "wav", "环境音"]),
            new ManagedAssetRecord(
                "asset-003",
                "mahiro_smile_pose.png",
                illustrationLibrary.Name,
                "图片",
                @"characters\mahiro_smile_pose.png",
                @"D:\Assets\Illustrations\characters\mahiro_smile_pose.png",
                "图片文件 · 1.7 MB · 修改于 2026-05-10 16:08",
                "待索引",
                "等待桌面端索引流水线",
                ["图片", "png", "角色立绘"]),
            new ManagedAssetRecord(
                "asset-004",
                "campfire_loop.mp4",
                illustrationLibrary.Name,
                "视频",
                @"scenes\campfire_loop.mp4",
                @"D:\Assets\Illustrations\scenes\campfire_loop.mp4",
                "视频文件 · 24.9 MB · 修改于 2026-05-08 22:31",
                "已扫描",
                "未提交模型",
                ["视频", "mp4", "场景素材"]),
        };

        AllAssets.AddRange(designAssets);
        RebuildVisibleAssets(musicLibrary);
        RebuildAssetTree();
        RebuildMetrics();

        SuppressLibrarySelectionLoad = true;
        SelectedLibrary = musicLibrary;
        SuppressLibrarySelectionLoad = false;

        WorkspaceTitle = musicLibrary.Name;
        WorkspaceSummary = musicLibrary.RootPath;
        AssetSummary = musicLibrary.Summary;

        SelectedAsset = designAssets[1];
        SelectedAssetTreeNode = FindAssetTreeNode(designAssets[1].Id);
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
}
