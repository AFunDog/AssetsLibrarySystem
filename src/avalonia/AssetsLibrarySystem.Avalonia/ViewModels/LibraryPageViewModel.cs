using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Avalonia.Services.Library;
using AssetsLibrarySystem.Application.UseCases.AssetOperations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class LibraryPageViewModel : ObservableObject
{
    private BackendSessionService BackendSessionService { get; }
    private LibraryCatalogService LibraryCatalogService { get; }
    private IAssetSearchService? AssetSearchService { get; }
    private DescribeAssetsUseCase? DescribeAssetsUseCase { get; }
    private DeleteAssetDescriptionUseCase? DeleteAssetDescriptionUseCase { get; }
    private RebuildSearchIndexUseCase? RebuildSearchIndexUseCase { get; }

    public LibraryPageViewModel()
        : this(
            new BackendSessionService(),
            new LibraryCatalogService(),
            null,
            null,
            null,
            null,
            new ActivityFeedService())
    {
    }

    public LibraryPageViewModel(
        BackendSessionService backendSessionService,
        LibraryCatalogService libraryCatalogService,
        IAssetSearchService? assetSearchService,
        DescribeAssetsUseCase? describeAssetsUseCase,
        DeleteAssetDescriptionUseCase? deleteAssetDescriptionUseCase,
        RebuildSearchIndexUseCase? rebuildSearchIndexUseCase,
        ActivityFeedService activityFeedService)
    {
        BackendSessionService = backendSessionService;
        LibraryCatalogService = libraryCatalogService;
        AssetSearchService = assetSearchService;
        DescribeAssetsUseCase = describeAssetsUseCase;
        DeleteAssetDescriptionUseCase = deleteAssetDescriptionUseCase;
        RebuildSearchIndexUseCase = rebuildSearchIndexUseCase;
        ActivityFeed = activityFeedService.Entries;
        SearchResults = [];

        SearchQuery = "紧张氛围的音乐";
        SearchCandidateTopKText = "20";
        SearchFinalTopKText = "5";
        SearchStatus = "尚未执行素材检索。";
        SearchIndexSummary = "尚未刷新本地检索状态。";
        SearchIndexDetail = "当前由桌面端直接读取 asset_descriptions.db 做本地召回，Python 后端只负责 embedding 和 rerank。";

        ScanSelectedLibraryCommand = new AsyncRelayCommand(() => LibraryCatalogService.ScanSelectedLibraryAsync());
        ExecuteSearchCommand = new AsyncRelayCommand(ExecuteSearchAsync);
        RebuildSearchIndexCommand = new AsyncRelayCommand(RebuildSearchIndexAsync);
        DeleteSelectedDescriptionCommand = new AsyncRelayCommand(DeleteSelectedDescriptionAsync);
        OpenLibraryCommand = new RelayCommand<LibraryWorkspace?>(SelectLibrary);
        OpenExplorerItemCommand = new RelayCommand<AssetLibraryTreeNode?>(OpenExplorerItem);
        NavigateUpCommand = new RelayCommand(NavigateUp);

        BackendSessionService.PropertyChanged += OnDependencyPropertyChanged;
        LibraryCatalogService.PropertyChanged += OnDependencyPropertyChanged;
        Log.Debug(
            "LibraryPageViewModel 已创建，searchServiceRegistered={HasSearchService}, descriptionServiceRegistered={HasDescriptionService}",
            AssetSearchService is not null,
            DescribeAssetsUseCase is not null);
    }

    [ObservableProperty]
    public partial string SearchQuery { get; set; }

    [ObservableProperty]
    public partial string SearchCandidateTopKText { get; set; }

    [ObservableProperty]
    public partial string SearchFinalTopKText { get; set; }

    [ObservableProperty]
    public partial string SearchAssetFormat { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchStatus { get; set; }

    [ObservableProperty]
    public partial string SearchIndexSummary { get; set; }

    [ObservableProperty]
    public partial string SearchIndexDetail { get; set; }

    public string BackendStatusTitle => BackendSessionService.BackendStatusTitle;
    public string BackendStatusStage => BackendSessionService.BackendStatusStage;
    public string WorkspaceTitle => LibraryCatalogService.WorkspaceTitle;
    public string WorkspaceSummary => LibraryCatalogService.WorkspaceSummary;
    public ObservableCollection<LibraryWorkspace> Libraries => LibraryCatalogService.Libraries;
    public ObservableCollection<AssetLibraryTreeNode> AssetTreeRoots => LibraryCatalogService.AssetTreeRoots;
    public ObservableCollection<AssetLibraryTreeNode> CurrentExplorerItems => LibraryCatalogService.CurrentExplorerItems;
    public string ExplorerTitle => LibraryCatalogService.ExplorerTitle;
    public string ExplorerSummary => LibraryCatalogService.ExplorerSummary;
    public string ExplorerPath => LibraryCatalogService.ExplorerPath;
    public bool CanNavigateUp => LibraryCatalogService.CanNavigateUp;
    public AssetLibraryTreeNode? SelectedAssetTreeNode
    {
        get => LibraryCatalogService.SelectedAssetTreeNode;
        set => LibraryCatalogService.SelectedAssetTreeNode = value;
    }

    public LibraryWorkspace? SelectedLibrary => LibraryCatalogService.SelectedLibrary;
    public string SelectedAssetName => LibraryCatalogService.SelectedAssetName;
    public string SelectedAssetLibrary => LibraryCatalogService.SelectedAssetLibrary;
    public string SelectedAssetPath => LibraryCatalogService.SelectedAssetPath;
    public string SelectedAssetType => LibraryCatalogService.SelectedAssetType;
    public string SelectedAssetStage => LibraryCatalogService.SelectedAssetStage;
    public string SelectedAssetAiState => LibraryCatalogService.SelectedAssetAiState;
    public string SelectedAssetDetail => LibraryCatalogService.SelectedAssetDetail;
    public string SelectedAssetDescriptionState => LibraryCatalogService.SelectedAssetDescriptionState;
    public string SelectedAssetDescriptionGeneratedAt => LibraryCatalogService.SelectedAssetDescriptionGeneratedAt;
    public string SelectedAssetDescriptionText => LibraryCatalogService.SelectedAssetDescriptionText;
    public string SelectedAssetDescriptionStorePath => LibraryCatalogService.SelectedAssetDescriptionStorePath;
    public string SelectedAssetDescriptionMode => LibraryCatalogService.SelectedAssetDescriptionMode;
    public string SelectedAssetDescriptionTokenUsage => LibraryCatalogService.SelectedAssetDescriptionTokenUsage;
    public string SelectedAssetDescriptionPrompt => LibraryCatalogService.SelectedAssetDescriptionPrompt;
    public string SelectedAssetDescriptionSystemPrompt => LibraryCatalogService.SelectedAssetDescriptionSystemPrompt;
    public ObservableCollection<AssetSearchDocument> SearchResults { get; }
    public ObservableCollection<string> ActivityFeed { get; }
    public IAsyncRelayCommand ScanSelectedLibraryCommand { get; }
    public IAsyncRelayCommand ExecuteSearchCommand { get; }
    public IAsyncRelayCommand RebuildSearchIndexCommand { get; }
    public IAsyncRelayCommand DeleteSelectedDescriptionCommand { get; }
    public IRelayCommand<LibraryWorkspace?> OpenLibraryCommand { get; }
    public IRelayCommand<AssetLibraryTreeNode?> OpenExplorerItemCommand { get; }
    public IRelayCommand NavigateUpCommand { get; }

    public Task AddLibraryDirectoryAsync(string folderPath)
    {
        Log.Information("用户操作: 添加素材库目录，folderPath={FolderPath}", folderPath);
        return LibraryCatalogService.AddLibraryDirectoryAsync(folderPath);
    }

    public void RevealInFileExplorer(AssetLibraryTreeNode? node)
    {
        if (node is null || string.IsNullOrWhiteSpace(node.FullPath))
        {
            LibraryCatalogService.SetOperatorNotice("当前节点没有可打开的本地路径。");
            Log.Warning("资源管理器定位失败：节点没有可用路径。");
            return;
        }

        var path = Path.GetFullPath(node.FullPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
            Arguments = node.Kind == AssetLibraryTreeNodeKind.File ? $"/select,\"{path}\"" : $"\"{path}\"",
        };

        Process.Start(startInfo);
        LibraryCatalogService.SetOperatorNotice($"已在文件资源管理器中显示：{path}");
        ActivityFeed.Insert(0, $"资源管理器定位：{node.DisplayName}");
        Log.Information("资源管理器定位成功: nodeName={NodeName}, nodeKind={NodeKind}, path={Path}", node.DisplayName, node.Kind, path);
    }

    public void RevealSearchResultInExplorer(AssetSearchDocument? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.AssetPath))
        {
            LibraryCatalogService.SetOperatorNotice("当前搜索结果没有可打开的本地路径。");
            Log.Warning("搜索结果定位失败：没有可用路径。");
            return;
        }

        var path = Path.GetFullPath(result.AssetPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
            Arguments = $"/select,\"{path}\"",
        });

        LibraryCatalogService.SetOperatorNotice($"已在文件资源管理器中显示：{path}");
        ActivityFeed.Insert(0, $"搜索结果定位：{result.AssetName}");
        Log.Information("搜索结果定位成功: assetName={AssetName}, assetPath={AssetPath}", result.AssetName, path);
    }

    private void OpenExplorerItem(AssetLibraryTreeNode? node)
    {
        if (node is null)
        {
            return;
        }

        LibraryCatalogService.SelectedAssetTreeNode = node;
    }

    private void NavigateUp()
    {
        LibraryCatalogService.NavigateUpExplorer();
    }

    public void SelectLibrary(LibraryWorkspace? library)
    {
        if (library is null)
        {
            return;
        }

        LibraryCatalogService.SelectLibrary(library);
    }

    public async Task QueueDescriptionForNodeAsync(AssetLibraryTreeNode? node)
    {
        if (node is null)
        {
            LibraryCatalogService.SetOperatorNotice("请先选择一个素材库、目录或素材文件。");
            return;
        }

        LibraryCatalogService.SelectedAssetTreeNode = node;
        var assets = LibraryCatalogService.GetDescriptionSelectionAssets();
        if (assets.Count == 0)
        {
            LibraryCatalogService.SetOperatorNotice("当前节点下没有可发送到后端描述的素材。");
            Log.Warning(
                "右键加入描述任务失败：节点下没有素材，nodeName={NodeName}, nodeKind={NodeKind}, path={Path}",
                node.DisplayName,
                node.Kind,
                node.FullPath);
            return;
        }

        if (!BackendSessionService.IsBackendReady)
        {
            LibraryCatalogService.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            Log.Warning("右键加入描述任务失败：后端未就绪，assetCount={AssetCount}", assets.Count);
            return;
        }

        if (DescribeAssetsUseCase is null)
        {
            LibraryCatalogService.SetOperatorNotice("描述服务未注册，当前无法调用后端。");
            Log.Warning("右键加入描述任务失败：描述服务未注册，assetCount={AssetCount}", assets.Count);
            return;
        }

        LibraryCatalogService.SetOperatorNotice($"已将 {assets.Count} 个素材排入后端描述任务。");
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
            LibraryCatalogService.SetOperatorNotice("请右键具体素材文件，再删除它的描述记录。");
            return;
        }

        LibraryCatalogService.SelectedAssetTreeNode = node;
        await DeleteDescriptionForAssetAsync(node.Asset);
    }

    private async Task ExecuteSearchAsync()
    {
        if (!BackendSessionService.IsBackendReady)
        {
            LibraryCatalogService.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            SearchStatus = "后端未就绪，无法执行检索。";
            Log.Warning("用户触发库页检索，但后端未就绪。");
            return;
        }

        if (AssetSearchService is null)
        {
            LibraryCatalogService.SetOperatorNotice("检索服务未注册，当前无法调用后端。");
            SearchStatus = "检索服务未注册。";
            Log.Warning("库页检索失败：检索服务未注册。");
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            LibraryCatalogService.SetOperatorNotice("请输入要检索的文本描述。");
            SearchStatus = "请输入查询文本。";
            Log.Warning("库页检索失败：查询文本为空。");
            return;
        }

        if (!TryParsePositiveInt(SearchCandidateTopKText, out var candidateTopK))
        {
            LibraryCatalogService.SetOperatorNotice("候选数量必须是大于 0 的整数。");
            SearchStatus = "候选数量格式错误。";
            Log.Warning("库页检索失败：候选数量格式错误，value={Value}", SearchCandidateTopKText);
            return;
        }

        if (!TryParsePositiveInt(SearchFinalTopKText, out var finalTopK))
        {
            LibraryCatalogService.SetOperatorNotice("返回数量必须是大于 0 的整数。");
            SearchStatus = "返回数量格式错误。";
            Log.Warning("库页检索失败：返回数量格式错误，value={Value}", SearchFinalTopKText);
            return;
        }

        SearchStatus = "正在执行向量召回与重排序...";
        LibraryCatalogService.SetOperatorNotice($"正在检索：“{SearchQuery}”");
        ActivityFeed.Insert(0, $"开始检索：{SearchQuery}");
        Log.Information(
            "用户在库页发起检索: queryLength={QueryLength}, candidateTopK={CandidateTopK}, finalTopK={FinalTopK}, assetFormat={AssetFormat}",
            SearchQuery.Length,
            candidateTopK,
            finalTopK,
            string.IsNullOrWhiteSpace(SearchAssetFormat) ? "全部" : SearchAssetFormat);

        try
        {
            var response = await AssetSearchService.SearchAsync(
                BackendSessionService.BaseUrl,
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
            LibraryCatalogService.SetOperatorNotice(SearchStatus);
            ActivityFeed.Insert(0, $"检索完成：{SearchQuery} -> {response.Results.Length} 条结果");
            Log.Information(
                "库页检索完成: resultCount={ResultCount}, embeddingModel={EmbeddingModel}, rerankModel={RerankModel}",
                response.Results.Length,
                response.EmbeddingModel,
                response.RerankModel);
        }
        catch (Exception ex)
        {
            SearchStatus = $"检索失败：{ex.Message}";
            LibraryCatalogService.SetOperatorNotice(SearchStatus);
            ActivityFeed.Insert(0, $"检索失败：{SearchQuery} -> {ex.Message}");
            Log.Error(ex, "库页检索失败。");
        }
    }

    private async Task RebuildSearchIndexAsync()
    {
        if (RebuildSearchIndexUseCase is null)
        {
            SearchIndexSummary = "检索服务未注册，无法重建索引。";
            LibraryCatalogService.SetOperatorNotice(SearchIndexSummary);
            Log.Warning("用户触发索引重建，但检索服务未注册。");
            return;
        }

        SearchIndexSummary = "正在重建向量索引...";
        SearchIndexDetail = "桌面端会重新扫描本地 SQLite 向量数据；当前阶段不再依赖 Python 后端读库建索引。";
        LibraryCatalogService.SetOperatorNotice(SearchIndexSummary);
        ActivityFeed.Insert(0, "开始重建向量索引。");
        Log.Information("用户触发向量索引重建。");

        try
        {
            await BackendSessionService.EnsureRunningAsync();
            var response = await RebuildSearchIndexUseCase.ExecuteAsync(BackendSessionService.BaseUrl);
            SearchIndexSummary = $"索引已重建：{response.DocumentCount} 条，{response.VectorDim} 维。";
            SearchIndexDetail = $"数据库：{response.DatabasePath}\n索引：{response.IndexPath}\n元数据：{response.MetadataPath}\n模型：{string.Join(", ", response.EmbeddingModels)}";
            LibraryCatalogService.SetOperatorNotice(SearchIndexSummary);
            ActivityFeed.Insert(0, $"索引重建完成：{response.DocumentCount} 条素材描述。");
            Log.Information(
                "索引重建完成: documentCount={DocumentCount}, vectorDim={VectorDim}, databasePath={DatabasePath}, indexPath={IndexPath}",
                response.DocumentCount,
                response.VectorDim,
                response.DatabasePath,
                response.IndexPath);
        }
        catch (Exception ex)
        {
            SearchIndexSummary = $"索引重建失败：{ex.Message}";
            SearchIndexDetail = ex.Message;
            LibraryCatalogService.SetOperatorNotice(SearchIndexSummary);
            ActivityFeed.Insert(0, $"索引重建失败：{ex.Message}");
            Log.Error(ex, "索引重建失败。");
        }
    }

    private async Task DescribeAssetsAsync(IReadOnlyList<ManagedAssetRecord> assets)
    {
        if (!BackendSessionService.IsBackendReady)
        {
            LibraryCatalogService.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            return;
        }

        if (DescribeAssetsUseCase is null)
        {
            LibraryCatalogService.SetOperatorNotice("描述服务未注册，当前无法调用后端。");
            return;
        }

        await DescribeAssetsUseCase.ExecuteAsync(
            assets,
            BackendSessionService.BaseUrl,
            progress: progress =>
            {
                if (progress.Kind == DescribeAssetProgressKind.Queued)
                {
                    LibraryCatalogService.MarkAssetDescriptionQueued(progress.Asset);
                }
                else if (progress.Kind == DescribeAssetProgressKind.Completed && progress.Document is not null)
                {
                    LibraryCatalogService.CompleteAssetDescription(progress.Asset, progress.Document);
                }
                else if (progress.Kind == DescribeAssetProgressKind.Failed && progress.Error is not null)
                {
                    LibraryCatalogService.FailAssetDescription(progress.Asset, progress.Error.Message);
                }

                return Task.CompletedTask;
            });
    }

    private async Task DeleteSelectedDescriptionAsync()
    {
        var asset = LibraryCatalogService.SelectedAsset;
        if (asset is null)
        {
            LibraryCatalogService.SetOperatorNotice("请先选择一个素材，再删除它的描述记录。");
            return;
        }

        await DeleteDescriptionForAssetAsync(asset);
    }

    private async Task DeleteDescriptionForAssetAsync(ManagedAssetRecord asset)
    {

        if (DeleteAssetDescriptionUseCase is null)
        {
            LibraryCatalogService.SetOperatorNotice("描述删除服务未注册，当前无法删除描述记录。");
            return;
        }

        try
        {
            var result = await DeleteAssetDescriptionUseCase.ExecuteAsync(asset);
            if (!result.DeletedAny)
            {
                LibraryCatalogService.SetOperatorNotice($"当前素材没有可删除的描述记录：{asset.Name}");
                ActivityFeed.Insert(0, $"描述删除跳过：{asset.Name} 没有记录");
                return;
            }

            LibraryCatalogService.RemoveAssetDescription(asset, result.VectorDeleted);
            ActivityFeed.Insert(0, $"描述删除完成：{asset.Name}");
        }
        catch (Exception ex)
        {
            LibraryCatalogService.SetOperatorNotice($"删除描述失败：{ex.Message}");
            ActivityFeed.Insert(0, $"描述删除失败：{asset.Name} -> {ex.Message}");
            Log.Error(ex, "删除素材描述失败: assetUid={AssetUid}, assetName={AssetName}", asset.AssetUid, asset.Name);
        }
    }

    private void OnDependencyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
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
}
