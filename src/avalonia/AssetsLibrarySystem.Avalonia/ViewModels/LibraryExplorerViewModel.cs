using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class LibraryExplorerViewModel : ObservableObject
{
    private LibraryCatalogService LibraryCatalogService { get; }
    private ObservableCollection<string> ActivityFeed { get; }

    public LibraryExplorerViewModel()
        : this(new LibraryCatalogService(), new ActivityFeedService())
    {
    }

    public LibraryExplorerViewModel(
        LibraryCatalogService libraryCatalogService,
        ActivityFeedService activityFeedService)
    {
        LibraryCatalogService = libraryCatalogService;
        ActivityFeed = activityFeedService.Entries;

        ScanSelectedLibraryCommand = new AsyncRelayCommand(() => LibraryCatalogService.ScanSelectedLibraryAsync());
        OpenLibraryCommand = new RelayCommand<LibraryWorkspace?>(SelectLibrary);
        OpenExplorerItemCommand = new RelayCommand<AssetLibraryTreeNode?>(OpenExplorerItem);
        NavigateUpCommand = new RelayCommand(NavigateUp);

        LibraryCatalogService.PropertyChanged += OnCatalogPropertyChanged;
    }

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

    public IAsyncRelayCommand ScanSelectedLibraryCommand { get; }
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

    public void SelectLibrary(LibraryWorkspace? library)
    {
        if (library is null)
        {
            return;
        }

        LibraryCatalogService.SelectLibrary(library);
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

    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
