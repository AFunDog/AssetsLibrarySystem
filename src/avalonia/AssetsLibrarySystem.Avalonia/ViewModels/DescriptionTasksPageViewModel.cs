using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.AssetDescription;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Avalonia.Services.BackgroundTasks;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed class DescriptionTasksPageViewModel : ObservableObject
{
    private BackendSessionService BackendSessionService { get; }
    private LibraryCatalogService LibraryCatalogService { get; }
    private IAssetDescriptionService? AssetDescriptionService { get; }
    private IBackgroundTaskService BackgroundTaskService { get; }
    private ObservableCollection<BackgroundTaskEntry> SourceTasks => BackgroundTaskService.Tasks;

    public DescriptionTasksPageViewModel()
        : this(new BackendSessionService(), new LibraryCatalogService(), null, new BackgroundTaskService(), new ActivityFeedService())
    {
    }

    public DescriptionTasksPageViewModel(
        BackendSessionService backendSessionService,
        LibraryCatalogService libraryCatalogService,
        IAssetDescriptionService? assetDescriptionService,
        IBackgroundTaskService backgroundTaskService,
        ActivityFeedService activityFeedService)
    {
        BackendSessionService = backendSessionService;
        LibraryCatalogService = libraryCatalogService;
        AssetDescriptionService = assetDescriptionService;
        BackgroundTaskService = backgroundTaskService;
        ActivityFeed = activityFeedService.Entries;
        DescriptionTasks = [];

        ScanSelectedLibraryCommand = new AsyncRelayCommand(() => LibraryCatalogService.ScanSelectedLibraryAsync());
        QueueDescriptionsForSelectionCommand = new AsyncRelayCommand(QueueDescriptionsForSelectionAsync);
        QueueDescriptionCommand = new AsyncRelayCommand(QueueDescriptionAsync);

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
