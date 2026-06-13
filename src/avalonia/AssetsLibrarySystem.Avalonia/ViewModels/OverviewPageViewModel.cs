using System.ComponentModel;
using System.Collections.ObjectModel;
using AssetsLibrarySystem.Application.Models;
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
        : this(new BackendSessionService(), new LibraryCatalogService())
    {
    }

    public OverviewPageViewModel(
        BackendSessionService backendSessionService,
        LibraryCatalogService libraryCatalogService)
    {
        BackendSessionService = backendSessionService;
        LibraryCatalogService = libraryCatalogService;
        RefreshWorkspaceCommand = new AsyncRelayCommand(() => LibraryCatalogService.RefreshSelectedLibraryAsync());

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
    public IAsyncRelayCommand RefreshWorkspaceCommand { get; }

    private void OnDependencyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
