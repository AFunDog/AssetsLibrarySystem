using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
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
        DescriptionTasks = [];

        BackgroundTaskService.PropertyChanged += OnBackgroundTaskServicePropertyChanged;
        BackgroundTaskService.Tasks.CollectionChanged += OnBackgroundTasksCollectionChanged;
        BackendSessionService.PropertyChanged += OnBackendSessionPropertyChanged;

        RefreshDescriptionTasks();
    }

    public OverviewPageViewModel OverviewPage { get; }
    public LibraryPageViewModel LibraryPage { get; }
    public SettingsPageViewModel SettingsPage { get; }
    public ObservableCollection<BackgroundTaskEntry> BackgroundTasks => BackgroundTaskService.Tasks;
    public ObservableCollection<BackgroundTaskEntry> DescriptionTasks { get; }

    public string BackendStatusTitle => BackendSessionService.BackendStatusTitle;
    public string BackendStatusStage => BackendSessionService.BackendStatusStage;
    public string BackendEndpoint => BackendSessionService.BackendEndpoint;
    public string SearchModelStatusTitle => BackendSessionService.SearchModelStatusTitle;
    public string SearchModelStatusStage => BackendSessionService.SearchModelStatusStage;
    public string SearchModelStatusDetail => BackendSessionService.SearchModelStatusDetail;

    [ObservableProperty]
    public partial string LatestDescriptionTaskText { get; set; } = "暂无描述任务";

    [ObservableProperty]
    public partial bool HasDescriptionTasks { get; set; }

    public async Task InitializeAsync()
    {
        await BackendSessionService.InitializeAsync();
        await LibraryCatalogService.InitializeAsync();
    }

    private void OnBackgroundTaskServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshDescriptionTaskSummary();
    }

    private void OnBackgroundTasksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (BackgroundTaskEntry task in e.OldItems)
            {
                task.PropertyChanged -= OnDescriptionTaskPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (BackgroundTaskEntry task in e.NewItems)
            {
                task.PropertyChanged += OnDescriptionTaskPropertyChanged;
            }
        }

        RefreshDescriptionTasks();
    }

    private void OnBackendSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }

    private void OnDescriptionTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshDescriptionTaskSummary();
    }

    private void RefreshDescriptionTasks()
    {
        DescriptionTasks.Clear();
        foreach (var task in BackgroundTaskService.Tasks.Where(task =>
                     string.Equals(task.Title, "素材描述", StringComparison.Ordinal)))
        {
            task.PropertyChanged -= OnDescriptionTaskPropertyChanged;
            task.PropertyChanged += OnDescriptionTaskPropertyChanged;
            DescriptionTasks.Add(task);
        }

        RefreshDescriptionTaskSummary();
    }

    private void RefreshDescriptionTaskSummary()
    {
        HasDescriptionTasks = DescriptionTasks.Count > 0;
        LatestDescriptionTaskText = DescriptionTasks.Count == 0
            ? "暂无描述任务"
            : $"{DescriptionTasks[0].StageText} · {DescriptionTasks[0].StatusText}";
    }
}
