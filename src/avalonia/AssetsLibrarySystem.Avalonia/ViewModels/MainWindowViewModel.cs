using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.AssetDescription;
using AssetsLibrarySystem.Avalonia.Services.AssetLibrary;
using AssetsLibrarySystem.Avalonia.Services.AssetSearch;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private IAssetLibraryService? AssetLibraryService { get; }
    private IBackendLauncher? BackendLauncher { get; }
    private IAssetDescriptionService? AssetDescriptionService { get; }
    private IAssetDescriptionStore? AssetDescriptionStore { get; }
    private IAssetSearchService? AssetSearchService { get; }
    private List<ManagedAssetRecord> AllAssets { get; } = [];
    private bool SuppressLibrarySelectionLoad { get; set; }
    private bool IsLibraryScanRunning { get; set; }

    public MainWindowViewModel() : this(null, null, null, null, null)
    {
    }

    public MainWindowViewModel(
        IBackendLauncher? backendLauncher,
        IAssetLibraryService? assetLibraryService,
        IAssetDescriptionService? assetDescriptionService,
        IAssetDescriptionStore? assetDescriptionStore,
        IAssetSearchService? assetSearchService)
    {
        BackendLauncher = backendLauncher;
        AssetLibraryService = assetLibraryService;
        AssetDescriptionService = assetDescriptionService;
        AssetDescriptionStore = assetDescriptionStore;
        AssetSearchService = assetSearchService;

        Metrics = new ObservableCollection<DashboardMetric>();
        AssetTreeRoots = [];
        Libraries = new ObservableCollection<LibraryWorkspace>();
        VisibleAssets = new ObservableCollection<ManagedAssetRecord>();
        AiCapabilities = new ObservableCollection<AiCapabilityRecord>();
        SelectedAssetTags = new ObservableCollection<string>();
        ActivityFeed = new ObservableCollection<string>();
        SearchResults = new ObservableCollection<AssetSearchDocument>();

        BackendStatusTitle = "Python 模型服务待连接";
        BackendStatusDetail = "桌面端承担素材目录、元数据和工作流编排；Python 只负责 HTTP 模型能力。";
        BackendEndpoint = "http://127.0.0.1:8000";
        WorkspaceTitle = "本地素材工作台";
        WorkspaceSummary = "先登记素材库目录，再扫描本地文件，桌面端负责目录和元数据展示。";
        AssetSummary = "当前还没有扫描结果。选择一个素材库后，点击“扫描当前素材库”加载文件。";
        OperatorNotice = "先在桌面端选择一个文件夹并登记为素材库目录，再触发扫描。";
        PromptDraft = "请基于当前素材生成一段准确、简洁、全面的中文描述。";
        SelectedAssetName = "尚未选择素材";
        SelectedAssetLibrary = "请先添加并扫描一个素材库";
        SelectedAssetPath = "当前未加载本地文件路径";
        SelectedAssetType = "未选择";
        SelectedAssetStage = "待选择";
        SelectedAssetAiState = "未排队";
        SelectedAssetDetail = "右侧详情区域会展示当前素材的路径、类型和扫描结果。";
        SearchQuery = "紧张氛围的音乐";
        SearchCandidateTopKText = "20";
        SearchFinalTopKText = "5";
        SearchAssetFormat = string.Empty;
        SearchStatus = "尚未执行素材检索。";
        SearchIndexSummary = "尚未重建索引。";
        SearchIndexDetail = "点击“重建向量索引”后，Python 后端会根据 asset_descriptions.db 重新构建 HNSW。";

        SeedStaticData();
        RebuildMetrics();
        SetEmptyWorkspaceState();
        ResetSelectedAssetDescription();
    }

    public ObservableCollection<DashboardMetric> Metrics { get; }
    public ObservableCollection<AssetLibraryTreeNode> AssetTreeRoots { get; }
    public ObservableCollection<LibraryWorkspace> Libraries { get; }
    public ObservableCollection<ManagedAssetRecord> VisibleAssets { get; }
    public ObservableCollection<AiCapabilityRecord> AiCapabilities { get; }
    public ObservableCollection<string> SelectedAssetTags { get; }
    public ObservableCollection<string> ActivityFeed { get; }
    public ObservableCollection<AssetSearchDocument> SearchResults { get; }

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
    public partial AssetDescriptionDocument? SelectedAssetDescription { get; set; }

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
    public partial AssetLibraryTreeNode? SelectedAssetTreeNode { get; set; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; }

    [ObservableProperty]
    public partial string SearchCandidateTopKText { get; set; }

    [ObservableProperty]
    public partial string SearchFinalTopKText { get; set; }

    [ObservableProperty]
    public partial string SearchAssetFormat { get; set; }

    [ObservableProperty]
    public partial string SearchStatus { get; set; }

    [ObservableProperty]
    public partial string SearchIndexSummary { get; set; }

    [ObservableProperty]
    public partial string SearchIndexDetail { get; set; }

    public async Task InitializeAsync()
    {
        if (BackendLauncher is null)
        {
            BackendStatusTitle = "设计时模式";
            BackendStatusDetail = "当前界面使用桌面端本地逻辑，没有注入 Python 模型服务。";
        }
        else
        {
            _ = InitializeBackendAsync();
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
        _ = LoadSelectedAssetDescriptionAsync(value);
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
        foreach (var tag in value.Tags)
        {
            SelectedAssetTags.Add(tag);
        }

        PromptDraft = $"请围绕素材“{value.Name}”输出一段准确、简洁、全面的中文描述。";
        ResetSelectedAssetDescription();
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
    private async Task QueueDescription()
    {
        if (SelectedAsset is null)
        {
            OperatorNotice = "请先选择一个素材，再把描述生成任务送入 Python 模型服务。";
            return;
        }

        if (BackendLauncher?.IsRunning != true)
        {
            OperatorNotice = "Python 模型服务尚未就绪，请先等待后端启动完成。";
            return;
        }

        if (AssetDescriptionService is null)
        {
            OperatorNotice = "描述服务未注册，当前无法调用后端。";
            return;
        }

        SelectedAsset.Stage = "描述中";
        SelectedAsset.AiState = "已发送到 Python HTTP 服务";
        SyncSelectedAssetFields();
        OperatorNotice = $"已为 {SelectedAsset.Name} 排入描述任务，正在调用后端服务。";
        ActivityFeed.Insert(0, $"描述任务排队：{SelectedAsset.Name}");

        try
        {
            var document = await AssetDescriptionService.DescribeAsync(
                SelectedAsset,
                BackendLauncher.BaseUrl,
                prompt: null,
                systemPrompt: null);

            SelectedAsset.Stage = document.Mode == "live" ? "已描述" : "已描述（占位）";
            ApplySelectedAssetDescription(document);
            SyncSelectedAssetFields();
            OperatorNotice = $"描述已写入 SQLite：{document.StorePath}";
            ActivityFeed.Insert(0, $"描述完成：{SelectedAsset.Name} -> {document.StorePath}");
        }
        catch (Exception ex)
        {
            SelectedAsset.Stage = "描述失败";
            SelectedAsset.AiState = "调用后端失败";
            SyncSelectedAssetFields();
            OperatorNotice = $"描述任务失败：{ex.Message}";
            ActivityFeed.Insert(0, $"描述失败：{SelectedAsset.Name} -> {ex.Message}");
        }
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
    private async Task ExecuteSearchAsync()
    {
        if (BackendLauncher?.IsRunning != true)
        {
            OperatorNotice = "Python 模型服务尚未就绪，请先等待后端启动完成。";
            SearchStatus = "后端未就绪，无法执行检索。";
            return;
        }

        if (AssetSearchService is null)
        {
            OperatorNotice = "检索服务未注册，当前无法调用后端。";
            SearchStatus = "检索服务未注册。";
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            OperatorNotice = "请输入要检索的文本描述。";
            SearchStatus = "请输入查询文本。";
            return;
        }

        if (!TryParsePositiveInt(SearchCandidateTopKText, out var candidateTopK))
        {
            OperatorNotice = "候选数量必须是大于 0 的整数。";
            SearchStatus = "候选数量格式错误。";
            return;
        }

        if (!TryParsePositiveInt(SearchFinalTopKText, out var finalTopK))
        {
            OperatorNotice = "返回数量必须是大于 0 的整数。";
            SearchStatus = "返回数量格式错误。";
            return;
        }

        SearchStatus = "正在执行向量召回与重排序...";
        OperatorNotice = $"正在检索：“{SearchQuery}”";
        ActivityFeed.Insert(0, $"开始检索：{SearchQuery}");

        try
        {
            var response = await AssetSearchService.SearchAsync(
                BackendLauncher.BaseUrl,
                SearchQuery,
                candidateTopK,
                finalTopK,
                string.IsNullOrWhiteSpace(SearchAssetFormat) ? null : SearchAssetFormat);

            SearchResults.Clear();
            foreach (var item in response.Results)
            {
                SearchResults.Add(item);
            }

            SearchStatus = $"检索完成：候选 {response.CandidateTopK} 条，返回 {response.Results.Length} 条。";
            OperatorNotice = SearchStatus;
            ActivityFeed.Insert(0, $"检索完成：{SearchQuery} -> {response.Results.Length} 条结果");
        }
        catch (Exception ex)
        {
            SearchStatus = $"检索失败：{ex.Message}";
            OperatorNotice = SearchStatus;
            ActivityFeed.Insert(0, $"检索失败：{SearchQuery} -> {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RebuildSearchIndexAsync()
    {
        if (BackendLauncher is null)
        {
            SearchIndexSummary = "后端启动器未注册，无法重建索引。";
            OperatorNotice = SearchIndexSummary;
            return;
        }

        if (AssetSearchService is null)
        {
            SearchIndexSummary = "检索服务未注册，无法重建索引。";
            OperatorNotice = SearchIndexSummary;
            return;
        }

        SearchIndexSummary = "正在重建向量索引...";
        SearchIndexDetail = "后端会从 asset_descriptions.db 读取向量并重建 HNSW。";
        OperatorNotice = SearchIndexSummary;
        ActivityFeed.Insert(0, "开始重建向量索引。");

        try
        {
            if (BackendLauncher.IsRunning != true)
            {
                await BackendLauncher.StartAsync();
            }

            var response = await AssetSearchService.ReindexAsync(BackendLauncher.BaseUrl);
            SearchIndexSummary = $"索引已重建：{response.DocumentCount} 条，{response.VectorDim} 维。";
            SearchIndexDetail = $"数据库：{response.DatabasePath}\n索引：{response.IndexPath}\n元数据：{response.MetadataPath}\n模型：{string.Join(", ", response.EmbeddingModels)}";
            OperatorNotice = SearchIndexSummary;
            ActivityFeed.Insert(0, $"索引重建完成：{response.DocumentCount} 条素材描述。");
        }
        catch (Exception ex)
        {
            SearchIndexSummary = $"索引重建失败：{ex.Message}";
            SearchIndexDetail = ex.Message;
            OperatorNotice = SearchIndexSummary;
            ActivityFeed.Insert(0, $"索引重建失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private void RevealInFileExplorer(AssetLibraryTreeNode? node)
    {
        if (node is null || string.IsNullOrWhiteSpace(node.FullPath))
        {
            OperatorNotice = "当前节点没有可打开的本地路径。";
            return;
        }

        var path = Path.GetFullPath(node.FullPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
        };

        if (node.Kind == AssetLibraryTreeNodeKind.File)
        {
            startInfo.Arguments = $"/select,\"{path}\"";
        }
        else
        {
            startInfo.Arguments = $"\"{path}\"";
        }

        Process.Start(startInfo);
        OperatorNotice = $"已在文件资源管理器中显示：{path}";
        ActivityFeed.Insert(0, $"资源管理器定位：{node.DisplayName}");
    }

    [RelayCommand]
    private void RevealSearchResultInExplorer(AssetSearchDocument? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.AssetPath))
        {
            OperatorNotice = "当前搜索结果没有可打开的本地路径。";
            return;
        }

        var path = Path.GetFullPath(result.AssetPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
            Arguments = $"/select,\"{path}\"",
        };

        Process.Start(startInfo);
        OperatorNotice = $"已在文件资源管理器中显示：{path}";
        ActivityFeed.Insert(0, $"搜索结果定位：{result.AssetName}");
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

            if (AssetSearchService is not null)
            {
                BackendStatusDetail = "正在预热向量模型与重排序模型，减少第一次检索等待。";
                try
                {
                    var embeddingWarmup = await AssetSearchService.WarmupEmbeddingAsync(BackendEndpoint);
                    var rerankWarmup = await AssetSearchService.WarmupRerankAsync(BackendEndpoint);
                    BackendStatusDetail = $"检索模型已预热：{embeddingWarmup.ModelName} / {rerankWarmup.ModelName}";
                    ActivityFeed.Insert(0, $"检索模型预热完成：{embeddingWarmup.ModelName} / {rerankWarmup.ModelName}");
                }
                catch (Exception ex)
                {
                    BackendStatusDetail = $"模型预热失败：{ex.Message}";
                    ActivityFeed.Insert(0, $"检索模型预热失败：{ex.Message}");
                }
            }
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

        RebuildVisibleAssets(library);
    }

    private async Task LoadAllLibraryDataAsync()
    {
        if (AssetLibraryService is null || Libraries.Count == 0)
        {
            return;
        }

        try
        {
            OperatorNotice = "正在后台异步加载全部素材库文件数据...";

            foreach (var library in Libraries.ToList())
            {
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
                    RebuildVisibleAssets(library);
                    WorkspaceTitle = library.Name;
                    WorkspaceSummary = library.RootPath;
                    AssetSummary = library.Summary;
                }

                ActivityFeed.Insert(0, $"素材库数据已加载：{library.Name}，共 {assets.Count} 个素材文件。");
            }

            if (SelectedLibrary is not null)
            {
                RebuildVisibleAssets(SelectedLibrary);
            }

            OperatorNotice = "全部素材库文件数据已加载完成。";
        }
        catch (Exception ex)
        {
            OperatorNotice = $"素材库数据加载失败：{ex.Message}";
            ActivityFeed.Insert(0, $"素材库数据加载失败：{ex.Message}");
        }
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
        AiCapabilities.Add(new AiCapabilityRecord("向量检索", "/api/v1/search/explore", "输入自然语言后，先召回再重排返回最符合的素材。"));
        AiCapabilities.Add(new AiCapabilityRecord("索引重建", "/api/v1/search/reindex", "从 asset_descriptions.db 重新构建本地 HNSW 索引。"));

        ActivityFeed.Clear();
        ActivityFeed.Add("桌面端作为素材管理主入口，先固定本地工作流边界。");
        ActivityFeed.Add("本地素材库目录会持久化为 JSON，素材描述与向量会写入 SQLite，并由 .NET 负责读取展示。");
        ActivityFeed.Add("Python 进程仅暴露 HTTP 模型能力，包括描述向量化、召回搜索和索引重建。");
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
        SelectedAssetDescription = document;

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
        SelectedAssetDescription = null;
        SelectedAssetDescriptionState = "未生成 AI 描述";
        SelectedAssetDescriptionStorePath = "尚未生成描述记录";
        SelectedAssetDescriptionGeneratedAt = "未生成";
        SelectedAssetDescriptionMode = "未生成";
        SelectedAssetDescriptionTokenUsage = "未返回 token 用量";
        SelectedAssetDescriptionPrompt = "尚未生成 prompt。";
        SelectedAssetDescriptionSystemPrompt = "尚未生成 system prompt。";
        SelectedAssetDescriptionText = "当前素材还没有可显示的 AI 描述。";
    }

    private static bool TryParsePositiveInt(string? value, out int result)
    {
        if (int.TryParse(value, out result) && result > 0)
        {
            return true;
        }

        result = 0;
        return false;
    }

    private static string FormatTokenUsage(AssetDescriptionTokenUsage usage)
    {
        var baseText = $"input={usage.InputTokens}, output={usage.OutputTokens}, total={usage.TotalTokens}";
        return usage.ImageTokens is null && usage.VideoTokens is null && usage.AudioTokens is null
            ? baseText
            : $"{baseText}; image={usage.ImageTokens ?? 0}, video={usage.VideoTokens ?? 0}, audio={usage.AudioTokens ?? 0}";
    }
}
