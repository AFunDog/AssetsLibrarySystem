using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class LibraryPageViewModel : ObservableObject
{
    private BackendSessionService BackendSessionService { get; }
    private LibraryCatalogService LibraryCatalogService { get; }

    public LibraryPageViewModel()
        : this(
            new BackendSessionService(),
            new LibraryCatalogService(),
            new LibraryExplorerViewModel(),
            new AssetDetailViewModel(),
            new AssetDescriptionPanelViewModel(),
            new AssetVectorizationPanelViewModel(),
            new AssetSearchPanelViewModel(),
            new ActivityFeedService())
    {
    }

    public LibraryPageViewModel(
        BackendSessionService backendSessionService,
        LibraryCatalogService libraryCatalogService,
        LibraryExplorerViewModel explorer,
        AssetDetailViewModel assetDetail,
        AssetDescriptionPanelViewModel descriptionPanel,
        AssetVectorizationPanelViewModel vectorizationPanel,
        AssetSearchPanelViewModel searchPanel,
        ActivityFeedService activityFeedService)
    {
        BackendSessionService = backendSessionService;
        LibraryCatalogService = libraryCatalogService;
        Explorer = explorer;
        AssetDetail = assetDetail;
        DescriptionPanel = descriptionPanel;
        VectorizationPanel = vectorizationPanel;
        SearchPanel = searchPanel;
        ActivityFeed = activityFeedService.Entries;

        BackendSessionService.PropertyChanged += OnDependencyPropertyChanged;
        LibraryCatalogService.PropertyChanged += OnDependencyPropertyChanged;
        Explorer.PropertyChanged += OnDependencyPropertyChanged;
        AssetDetail.PropertyChanged += OnDependencyPropertyChanged;
        SearchPanel.PropertyChanged += OnDependencyPropertyChanged;
    }

    public LibraryExplorerViewModel Explorer { get; }
    public AssetDetailViewModel AssetDetail { get; }
    public AssetDescriptionPanelViewModel DescriptionPanel { get; }
    public AssetVectorizationPanelViewModel VectorizationPanel { get; }
    public AssetSearchPanelViewModel SearchPanel { get; }

    public string SearchQuery
    {
        get => SearchPanel.SearchQuery;
        set => SearchPanel.SearchQuery = value;
    }

    public string SearchCandidateTopKText
    {
        get => SearchPanel.SearchCandidateTopKText;
        set => SearchPanel.SearchCandidateTopKText = value;
    }

    public string SearchFinalTopKText
    {
        get => SearchPanel.SearchFinalTopKText;
        set => SearchPanel.SearchFinalTopKText = value;
    }

    public string SearchAssetFormat
    {
        get => SearchPanel.SearchAssetFormat;
        set => SearchPanel.SearchAssetFormat = value;
    }

    public string SearchStatus => SearchPanel.SearchStatus;
    public string SearchIndexSummary => SearchPanel.SearchIndexSummary;
    public string SearchIndexDetail => SearchPanel.SearchIndexDetail;

    public string BackendStatusTitle => BackendSessionService.BackendStatusTitle;
    public string BackendStatusStage => BackendSessionService.BackendStatusStage;
    public string WorkspaceTitle => Explorer.WorkspaceTitle;
    public string WorkspaceSummary => Explorer.WorkspaceSummary;
    public ObservableCollection<LibraryWorkspace> Libraries => Explorer.Libraries;
    public ObservableCollection<AssetLibraryTreeNode> AssetTreeRoots => Explorer.AssetTreeRoots;
    public ObservableCollection<AssetLibraryTreeNode> CurrentExplorerItems => Explorer.CurrentExplorerItems;
    public string ExplorerTitle => Explorer.ExplorerTitle;
    public string ExplorerSummary => Explorer.ExplorerSummary;
    public string ExplorerPath => Explorer.ExplorerPath;
    public bool CanNavigateUp => Explorer.CanNavigateUp;

    public AssetLibraryTreeNode? SelectedAssetTreeNode
    {
        get => Explorer.SelectedAssetTreeNode;
        set => Explorer.SelectedAssetTreeNode = value;
    }

    public LibraryWorkspace? SelectedLibrary => Explorer.SelectedLibrary;
    public string SelectedAssetName => AssetDetail.SelectedAssetName;
    public string SelectedAssetLibrary => AssetDetail.SelectedAssetLibrary;
    public string SelectedAssetPath => AssetDetail.SelectedAssetPath;
    public string SelectedAssetType => AssetDetail.SelectedAssetType;
    public string SelectedAssetStage => AssetDetail.SelectedAssetStage;
    public string SelectedAssetAiState => AssetDetail.SelectedAssetAiState;
    public string SelectedAssetDetail => AssetDetail.SelectedAssetDetail;
    public string SelectedAssetDescriptionState => AssetDetail.SelectedAssetDescriptionState;
    public string SelectedAssetDescriptionGeneratedAt => AssetDetail.SelectedAssetDescriptionGeneratedAt;
    public string SelectedAssetDescriptionText => AssetDetail.SelectedAssetDescriptionText;
    public string SelectedAssetDescriptionStorePath => AssetDetail.SelectedAssetDescriptionStorePath;
    public string SelectedAssetDescriptionMode => AssetDetail.SelectedAssetDescriptionMode;
    public string SelectedAssetDescriptionTokenUsage => AssetDetail.SelectedAssetDescriptionTokenUsage;
    public string SelectedAssetDescriptionPrompt => AssetDetail.SelectedAssetDescriptionPrompt;
    public string SelectedAssetDescriptionSystemPrompt => AssetDetail.SelectedAssetDescriptionSystemPrompt;

    public ObservableCollection<AssetSearchDocument> SearchResults => SearchPanel.SearchResults;
    public ObservableCollection<string> ActivityFeed { get; }
    public IAsyncRelayCommand ScanSelectedLibraryCommand => Explorer.ScanSelectedLibraryCommand;
    public IAsyncRelayCommand ExecuteSearchCommand => SearchPanel.ExecuteSearchCommand;
    public IAsyncRelayCommand RebuildSearchIndexCommand => SearchPanel.RebuildSearchIndexCommand;
    public IAsyncRelayCommand QueueDescriptionsForSelectionCommand => DescriptionPanel.QueueDescriptionsForSelectionCommand;
    public IAsyncRelayCommand QueueSelectedDescriptionCommand => DescriptionPanel.QueueSelectedDescriptionCommand;
    public IAsyncRelayCommand VectorizeDescriptionsCommand => VectorizationPanel.VectorizeDescriptionsCommand;
    public IAsyncRelayCommand DeleteSelectedDescriptionCommand => DescriptionPanel.DeleteSelectedDescriptionCommand;
    public IRelayCommand<LibraryWorkspace?> OpenLibraryCommand => Explorer.OpenLibraryCommand;
    public IRelayCommand<AssetLibraryTreeNode?> OpenExplorerItemCommand => Explorer.OpenExplorerItemCommand;
    public IRelayCommand NavigateUpCommand => Explorer.NavigateUpCommand;

    public Task AddLibraryDirectoryAsync(string folderPath) => Explorer.AddLibraryDirectoryAsync(folderPath);

    public void RevealInFileExplorer(AssetLibraryTreeNode? node) => Explorer.RevealInFileExplorer(node);

    public void RevealSearchResultInExplorer(AssetSearchDocument? result) => SearchPanel.RevealSearchResultInExplorer(result);

    public void SelectLibrary(LibraryWorkspace? library) => Explorer.SelectLibrary(library);

    public Task QueueDescriptionForNodeAsync(AssetLibraryTreeNode? node) => DescriptionPanel.QueueDescriptionForNodeAsync(node);

    public Task DeleteDescriptionForNodeAsync(AssetLibraryTreeNode? node) => DescriptionPanel.DeleteDescriptionForNodeAsync(node);

    private void OnDependencyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
