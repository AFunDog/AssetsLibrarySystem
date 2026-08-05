using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;
using AssetsLibrarySystem.Application.Services.Python;
using AssetsLibrarySystem.Avalonia.Services.Settings;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using Microsoft.Extensions.Configuration;

namespace AssetsLibrarySystem.Avalonia.Services.Backend;

public sealed partial class BackendSessionService : ObservableObject, IBackendSessionService
{
    private PythonEngineService? PythonEngine { get; }
    private IBackgroundTaskService? BackgroundTaskService { get; }
    private ActivityFeedService ActivityFeedService { get; }
    private CancellationTokenSource? _initCts;
    private Task? _initTask;
    private bool _isBackendReady;

    public BackendSessionService()
        : this(null, null, new ActivityFeedService(), new UserSettingsService(), null)
    {
    }

    public event Action? BackendStatusChanged;

    public BackendSessionService(
        PythonEngineService? pythonEngine,
        IBackgroundTaskService? backgroundTaskService,
        ActivityFeedService activityFeedService,
        IUserSettingsService userSettingsService,
        IConfiguration? configuration)
    {
        PythonEngine = pythonEngine;
        BackgroundTaskService = backgroundTaskService;
        ActivityFeedService = activityFeedService;
        AiCapabilities = [];

        BackendStatusTitle = "Python 引擎待初始化";
        BackendStatusStage = "等待初始化";
        BackendStatusDetail = "Python 引擎嵌入在桌面端进程中，无需独立 HTTP 服务。";
        BackendEndpoint = "in-process";
        SearchModelStatusTitle = "DashScope 云端模型";
        SearchModelStatusStage = "按请求调用";
        SearchModelStatusDetail = "向量化和重排序通过嵌入的 Python 引擎直接调用 DashScope API。";

        SeedCapabilities();
    }

    public ObservableCollection<AiCapabilityRecord> AiCapabilities { get; }

    [ObservableProperty]
    public partial string BackendStatusTitle { get; set; }

    [ObservableProperty]
    public partial string BackendStatusStage { get; set; }

    [ObservableProperty]
    public partial string BackendStatusDetail { get; set; }

    [ObservableProperty]
    public partial string BackendEndpoint { get; set; }

    [ObservableProperty]
    public partial string SearchModelStatusTitle { get; set; }

    [ObservableProperty]
    public partial string SearchModelStatusStage { get; set; }

    [ObservableProperty]
    public partial string SearchModelStatusDetail { get; set; }

    public bool IsBackendReady => _isBackendReady;

    /// <summary>更新后端状态（属性 + 事件），确保在 UI 线程执行。</summary>
    private void SetBackendStatus(string title, string stage, string detail, bool isReady)
    {
        _isBackendReady = isReady;
        var update = () =>
        {
            BackendStatusTitle = title;
            BackendStatusStage = stage;
            BackendStatusDetail = detail;
            BackendStatusChanged?.Invoke();
        };
        if (Dispatcher.UIThread.CheckAccess())
        {
            update();
        }
        else
        {
            // 后台线程更新 UI 属性/事件必须派发到 UI 线程；
            // 同步等待保证调用方（await InitializeAsync）返回时状态已就绪
            Dispatcher.UIThread.InvokeAsync(update).Wait();
        }
    }

    public string BaseUrl => BackendEndpoint;

    public Task InitializeAsync()
    {
        if (PythonEngine is null)
        {
            Log.Debug("BackendSessionService 处于设计时模式，跳过 Python 引擎初始化。");
            _isBackendReady = false;
            BackendStatusTitle = "设计时模式";
            BackendStatusStage = "本地预览";
            BackendStatusDetail = "Python 引擎未注入，仅使用桌面端本地逻辑。";
            SearchModelStatusTitle = "设计时模式";
            SearchModelStatusStage = "本地预览";
            SearchModelStatusDetail = "Python 引擎未连接。";
            BackendStatusChanged?.Invoke();
            return Task.CompletedTask;
        }

        if (_initTask is not null)
        {
            if (_initTask.IsCompleted)
            {
                // 初始化失败（引擎未就绪）后允许重新初始化；
                // 成功则直接复用已完成的 task
                if (!_isBackendReady)
                {
                    _initTask = null;
                }
                else
                {
                    return _initTask;
                }
            }
            else
            {
                return _initTask;
            }
        }

        Log.Information("开始初始化嵌入的 Python 引擎。");
        _initCts = new CancellationTokenSource();
        _initTask = InitializePythonEngineAsync(_initCts.Token);
        // 返回初始化任务本身，调用方 await 后才能依赖 IsBackendReady。
        return _initTask;
    }

    private async Task InitializePythonEngineAsync(CancellationToken ct)
    {
        var taskId = BackgroundTaskService?.BeginTask("Python 引擎", "正在初始化嵌入的 Python 运行时");
        _isBackendReady = false;
        BackendStatusTitle = "Python 引擎初始化中";
        BackendStatusStage = "正在初始化";
        BackendStatusDetail = "Python 引擎嵌入在桌面端进程中，无需独立 HTTP 服务。";
        Log.Information("开始初始化 Python 引擎。");
        BackendStatusChanged?.Invoke();

        try
        {
            await Task.Run(() => PythonEngine!.Initialize(), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            SetBackendStatus("Python 引擎已就绪", "就绪", "Python 引擎嵌入在桌面端进程中，直接调用模型 API。", true);
            Log.Information("Python 引擎初始化完成");
            ActivityFeedService.Add("Python 引擎就绪（嵌入模式）");
            CompleteTask(taskId, "Python 引擎就绪", BackendStatusDetail);
        }
        catch (OperationCanceledException)
        {
            SetBackendStatus("Python 引擎初始化已取消", "已取消", "应用已退出", false);
            Log.Information("Python 引擎初始化已取消（应用退出）。");
            CompleteTask(taskId, "引擎初始化已取消", "应用已退出");
        }
        catch (Exception ex)
        {
            SetBackendStatus("Python 引擎初始化失败", "启动失败", ex.Message, false);
            Log.Error(ex, "Python 引擎初始化失败。");
            ActivityFeedService.Add($"Python 引擎初始化失败：{ex.Message}");
            FailTask(taskId, "引擎初始化失败", ex.Message);
        }
    }

    private void SeedCapabilities()
    {
        AiCapabilities.Clear();
        AiCapabilities.Add(new AiCapabilityRecord("文本生成", "嵌入调用", "通过嵌入的 Python 引擎直接调用 DashScope 模型。"));
        AiCapabilities.Add(new AiCapabilityRecord("向量化", "嵌入调用", "通过嵌入的 Python 引擎直接调用 DashScope 向量化 API。"));
        AiCapabilities.Add(new AiCapabilityRecord("重排序", "嵌入调用", "通过嵌入的 Python 引擎直接调用 DashScope 重排序 API。"));
    }

    private void UpdateTask(string? taskId, string stageText, string? detailText = null)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        BackgroundTaskService?.UpdateTask(taskId, stageText, detailText);
    }

    private void CompleteTask(string? taskId, string? stageText = null, string? detailText = null)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        BackgroundTaskService?.CompleteTask(taskId, stageText, detailText);
    }

    private void FailTask(string? taskId, string stageText, string detailText)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        BackgroundTaskService?.FailTask(taskId, detailText, stageText);
    }
}