using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;
using AssetsLibrarySystem.Avalonia.Services.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private BackendStatusViewModel BackendStatus { get; }
    private LibraryWorkspaceViewModel Workspace { get; }
    private IBackgroundTaskService BackgroundTaskService { get; }
    private IShellWindowService? ShellWindowService { get; }

    [Obsolete("仅供设计器使用。运行时请通过 DI 构造。", false)]
    public MainWindowViewModel()
        : this(
            new BackendStatusViewModel(),
            new LibraryWorkspaceViewModel(),
            new BackgroundTaskService(),
            new OverviewPageViewModel(),
            new LibraryPageViewModel(),
            new SettingsPageViewModel(),
            null)
    {
    }

    public MainWindowViewModel(
        BackendStatusViewModel backendStatus,
        LibraryWorkspaceViewModel workspace,
        IBackgroundTaskService backgroundTaskService,
        OverviewPageViewModel overviewPage,
        LibraryPageViewModel libraryPage,
        SettingsPageViewModel settingsPage,
        IShellWindowService? shellWindowService)
    {
        BackendStatus = backendStatus;
        Workspace = workspace;
        BackgroundTaskService = backgroundTaskService;
        ShellWindowService = shellWindowService;
        OverviewPage = overviewPage;
        LibraryPage = libraryPage;
        SettingsPage = settingsPage;

        BackgroundTaskService.PropertyChanged += OnBackgroundTaskServicePropertyChanged;
        BackgroundTaskService.Tasks.CollectionChanged += OnBackgroundTasksCollectionChanged;
        BackendStatus.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);

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

    public string BackendStatusTitle => BackendStatus.BackendStatusTitle;
    public string BackendStatusStage => BackendStatus.BackendStatusStage;
    public string BackendEndpoint => BackendStatus.BackendEndpoint;
    public string SearchModelStatusTitle => BackendStatus.SearchModelStatusTitle;
    public string SearchModelStatusStage => BackendStatus.SearchModelStatusStage;
    public string SearchModelStatusDetail => BackendStatus.SearchModelStatusDetail;

    [ObservableProperty]
    public partial string LatestBackgroundTaskText { get; set; } = "暂无后台任务";

    [ObservableProperty]
    public partial bool HasBackgroundTasks { get; set; }

    public async Task InitializeAsync()
    {
        await BackendStatus.InitializeAsync();
        await Workspace.InitializeAsync();
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

    [RelayCommand]
    private void ShowQuickSearch()
    {
        ShellWindowService?.ShowQuickSearchWindow();
    }

    [RelayCommand]
    private async Task RefreshWorkspaceAsync()
    {
        if (Workspace is not null)
            await Workspace.ScanSelectedLibraryAsync();
    }
}