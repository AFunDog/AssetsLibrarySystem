using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Avalonia.Services.BackgroundTasks;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private BackendSessionService BackendSessionService { get; }
    private LibraryCatalogService LibraryCatalogService { get; }
    private IBackgroundTaskService BackgroundTaskService { get; }

    [Obsolete("仅供设计器使用。运行时请通过 DI 构造。", false)]
    public MainWindowViewModel()
        : this(
            new BackendSessionService(),
            new LibraryCatalogService(),
            new BackgroundTaskService(),
            new OverviewPageViewModel(),
            new LibraryPageViewModel(),
            new DescriptionTasksPageViewModel(),
            new SettingsPageViewModel())
    {
    }

    public MainWindowViewModel(
        BackendSessionService backendSessionService,
        LibraryCatalogService libraryCatalogService,
        IBackgroundTaskService backgroundTaskService,
        OverviewPageViewModel overviewPage,
        LibraryPageViewModel libraryPage,
        DescriptionTasksPageViewModel descriptionTasksPage,
        SettingsPageViewModel settingsPage)
    {
        BackendSessionService = backendSessionService;
        LibraryCatalogService = libraryCatalogService;
        BackgroundTaskService = backgroundTaskService;
        OverviewPage = overviewPage;
        LibraryPage = libraryPage;
        DescriptionTasksPage = descriptionTasksPage;
        SettingsPage = settingsPage;

        ToggleBackgroundTaskPanelCommand = new RelayCommand(ToggleBackgroundTaskPanel);
        CloseBackgroundTaskPanelCommand = new RelayCommand(CloseBackgroundTaskPanel);

        BackgroundTaskService.PropertyChanged += OnBackgroundTaskServicePropertyChanged;
        BackgroundTaskService.Tasks.CollectionChanged += OnBackgroundTasksCollectionChanged;
        BackendSessionService.PropertyChanged += OnBackendSessionPropertyChanged;

        TitleBarTaskText = BackgroundTaskService.ActiveTaskSummary;
        HasTitleBarTask = BackgroundTaskService.HasActiveTaskSummary;
        HasBackgroundTaskEntries = BackgroundTaskService.Tasks.Count > 0;
    }

    public OverviewPageViewModel OverviewPage { get; }
    public LibraryPageViewModel LibraryPage { get; }
    public DescriptionTasksPageViewModel DescriptionTasksPage { get; }
    public SettingsPageViewModel SettingsPage { get; }
    public ObservableCollection<BackgroundTaskEntry> BackgroundTasks => BackgroundTaskService.Tasks;

    public string BackendStatusTitle => BackendSessionService.BackendStatusTitle;
    public string BackendStatusStage => BackendSessionService.BackendStatusStage;
    public string BackendEndpoint => BackendSessionService.BackendEndpoint;
    public string SearchModelStatusTitle => BackendSessionService.SearchModelStatusTitle;
    public string SearchModelStatusStage => BackendSessionService.SearchModelStatusStage;
    public string SearchModelStatusDetail => BackendSessionService.SearchModelStatusDetail;

    [ObservableProperty]
    public partial string TitleBarTaskText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasTitleBarTask { get; set; }

    [ObservableProperty]
    public partial bool HasBackgroundTaskEntries { get; set; }

    [ObservableProperty]
    public partial bool IsBackgroundTaskPanelOpen { get; set; }

    public IRelayCommand ToggleBackgroundTaskPanelCommand { get; }
    public IRelayCommand CloseBackgroundTaskPanelCommand { get; }

    public async Task InitializeAsync()
    {
        await BackendSessionService.InitializeAsync();
        await LibraryCatalogService.InitializeAsync();
    }

    private void ToggleBackgroundTaskPanel()
    {
        if (BackgroundTaskService.Tasks.Count == 0)
        {
            return;
        }

        IsBackgroundTaskPanelOpen = !IsBackgroundTaskPanelOpen;
    }

    private void CloseBackgroundTaskPanel()
    {
        IsBackgroundTaskPanelOpen = false;
    }

    private void OnBackgroundTaskServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IBackgroundTaskService.ActiveTaskSummary))
        {
            TitleBarTaskText = BackgroundTaskService.ActiveTaskSummary;
        }
        else if (e.PropertyName == nameof(IBackgroundTaskService.HasActiveTaskSummary))
        {
            HasTitleBarTask = BackgroundTaskService.HasActiveTaskSummary;
        }
    }

    private void OnBackgroundTasksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasBackgroundTaskEntries = BackgroundTaskService.Tasks.Count > 0;
        if (BackgroundTaskService.Tasks.Count == 0)
        {
            IsBackgroundTaskPanelOpen = false;
        }
    }

    private void OnBackendSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
