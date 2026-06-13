using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Avalonia.Services.Library;
using AssetsLibrarySystem.Avalonia.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private BackendSessionService BackendSessionService { get; }
    private LibraryCatalogService LibraryCatalogService { get; }
    private IUserSettingsService UserSettingsService { get; }
    private bool IsLoadingSettings { get; set; }

    public SettingsPageViewModel()
        : this(
            new BackendSessionService(),
            new LibraryCatalogService(),
            new ActivityFeedService(),
            new UserSettingsService())
    {
    }

    public SettingsPageViewModel(
        BackendSessionService backendSessionService,
        LibraryCatalogService libraryCatalogService,
        ActivityFeedService activityFeedService,
        IUserSettingsService userSettingsService)
    {
        BackendSessionService = backendSessionService;
        LibraryCatalogService = libraryCatalogService;
        UserSettingsService = userSettingsService;
        ActivityFeed = activityFeedService.Entries;
        PromptDraft = "请基于当前素材生成一段准确、简洁、全面的中文描述。";
        SettingsStatusMessage = "勾选后会保存到本地，并在下次启动时自动预热对应模型。";

        IsLoadingSettings = true;
        AutoWarmupEmbeddingModel = UserSettingsService.AutoWarmupEmbeddingModel;
        AutoWarmupRerankModel = UserSettingsService.AutoWarmupRerankModel;
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

        SubmitPromptCommand = new RelayCommand(SubmitPrompt);
        RefreshModelStatusCommand = new AsyncRelayCommand(RefreshModelStatusAsync);
        CloseEmbeddingModelCommand = new AsyncRelayCommand(() => CloseModelAsync("embedding"));
        CloseRerankModelCommand = new AsyncRelayCommand(() => CloseModelAsync("rerank"));

        BackendSessionService.PropertyChanged += OnDependencyPropertyChanged;
        LibraryCatalogService.PropertyChanged += OnDependencyPropertyChanged;
    }

    [ObservableProperty]
    public partial string PromptDraft { get; set; }

    [ObservableProperty]
    public partial bool AutoWarmupEmbeddingModel { get; set; }

    [ObservableProperty]
    public partial bool AutoWarmupRerankModel { get; set; }

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

    public string[] ProviderOptions { get; } = ["dashscope", "local"];

    public int[] EmbeddingDimensionOptions { get; } = [2048, 1024, 512];

    public int[] SearchCandidateTopKOptions { get; } = [5, 10, 20, 30, 50, 100];

    public int[] SearchExpandedCandidateTopKOptions { get; } = [20, 50, 100, 160, 250, 500, 1000];

    public int[] SearchRerankTopKOptions { get; } = [5, 10, 20, 30, 50, 100, 200];

    public int[] SearchFinalTopKOptions { get; } = [3, 5, 10, 20, 30, 50];

    [ObservableProperty]
    public partial string SettingsStatusMessage { get; set; }

    public string OperatorNotice => LibraryCatalogService.OperatorNotice;
    public string BackendStatusTitle => BackendSessionService.BackendStatusTitle;
    public string BackendStatusDetail => BackendSessionService.BackendStatusDetail;
    public string BackendEndpoint => BackendSessionService.BackendEndpoint;
    public string SearchModelStatusTitle => BackendSessionService.SearchModelStatusTitle;
    public string SearchModelStatusStage => BackendSessionService.SearchModelStatusStage;
    public string SearchModelStatusDetail => BackendSessionService.SearchModelStatusDetail;
    public ObservableCollection<AiCapabilityRecord> AiCapabilities => BackendSessionService.AiCapabilities;
    public ObservableCollection<string> ActivityFeed { get; }
    public IRelayCommand SubmitPromptCommand { get; }
    public IAsyncRelayCommand RefreshModelStatusCommand { get; }
    public IAsyncRelayCommand CloseEmbeddingModelCommand { get; }
    public IAsyncRelayCommand CloseRerankModelCommand { get; }

    partial void OnAutoWarmupEmbeddingModelChanged(bool value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.AutoWarmupEmbeddingModel = value;
        SettingsStatusMessage = value
            ? "已启用 embedding 自动预热，下次启动生效。"
            : "已关闭 embedding 自动预热，下次启动生效。";
    }

    partial void OnAutoWarmupRerankModelChanged(bool value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.AutoWarmupRerankModel = value;
        SettingsStatusMessage = value
            ? "已启用 rerank 自动预热，下次启动生效。"
            : "已关闭 rerank 自动预热，下次启动生效。";
    }

    partial void OnEmbeddingProviderChanged(string value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.EmbeddingProvider = value;
        RefreshEmbeddingFieldsFromSettings();
        SettingsStatusMessage = $"已切换 embedding 来源为 {UserSettingsService.EmbeddingProvider}，并恢复该来源上次使用的模型与维度。";
    }

    partial void OnEmbeddingModelChanged(string value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.EmbeddingModel = value;
        SettingsStatusMessage = "当前 embedding 来源的模型设置已保存，后续向量化与检索立即使用新设置。";
    }

    partial void OnEmbeddingDimensionsChanged(int value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.EmbeddingDimensions = value;
        SettingsStatusMessage = "当前 embedding 来源的向量维度已保存，后续向量化与检索立即使用新设置。";
    }

    partial void OnRerankProviderChanged(string value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.RerankProvider = value;
        RefreshRerankFieldsFromSettings();
        SettingsStatusMessage = $"已切换 rerank 来源为 {UserSettingsService.RerankProvider}，并恢复该来源上次使用的模型。";
    }

    partial void OnRerankModelChanged(string value)
    {
        if (IsLoadingSettings) return;
        UserSettingsService.RerankModel = value;
        SettingsStatusMessage = "当前 rerank 来源的模型设置已保存，后续检索立即使用新设置。";
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

    private void RefreshEmbeddingFieldsFromSettings()
    {
        IsLoadingSettings = true;
        EmbeddingProvider = UserSettingsService.EmbeddingProvider;
        EmbeddingModel = UserSettingsService.EmbeddingModel;
        EmbeddingDimensions = UserSettingsService.EmbeddingDimensions;
        IsLoadingSettings = false;
    }

    private void RefreshRerankFieldsFromSettings()
    {
        IsLoadingSettings = true;
        RerankProvider = UserSettingsService.RerankProvider;
        RerankModel = UserSettingsService.RerankModel;
        IsLoadingSettings = false;
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

    private void SubmitPrompt()
    {
        if (string.IsNullOrWhiteSpace(PromptDraft))
        {
            LibraryCatalogService.SetOperatorNotice("请输入要发送给 Python 模型服务的提示词。");
            return;
        }

        LibraryCatalogService.SetOperatorNotice(BackendSessionService.IsBackendReady
            ? $"提示词已准备发送到 {BackendSessionService.BackendEndpoint}，当前先保留为桌面端联动占位。"
            : "Python 模型服务尚未就绪，当前先完成本地素材扫描与管理。");
        ActivityFeed.Insert(0, "提示词草稿已更新：当前会话");
    }

    private async Task RefreshModelStatusAsync()
    {
        try
        {
            await BackendSessionService.RefreshSearchModelStatusAsync();
            LibraryCatalogService.SetOperatorNotice("已刷新本地搜索模型状态。");
            ActivityFeed.Insert(0, "刷新本地搜索模型状态。");
        }
        catch (System.Exception ex)
        {
            LibraryCatalogService.SetOperatorNotice($"刷新本地搜索模型状态失败：{ex.Message}");
            ActivityFeed.Insert(0, $"刷新本地搜索模型状态失败：{ex.Message}");
        }
    }

    private async Task CloseModelAsync(string modelKind)
    {
        try
        {
            var result = await BackendSessionService.CloseSearchModelAsync(modelKind);
            LibraryCatalogService.SetOperatorNotice(
                result.Closed
                    ? $"已关闭 {result.ModelKind} 模型，释放后端显存缓存。"
                    : $"{result.ModelKind} 模型当前未驻留，无需关闭。");
            ActivityFeed.Insert(0, $"关闭模型：{result.ModelKind} -> {(result.Closed ? "已释放" : "未驻留")}");
        }
        catch (System.Exception ex)
        {
            LibraryCatalogService.SetOperatorNotice($"关闭本地搜索模型失败：{ex.Message}");
            ActivityFeed.Insert(0, $"关闭本地搜索模型失败：{ex.Message}");
        }
    }

    private void OnDependencyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
