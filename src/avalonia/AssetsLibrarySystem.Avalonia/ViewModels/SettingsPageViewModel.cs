using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private BackendSessionService BackendSessionService { get; }
    private LibraryCatalogService LibraryCatalogService { get; }

    public SettingsPageViewModel()
        : this(new BackendSessionService(), new LibraryCatalogService(), new ActivityFeedService())
    {
    }

    public SettingsPageViewModel(
        BackendSessionService backendSessionService,
        LibraryCatalogService libraryCatalogService,
        ActivityFeedService activityFeedService)
    {
        BackendSessionService = backendSessionService;
        LibraryCatalogService = libraryCatalogService;
        ActivityFeed = activityFeedService.Entries;
        PromptDraft = "请基于当前素材生成一段准确、简洁、全面的中文描述。";
        SubmitPromptCommand = new RelayCommand(SubmitPrompt);
        RefreshModelStatusCommand = new AsyncRelayCommand(RefreshModelStatusAsync);
        CloseEmbeddingModelCommand = new AsyncRelayCommand(() => CloseModelAsync("embedding"));
        CloseRerankModelCommand = new AsyncRelayCommand(() => CloseModelAsync("rerank"));

        BackendSessionService.PropertyChanged += OnDependencyPropertyChanged;
        LibraryCatalogService.PropertyChanged += OnDependencyPropertyChanged;
    }

    [ObservableProperty]
    public partial string PromptDraft { get; set; }

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
