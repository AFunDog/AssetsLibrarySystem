using System.ComponentModel;
using System.Collections.ObjectModel;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed class OverviewPageViewModel : ObservableObject
{
    private BackendSessionService BackendSessionService { get; }
    private LibraryCatalogService LibraryCatalogService { get; }

    public OverviewPageViewModel()
        : this(new BackendSessionService(), new LibraryCatalogService(), new ActivityFeedService())
    {
    }

    public OverviewPageViewModel(
        BackendSessionService backendSessionService,
        LibraryCatalogService libraryCatalogService,
        ActivityFeedService activityFeedService)
    {
        BackendSessionService = backendSessionService;
        LibraryCatalogService = libraryCatalogService;
        ActivityFeed = activityFeedService.Entries;
        RefreshWorkspaceCommand = new AsyncRelayCommand(() => LibraryCatalogService.RefreshSelectedLibraryAsync());
        MarkManagedCommand = new RelayCommand(LibraryCatalogService.MarkSelectedAssetManaged);

        BackendSessionService.PropertyChanged += OnDependencyPropertyChanged;
        LibraryCatalogService.PropertyChanged += OnDependencyPropertyChanged;
    }

    public string BackendStatusTitle => BackendSessionService.BackendStatusTitle;
    public string BackendStatusStage => BackendSessionService.BackendStatusStage;
    public string BackendStatusDetail => BackendSessionService.BackendStatusDetail;
    public string BackendEndpoint => BackendSessionService.BackendEndpoint;
    public ObservableCollection<DashboardMetric> Metrics => LibraryCatalogService.Metrics;
    public string WorkspaceTitle => LibraryCatalogService.WorkspaceTitle;
    public string WorkspaceSummary => LibraryCatalogService.WorkspaceSummary;
    public string AssetSummary => LibraryCatalogService.AssetSummary;
    public string OperatorNotice => LibraryCatalogService.OperatorNotice;
    public string SelectedAssetName => LibraryCatalogService.SelectedAssetName;
    public string SelectedAssetLibrary => LibraryCatalogService.SelectedAssetLibrary;
    public string SelectedAssetPath => LibraryCatalogService.SelectedAssetPath;
    public string SelectedAssetType => LibraryCatalogService.SelectedAssetType;
    public string SelectedAssetStage => LibraryCatalogService.SelectedAssetStage;
    public string SelectedAssetAiState => LibraryCatalogService.SelectedAssetAiState;
    public string SelectedAssetDetail => LibraryCatalogService.SelectedAssetDetail;
    public ObservableCollection<string> ActivityFeed { get; }
    public IAsyncRelayCommand RefreshWorkspaceCommand { get; }
    public IRelayCommand MarkManagedCommand { get; }

    private void OnDependencyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
