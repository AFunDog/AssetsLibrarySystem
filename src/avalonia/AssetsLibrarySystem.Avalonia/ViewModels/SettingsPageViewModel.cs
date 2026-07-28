using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private BackendStatusViewModel BackendStatus { get; }
    private IUserSettingsService UserSettingsService { get; }
    private bool IsLoadingSettings { get; set; }

    public SettingsPageViewModel()
        : this(
            new BackendStatusViewModel(),
            new ActivityFeedService(),
            new UserSettingsService())
    {
    }

    public SettingsPageViewModel(
        BackendStatusViewModel backendStatus,
        ActivityFeedService activityFeedService,
        IUserSettingsService userSettingsService)
    {
        BackendStatus = backendStatus;
        UserSettingsService = userSettingsService;
        ActivityFeed = activityFeedService.Entries;
        SettingsStatusMessage = "修改模型名和维度后会自动保存，并立即生效。";

        IsLoadingSettings = true;
        EmbeddingProvider = UserSettingsService.EmbeddingProvider;
        EmbeddingModel = UserSettingsService.EmbeddingModel;
        EmbeddingDimensions = UserSettingsService.EmbeddingDimensions;
        RerankProvider = UserSettingsService.RerankProvider;
        RerankModel = UserSettingsService.RerankModel;
        SearchCandidateTopK = UserSettingsService.SearchCandidateTopK;
        SearchExpandedCandidateTopK = UserSettingsService.SearchExpandedCandidateTopK;
        SearchRerankTopK = UserSettingsService.SearchRerankTopK;
        SearchFinalTopK = UserSettingsService.SearchFinalTopK;
        IsLoadingSettings = false;

        BackendStatus.PropertyChanged += OnDependencyPropertyChanged;
    }

    [ObservableProperty]
    public partial string EmbeddingProvider { get; set; }

    [ObservableProperty]
    public partial string EmbeddingModel { get; set; }

    [ObservableProperty]
    public partial int EmbeddingDimensions { get; set; }

    [ObservableProperty]
    public partial string RerankProvider { get; set; }

    [ObservableProperty]
    public partial string RerankModel { get; set; }

    [ObservableProperty]
    public partial int SearchCandidateTopK { get; set; }

    [ObservableProperty]
    public partial int SearchExpandedCandidateTopK { get; set; }

    [ObservableProperty]
    public partial int SearchRerankTopK { get; set; }

    [ObservableProperty]
    public partial int SearchFinalTopK { get; set; }

    public int[] EmbeddingDimensionOptions { get; } = [2048, 1024, 512];

    public int[] SearchCandidateTopKOptions { get; } = [5, 10, 20, 30, 50, 100];

    public int[] SearchExpandedCandidateTopKOptions { get; } = [20, 50, 100, 160, 250, 500, 1000];

    public int[] SearchRerankTopKOptions { get; } = [5, 10, 20, 30, 50, 100, 200];

    public int[] SearchFinalTopKOptions { get; } = [3, 5, 10, 20, 30, 50];

    [ObservableProperty]
    public partial string SettingsStatusMessage { get; set; }

    public string OperatorNotice => BackendStatus.BackendStatusDetail;
    public string BackendStatusTitle => BackendStatus.BackendStatusTitle;
    public string BackendStatusDetail => BackendStatus.BackendStatusDetail;
    public string BackendEndpoint => BackendStatus.BackendEndpoint;
    public string SearchModelStatusTitle => BackendStatus.SearchModelStatusTitle;
    public string SearchModelStatusStage => BackendStatus.SearchModelStatusStage;
    public string SearchModelStatusDetail => BackendStatus.SearchModelStatusDetail;
    public ObservableCollection<AiCapabilityRecord> AiCapabilities => BackendStatus.AiCapabilities;
    public ObservableCollection<string> ActivityFeed { get; }

    partial void OnEmbeddingModelChanged(string value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.EmbeddingModel = value;
        SettingsStatusMessage = "当前 embedding 模型设置已保存，后续向量化与检索立即使用新设置。";
    }

    partial void OnEmbeddingDimensionsChanged(int value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.EmbeddingDimensions = value;
        SettingsStatusMessage = "当前向量维度已保存，后续向量化与检索立即使用新设置。";
    }

    partial void OnRerankModelChanged(string value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.RerankModel = value;
        SettingsStatusMessage = "当前 rerank 模型设置已保存，后续检索立即使用新设置。";
    }

    partial void OnSearchCandidateTopKChanged(int value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.SearchCandidateTopK = value;
        RefreshSearchParameterFieldsFromSettings();
        SettingsStatusMessage = "检索候选数已保存，后续快速检索立即使用新设置。";
    }

    partial void OnSearchExpandedCandidateTopKChanged(int value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.SearchExpandedCandidateTopK = value;
        RefreshSearchParameterFieldsFromSettings();
        SettingsStatusMessage = "扩展候选数已保存，后续快速检索立即使用新设置。";
    }

    partial void OnSearchRerankTopKChanged(int value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.SearchRerankTopK = value;
        RefreshSearchParameterFieldsFromSettings();
        SettingsStatusMessage = "重排序候选数已保存，后续快速检索立即使用新设置。";
    }

    partial void OnSearchFinalTopKChanged(int value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.SearchFinalTopK = value;
        RefreshSearchParameterFieldsFromSettings();
        SettingsStatusMessage = "最终返回 Top-K 已保存，后续快速检索立即使用新设置。";
    }

    private void RefreshSearchParameterFieldsFromSettings()
    {
        IsLoadingSettings = true;
        SearchCandidateTopK = UserSettingsService.SearchCandidateTopK;
        SearchExpandedCandidateTopK = UserSettingsService.SearchExpandedCandidateTopK;
        SearchRerankTopK = UserSettingsService.SearchRerankTopK;
        SearchFinalTopK = UserSettingsService.SearchFinalTopK;
        IsLoadingSettings = false;
    }

    private void OnDependencyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}