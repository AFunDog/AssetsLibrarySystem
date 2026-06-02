using System.Collections.ObjectModel;
using System.ComponentModel;
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

        BackendSessionService.PropertyChanged += OnDependencyPropertyChanged;
        LibraryCatalogService.PropertyChanged += OnDependencyPropertyChanged;
    }

    [ObservableProperty]
    public partial string PromptDraft { get; set; }

    public string OperatorNotice => LibraryCatalogService.OperatorNotice;
    public string BackendStatusTitle => BackendSessionService.BackendStatusTitle;
    public string BackendStatusDetail => BackendSessionService.BackendStatusDetail;
    public string BackendEndpoint => BackendSessionService.BackendEndpoint;
    public ObservableCollection<AiCapabilityRecord> AiCapabilities => BackendSessionService.AiCapabilities;
    public ObservableCollection<string> ActivityFeed { get; }
    public IRelayCommand SubmitPromptCommand { get; }

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

    private void OnDependencyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
