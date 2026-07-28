using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;
using AssetsLibrarySystem.Application.Services.Python;
using AssetsLibrarySystem.Avalonia.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using Microsoft.Extensions.Configuration;

namespace AssetsLibrarySystem.Avalonia.Services.Backend;

public sealed partial class BackendSessionService : ObservableObject, IBackendSessionService
{
    private PythonEngineService? PythonEngine { get; }
    private IBackgroundTaskService? BackgroundTaskService { get; }
    private ActivityFeedService ActivityFeedService { get; }

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

    public bool IsBackendReady => PythonEngine is not null;

    public string BaseUrl => BackendEndpoint;

    public Task InitializeAsync()
    {
        if (PythonEngine is null)
        {
            Log.Debug("BackendSessionService 处于设计时模式，跳过 Python 引擎初始化。");
            BackendStatusTitle = "设计时模式";
            BackendStatusStage = "本地预览";
            BackendStatusDetail = "Python 引擎未注入，仅使用桌面端本地逻辑。";
            SearchModelStatusTitle = "设计时模式";
            SearchModelStatusStage = "本地预览";
            SearchModelStatusDetail = "Python 引擎未连接。";
            BackendStatusChanged?.Invoke();
            return Task.CompletedTask;
        }

        Log.Information("开始初始化嵌入的 Python 引擎。");
        _ = InitializePythonEngineAsync();
        return Task.CompletedTask;
    }

    private async Task InitializePythonEngineAsync()
    {
        var taskId = BackgroundTaskService?.BeginTask("Python 引擎", "正在初始化嵌入的 Python 运行时");
        BackendStatusTitle = "Python 引擎初始化中";
        BackendStatusStage = "正在初始化";
        BackendStatusDetail = "Python 引擎嵌入在桌面端进程中，无需独立 HTTP 服务。";
        Log.Information("开始初始化 Python 引擎。");

        try
        {
            await Task.Run(() => PythonEngine!.Initialize());
            BackendStatusTitle = "Python 引擎已就绪";
            BackendStatusStage = "就绪";
            BackendStatusDetail = "Python 引擎嵌入在桌面端进程中，直接调用 DashScope API。";
            Log.Information("Python 引擎初始化完成");
            ActivityFeedService.Add("Python 引擎就绪（嵌入模式）");
            CompleteTask(taskId, "Python 引擎就绪", BackendStatusDetail);
            BackendStatusChanged?.Invoke();
        }
        catch (Exception ex)
        {
            BackendStatusTitle = "Python 引擎初始化失败";
            BackendStatusStage = "启动失败";
            BackendStatusDetail = ex.Message;
            Log.Error(ex, "Python 引擎初始化失败。");
            ActivityFeedService.Add($"Python 引擎初始化失败：{ex.Message}");
            FailTask(taskId, "引擎初始化失败", ex.Message);
            BackendStatusChanged?.Invoke();
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