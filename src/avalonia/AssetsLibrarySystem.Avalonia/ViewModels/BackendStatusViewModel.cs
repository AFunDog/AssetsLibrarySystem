using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

/// <summary>
/// 后端状态 ViewModel，持有后端连接状态的 UI 状态。
/// 代替原来 BackendSessionService 中的 ObservableProperty 职责。
/// </summary>
public sealed partial class BackendStatusViewModel : ObservableObject
{
    private IBackendSessionService BackendSession { get; }

    public BackendStatusViewModel(IBackendSessionService backendSession)
    {
        BackendSession = backendSession;
        AiCapabilities = [];

        BackendStatusTitle = "Python 引擎待初始化";
        BackendStatusStage = "等待初始化";
        BackendStatusDetail = "Python 引擎嵌入在桌面端进程中，无需独立 HTTP 服务。";
        BackendEndpoint = "in-process";
        SearchModelStatusTitle = "DashScope 云端模型";
        SearchModelStatusStage = "按请求调用";
        SearchModelStatusDetail = "向量化和重排序通过嵌入的 Python 引擎直接调用 DashScope API。";

        BackendSession.BackendStatusChanged += OnBackendStatusChanged;
    }

    // ===== 设计时构造函数 =====
    [Obsolete("仅供设计器使用")]
    public BackendStatusViewModel()
        : this(new NullBackendSessionService())
    {
    }

    public ObservableCollection<AiCapabilityRecord> AiCapabilities { get; }

    [ObservableProperty] public partial string BackendStatusTitle { get; set; }
    [ObservableProperty] public partial string BackendStatusStage { get; set; }
    [ObservableProperty] public partial string BackendStatusDetail { get; set; }
    [ObservableProperty] public partial string BackendEndpoint { get; set; }
    [ObservableProperty] public partial string SearchModelStatusTitle { get; set; }
    [ObservableProperty] public partial string SearchModelStatusStage { get; set; }
    [ObservableProperty] public partial string SearchModelStatusDetail { get; set; }

    public bool IsBackendReady => BackendSession.IsBackendReady;
    public string BaseUrl => BackendSession.BaseUrl;

    public async Task InitializeAsync()
    {
        BackendStatusTitle = "Python 引擎初始化中";
        BackendStatusStage = "正在初始化";
        OnPropertyChanged(nameof(IsBackendReady));
        try
        {
            await BackendSession.InitializeAsync();
            // 真实状态以 Session 为准（成功/失败/设计时模式）。
            SyncFromSession();
        }
        catch (Exception ex)
        {
            BackendStatusTitle = "Python 引擎初始化失败";
            BackendStatusStage = "启动失败";
            BackendStatusDetail = ex.Message;
            Log.Error(ex, "Python 引擎初始化失败。");
            OnPropertyChanged(nameof(IsBackendReady));
        }
    }

    private void OnBackendStatusChanged()
    {
        SyncFromSession();
    }

    private void SyncFromSession()
    {
        if (BackendSession is BackendSessionService session)
        {
            BackendStatusTitle = session.BackendStatusTitle;
            BackendStatusStage = session.BackendStatusStage;
            BackendStatusDetail = session.BackendStatusDetail;
            BackendEndpoint = session.BackendEndpoint;
            SearchModelStatusTitle = session.SearchModelStatusTitle;
            SearchModelStatusStage = session.SearchModelStatusStage;
            SearchModelStatusDetail = session.SearchModelStatusDetail;
        }

        OnPropertyChanged(nameof(IsBackendReady));
        OnPropertyChanged(nameof(BaseUrl));
    }
}

/// <summary>空实现，用于设计时模式</summary>
file sealed class NullBackendSessionService : IBackendSessionService
{
    public bool IsBackendReady => false;
    public string BaseUrl => "http://127.0.0.1:8000";
    public event Action? BackendStatusChanged;
    public Task InitializeAsync() => Task.CompletedTask;
}