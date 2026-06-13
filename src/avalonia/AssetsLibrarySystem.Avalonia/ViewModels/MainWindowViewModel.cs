using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;

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
            new SettingsPageViewModel())
    {
    }

    public MainWindowViewModel(
        BackendSessionService backendSessionService,
        LibraryCatalogService libraryCatalogService,
        IBackgroundTaskService backgroundTaskService,
        OverviewPageViewModel overviewPage,
        LibraryPageViewModel libraryPage,
        SettingsPageViewModel settingsPage)
    {
        BackendSessionService = backendSessionService;
        LibraryCatalogService = libraryCatalogService;
        BackgroundTaskService = backgroundTaskService;
        OverviewPage = overviewPage;
        LibraryPage = libraryPage;
        SettingsPage = settingsPage;

        BackgroundTaskService.PropertyChanged += OnBackgroundTaskServicePropertyChanged;
        BackgroundTaskService.Tasks.CollectionChanged += OnBackgroundTasksCollectionChanged;
        BackendSessionService.PropertyChanged += OnBackendSessionPropertyChanged;

        foreach (var task in BackgroundTaskService.Tasks)
        {
            task.PropertyChanged += OnBackgroundTaskPropertyChanged;
        }

        RefreshBackgroundTaskSummary();
    }

    public OverviewPageViewModel OverviewPage { get; }
    public LibraryPageViewModel LibraryPage { get; }
    public SettingsPageViewModel SettingsPage { get; }
    public ObservableCollection<BackgroundTaskEntry> BackgroundTasks => BackgroundTaskService.Tasks;

    public string BackendStatusTitle => BackendSessionService.BackendStatusTitle;
    public string BackendStatusStage => BackendSessionService.BackendStatusStage;
    public string BackendEndpoint => BackendSessionService.BackendEndpoint;
    public string SearchModelStatusTitle => BackendSessionService.SearchModelStatusTitle;
    public string SearchModelStatusStage => BackendSessionService.SearchModelStatusStage;
    public string SearchModelStatusDetail => BackendSessionService.SearchModelStatusDetail;

    [ObservableProperty]
    public partial string LatestBackgroundTaskText { get; set; } = "暂无后台任务";

    [ObservableProperty]
    public partial bool HasBackgroundTasks { get; set; }

    public async Task InitializeAsync()
    {
        await BackendSessionService.InitializeAsync();
        await LibraryCatalogService.InitializeAsync();
    }

    private void OnBackgroundTaskServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshBackgroundTaskSummary();
    }

    private void OnBackgroundTasksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (BackgroundTaskEntry task in e.OldItems)
            {
                task.PropertyChanged -= OnBackgroundTaskPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (BackgroundTaskEntry task in e.NewItems)
            {
                task.PropertyChanged += OnBackgroundTaskPropertyChanged;
            }
        }

        RefreshBackgroundTaskSummary();
    }

    private void OnBackendSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }

    private void OnBackgroundTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshBackgroundTaskSummary();
    }

    private void RefreshBackgroundTaskSummary()
    {
        HasBackgroundTasks = BackgroundTasks.Count > 0;
        LatestBackgroundTaskText = BackgroundTasks.Count == 0
            ? "暂无后台任务"
            : $"{BackgroundTasks[0].Title} · {BackgroundTasks[0].StageText} · {BackgroundTasks[0].StatusText}";
    }
}
