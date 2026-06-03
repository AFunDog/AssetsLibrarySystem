using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed class DescriptionTasksPageViewModel : ObservableObject
{
    private BackendSessionService BackendSessionService { get; }
    private LibraryCatalogService LibraryCatalogService { get; }
    private IAssetDescriptionService? AssetDescriptionService { get; }
    private IAssetDescriptionStore? AssetDescriptionStore { get; }
    private IAssetDescriptionVectorStore? AssetDescriptionVectorStore { get; }
    private IAssetTextVectorizationService? AssetTextVectorizationService { get; }
    private IAssetSearchService? AssetSearchService { get; }
    private IBackgroundTaskService BackgroundTaskService { get; }
    private ObservableCollection<BackgroundTaskEntry> SourceTasks => BackgroundTaskService.Tasks;

    public DescriptionTasksPageViewModel()
        : this(
            new BackendSessionService(),
            new LibraryCatalogService(),
            null,
            new AssetDescriptionStore(),
            new AssetDescriptionVectorStore(),
            new AssetTextVectorizationService(),
            null,
            new BackgroundTaskService(),
            new ActivityFeedService())
    {
    }

    public DescriptionTasksPageViewModel(
        BackendSessionService backendSessionService,
        LibraryCatalogService libraryCatalogService,
        IAssetDescriptionService? assetDescriptionService,
        IAssetDescriptionStore? assetDescriptionStore,
        IAssetDescriptionVectorStore? assetDescriptionVectorStore,
        IAssetTextVectorizationService? assetTextVectorizationService,
        IAssetSearchService? assetSearchService,
        IBackgroundTaskService backgroundTaskService,
        ActivityFeedService activityFeedService)
    {
        BackendSessionService = backendSessionService;
        LibraryCatalogService = libraryCatalogService;
        AssetDescriptionService = assetDescriptionService;
        AssetDescriptionStore = assetDescriptionStore;
        AssetDescriptionVectorStore = assetDescriptionVectorStore;
        AssetTextVectorizationService = assetTextVectorizationService;
        AssetSearchService = assetSearchService;
        BackgroundTaskService = backgroundTaskService;
        ActivityFeed = activityFeedService.Entries;
        DescriptionTasks = [];

        ScanSelectedLibraryCommand = new AsyncRelayCommand(() => LibraryCatalogService.ScanSelectedLibraryAsync());
        QueueDescriptionsForSelectionCommand = new AsyncRelayCommand(QueueDescriptionsForSelectionAsync);
        QueueDescriptionCommand = new AsyncRelayCommand(QueueDescriptionAsync);
        VectorizeSelectedDescriptionCommand = new AsyncRelayCommand(VectorizeSelectedDescriptionAsync);
        RebuildSearchIndexCommand = new AsyncRelayCommand(RebuildSearchIndexAsync);

        BackendSessionService.PropertyChanged += OnDependencyPropertyChanged;
        LibraryCatalogService.PropertyChanged += OnDependencyPropertyChanged;
        SourceTasks.CollectionChanged += OnBackgroundTasksChanged;
        RefreshDescriptionTasks();
    }

    public string BackendStatusTitle => BackendSessionService.BackendStatusTitle;
    public string BackendStatusStage => BackendSessionService.BackendStatusStage;
    public string WorkspaceTitle => LibraryCatalogService.WorkspaceTitle;
    public string WorkspaceSummary => LibraryCatalogService.WorkspaceSummary;
    public string DescriptionSelectionSummary => LibraryCatalogService.DescriptionSelectionSummary;
    public ObservableCollection<AssetLibraryTreeNode> AssetTreeRoots => LibraryCatalogService.AssetTreeRoots;
    public AssetLibraryTreeNode? SelectedAssetTreeNode
    {
        get => LibraryCatalogService.SelectedAssetTreeNode;
        set => LibraryCatalogService.SelectedAssetTreeNode = value;
    }

    public string SelectedAssetName => LibraryCatalogService.SelectedAssetName;
    public string SelectedAssetPath => LibraryCatalogService.SelectedAssetPath;
    public string SelectedAssetStage => LibraryCatalogService.SelectedAssetStage;
    public string SelectedAssetAiState => LibraryCatalogService.SelectedAssetAiState;
    public ObservableCollection<BackgroundTaskEntry> DescriptionTasks { get; }
    public ObservableCollection<string> ActivityFeed { get; }
    public IAsyncRelayCommand ScanSelectedLibraryCommand { get; }
    public IAsyncRelayCommand QueueDescriptionsForSelectionCommand { get; }
    public IAsyncRelayCommand QueueDescriptionCommand { get; }
    public IAsyncRelayCommand VectorizeSelectedDescriptionCommand { get; }
    public IAsyncRelayCommand RebuildSearchIndexCommand { get; }

    public Task AddLibraryDirectoryAsync(string folderPath)
    {
        return LibraryCatalogService.AddLibraryDirectoryAsync(folderPath);
    }

    public void RevealInFileExplorer(AssetLibraryTreeNode? node)
    {
        if (node is null || string.IsNullOrWhiteSpace(node.FullPath))
        {
            LibraryCatalogService.SetOperatorNotice("当前节点没有可打开的本地路径。");
            return;
        }

        var path = Path.GetFullPath(node.FullPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
            Arguments = node.Kind == AssetLibraryTreeNodeKind.File ? $"/select,\"{path}\"" : $"\"{path}\"",
        });

        LibraryCatalogService.SetOperatorNotice($"已在文件资源管理器中显示：{path}");
        ActivityFeed.Insert(0, $"资源管理器定位：{node.DisplayName}");
    }

    private async Task QueueDescriptionAsync()
    {
        var asset = LibraryCatalogService.SelectedAsset;
        if (asset is null)
        {
            LibraryCatalogService.SetOperatorNotice("请先选择一个素材，再把描述生成任务送入 Python 模型服务。");
            return;
        }

        await DescribeAssetAsync(asset);
    }

    private async Task VectorizeSelectedDescriptionAsync()
    {
        var assets = LibraryCatalogService.GetDescriptionSelectionAssets();
        if (assets.Count == 0)
        {
            LibraryCatalogService.SetOperatorNotice("当前选中范围内没有可向量化的素材。");
            return;
        }

        if (!BackendSessionService.IsBackendReady)
        {
            LibraryCatalogService.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            return;
        }

        if (AssetDescriptionStore is null || AssetDescriptionVectorStore is null || AssetTextVectorizationService is null)
        {
            LibraryCatalogService.SetOperatorNotice("向量化服务未注册，当前无法执行手动向量化。");
            return;
        }

        LibraryCatalogService.SetOperatorNotice($"正在批量向量化当前范围：{assets.Count} 个素材");
        ActivityFeed.Insert(0, $"开始批量向量化：{assets.Count} 个素材");

        try
        {
            var successCount = 0;
            var skipCount = 0;
            var failCount = 0;

            foreach (var asset in assets)
            {
                var descriptionDocument = await AssetDescriptionStore.TryGetForAssetAsync(asset);
                if (descriptionDocument is null)
                {
                    skipCount++;
                    ActivityFeed.Insert(0, $"跳过向量化：{asset.Name}（尚未生成描述）");
                    continue;
                }

                if (await AssetDescriptionVectorStore.TryGetAsync(asset.Id) is not null)
                {
                    skipCount++;
                    ActivityFeed.Insert(0, $"跳过向量化：{asset.Name}（向量已存在）");
                    continue;
                }

                try
                {
                    var vectorDocument = await AssetTextVectorizationService.VectorizeAsync(descriptionDocument, BackendSessionService.BaseUrl);
                    await AssetDescriptionVectorStore.SaveAsync(vectorDocument);
                    successCount++;
                    ActivityFeed.Insert(0, $"向量化完成：{asset.Name}");
                }
                catch (Exception ex)
                {
                    failCount++;
                    ActivityFeed.Insert(0, $"向量化失败：{asset.Name} -> {ex.Message}");
                }
            }

            LibraryCatalogService.SetOperatorNotice(
                $"批量向量化完成：成功 {successCount}，跳过 {skipCount}，失败 {failCount}。");
            ActivityFeed.Insert(0, $"批量向量化完成：成功 {successCount}，跳过 {skipCount}，失败 {failCount}");
        }
        catch (Exception ex)
        {
            LibraryCatalogService.SetOperatorNotice($"手动向量化失败：{ex.Message}");
            ActivityFeed.Insert(0, $"批量向量化失败：{ex.Message}");
        }
    }

    private async Task RebuildSearchIndexAsync()
    {
        if (AssetSearchService is null)
        {
            LibraryCatalogService.SetOperatorNotice("检索服务未注册，当前无法重建索引。");
            return;
        }

        if (!BackendSessionService.IsBackendReady)
        {
            LibraryCatalogService.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            return;
        }

        LibraryCatalogService.SetOperatorNotice("正在重建后端向量索引...");
        ActivityFeed.Insert(0, "开始重建后端向量索引。");

        try
        {
            var response = await AssetSearchService.ReindexAsync(BackendSessionService.BaseUrl);
            LibraryCatalogService.SetOperatorNotice($"索引已重建：{response.DocumentCount} 条，{response.VectorDim} 维。");
            ActivityFeed.Insert(0, $"索引重建完成：{response.DocumentCount} 条素材描述。");
        }
        catch (Exception ex)
        {
            LibraryCatalogService.SetOperatorNotice($"索引重建失败：{ex.Message}");
            ActivityFeed.Insert(0, $"索引重建失败：{ex.Message}");
        }
    }

    private async Task QueueDescriptionsForSelectionAsync()
    {
        var assets = LibraryCatalogService.GetDescriptionSelectionAssets();
        if (assets.Count == 0)
        {
            LibraryCatalogService.SetOperatorNotice("当前选中范围内没有可发送到后端的素材。");
            return;
        }

        if (!BackendSessionService.IsBackendReady)
        {
            LibraryCatalogService.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            return;
        }

        if (AssetDescriptionService is null)
        {
            LibraryCatalogService.SetOperatorNotice("描述服务未注册，当前无法调用后端。");
            return;
        }

        LibraryCatalogService.SetOperatorNotice($"已将 {assets.Count} 个素材排入后端描述任务。");
        ActivityFeed.Insert(0, $"批量描述任务排队，共 {assets.Count} 个素材");

        foreach (var asset in assets)
        {
            await DescribeAssetAsync(asset);
        }
    }

    private async Task DescribeAssetAsync(ManagedAssetRecord asset)
    {
        if (!BackendSessionService.IsBackendReady)
        {
            LibraryCatalogService.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            return;
        }

        if (AssetDescriptionService is null)
        {
            LibraryCatalogService.SetOperatorNotice("描述服务未注册，当前无法调用后端。");
            return;
        }

        LibraryCatalogService.MarkAssetDescriptionQueued(asset);
        var taskId = BackgroundTaskService.BeginTask("素材描述", $"正在生成素材描述：{asset.Name}", asset.LocalPath);

        try
        {
            var document = await AssetDescriptionService.DescribeAsync(asset, BackendSessionService.BaseUrl, null, null);
            LibraryCatalogService.CompleteAssetDescription(asset, document);
            BackgroundTaskService.CompleteTask(taskId, $"描述完成：{asset.Name}", document.StorePath);
        }
        catch (Exception ex)
        {
            LibraryCatalogService.FailAssetDescription(asset, ex.Message);
            BackgroundTaskService.FailTask(taskId, ex.Message, $"描述失败：{asset.Name}");
        }
    }

    private void OnDependencyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }

    private void OnBackgroundTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshDescriptionTasks();
    }

    private void RefreshDescriptionTasks()
    {
        DescriptionTasks.Clear();
        foreach (var task in SourceTasks.Where(task => string.Equals(task.Title, "素材描述", StringComparison.Ordinal)))
        {
            DescriptionTasks.Add(task);
        }
    }
}
