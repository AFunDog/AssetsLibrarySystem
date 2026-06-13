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
        RerankProvider = UserSettingsService.RerankProvider;
        RerankModel = UserSettingsService.RerankModel;
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
    public partial string RerankProvider { get; set; }

    [ObservableProperty]
    public partial string RerankModel { get; set; }

    public string[] ProviderOptions { get; } = ["dashscope", "local"];

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
        if (IsLoadingSettings)
        {
            return;
        }

        UserSettingsService.AutoWarmupEmbeddingModel = value;
        SettingsStatusMessage = value
            ? "已启用 embedding 自动预热，下次启动生效。"
            : "已关闭 embedding 自动预热，下次启动生效。";
    }

    partial void OnAutoWarmupRerankModelChanged(bool value)
    {
        if (IsLoadingSettings)
        {
            return;
        }

        UserSettingsService.AutoWarmupRerankModel = value;
        SettingsStatusMessage = value
            ? "已启用 rerank 自动预热，下次启动生效。"
            : "已关闭 rerank 自动预热，下次启动生效。";
    }

    partial void OnEmbeddingProviderChanged(string value) => SaveSearchModelSettings();
    partial void OnEmbeddingModelChanged(string value) => SaveSearchModelSettings();
    partial void OnRerankProviderChanged(string value) => SaveSearchModelSettings();
    partial void OnRerankModelChanged(string value) => SaveSearchModelSettings();

    private void SaveSearchModelSettings()
    {
        if (IsLoadingSettings)
        {
            return;
        }

        UserSettingsService.EmbeddingProvider = EmbeddingProvider;
        UserSettingsService.EmbeddingModel = EmbeddingModel;
        UserSettingsService.RerankProvider = RerankProvider;
        UserSettingsService.RerankModel = RerankModel;
        SettingsStatusMessage = "搜索模型设置已保存，后续向量化与检索立即使用新设置。";
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
