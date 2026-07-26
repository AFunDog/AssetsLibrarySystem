using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Application.Services.BackendLauncher;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;
using AssetsLibrarySystem.Avalonia.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using Microsoft.Extensions.Configuration;

namespace AssetsLibrarySystem.Avalonia.Services.Backend;

public sealed partial class BackendSessionService : ObservableObject
{
    private IBackendLauncher? BackendLauncher { get; }
    private IBackgroundTaskService? BackgroundTaskService { get; }
    private ActivityFeedService ActivityFeedService { get; }

    public BackendSessionService()
        : this(null, null, new ActivityFeedService(), new UserSettingsService(), null)
    {
    }

    public BackendSessionService(
        IBackendLauncher? backendLauncher,
        IBackgroundTaskService? backgroundTaskService,
        ActivityFeedService activityFeedService,
        IUserSettingsService userSettingsService,
        IConfiguration? configuration)
    {
        BackendLauncher = backendLauncher;
        BackgroundTaskService = backgroundTaskService;
        ActivityFeedService = activityFeedService;
        AiCapabilities = [];

        BackendStatusTitle = "Python 模型服务待连接";
        BackendStatusStage = "等待启动";
        BackendStatusDetail = "桌面端承担素材目录、元数据和工作流编排；Python 只负责 HTTP 模型能力。";
        BackendEndpoint = backendLauncher?.BaseUrl ?? "http://127.0.0.1:8000";
        SearchModelStatusTitle = "DashScope 云端模型";
        SearchModelStatusStage = "按请求调用";
        SearchModelStatusDetail = "向量化和重排序统一使用 DashScope 云端 API。";

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

    public bool IsBackendReady => BackendLauncher?.IsRunning == true;

    public string BaseUrl => BackendLauncher?.BaseUrl ?? BackendEndpoint;

    public Task InitializeAsync()
    {
        if (BackendLauncher is null)
        {
            Log.Debug("BackendSessionService 处于设计时模式，跳过后端启动。");
            BackendStatusTitle = "设计时模式";
            BackendStatusStage = "本地预览";
            BackendStatusDetail = "当前界面使用桌面端本地逻辑，没有注入 Python 模型服务。";
            SearchModelStatusTitle = "设计时模式";
            SearchModelStatusStage = "本地预览";
            SearchModelStatusDetail = "当前界面不连接 Python 后端。";
            return Task.CompletedTask;
        }

        Log.Information("开始初始化后端会话服务，准备启动 Python 后端。");
        _ = InitializeBackendCoreAsync();
        return Task.CompletedTask;
    }

    public async Task EnsureRunningAsync()
    {
        if (BackendLauncher is null)
        {
            throw new InvalidOperationException("后端启动器未注册。");
        }

        Log.Information("用户触发后端确保运行。");
        if (!BackendLauncher.IsRunning)
        {
            Log.Information("后端当前未运行，开始启动。");
            await BackendLauncher.StartAsync();
            BackendEndpoint = BackendLauncher.BaseUrl;
            Log.Information("后端已启动，baseUrl={BaseUrl}", BackendEndpoint);
        }
        else
        {
            Log.Debug("后端已在运行，跳过重复启动，baseUrl={BaseUrl}", BackendLauncher.BaseUrl);
        }
    }

    private async Task InitializeBackendCoreAsync()
    {
        var taskId = BackgroundTaskService?.BeginTask("模型服务", "正在启动 Python 模型服务");
        BackendStatusTitle = "Python 模型服务启动中";
        BackendStatusStage = "启动服务";
        BackendStatusDetail = "正在等待 /health 返回，就绪后桌面端可将提示词任务转发给 HTTP 后端。";
        Log.Information("开始启动 Python 模型服务。");

        try
        {
            await BackendLauncher!.StartAsync();
            BackendEndpoint = BackendLauncher.BaseUrl;
            BackendStatusTitle = "Python 模型服务已连接";
            BackendStatusStage = "模型已就绪";
            BackendStatusDetail = "模型服务只负责大模型 HTTP 接口，不再承担素材库、文件扫描或目录管理。";
            Log.Information("Python 模型服务已连接，baseUrl={BaseUrl}", BackendEndpoint);
            ActivityFeedService.Add($"模型网关就绪：{BackendEndpoint}");

            BackendStatusStage = "模型已连接";
            BackendStatusDetail = "已连接到 Python 模型服务，检索使用 DashScope 云端 API。";
            SearchModelStatusTitle = "DashScope 云端模型";
            SearchModelStatusStage = "按请求调用";
            SearchModelStatusDetail = "向量化和重排序统一使用 DashScope 云端 API。";
            Log.Information("检索使用 DashScope 云端模型，跳过本地 warmup。");
            CompleteTask(taskId, "模型已连接", BackendStatusDetail);
        }
        catch (Exception ex)
        {
            BackendStatusTitle = "Python 模型服务未就绪";
            BackendStatusStage = "启动失败";
            BackendStatusDetail = ex.Message;
            SearchModelStatusTitle = "DashScope 云端模型";
            SearchModelStatusStage = "后端未就绪";
            SearchModelStatusDetail = ex.Message;
            Log.Error(ex, "Python 模型服务启动失败。");
            ActivityFeedService.Add($"模型网关启动失败：{ex.Message}");
            FailTask(taskId, "模型启动失败", ex.Message);
        }
    }

    private void SeedCapabilities()
    {
        AiCapabilities.Clear();
        AiCapabilities.Add(new AiCapabilityRecord("健康检查", "/health", "供桌面端确认 Python 模型服务是否可达。"));
        AiCapabilities.Add(new AiCapabilityRecord("能力清单", "/api/v1/model/capabilities", "返回当前模型网关的槽位、模式和占位能力。"));
        AiCapabilities.Add(new AiCapabilityRecord("文本生成", "/api/v1/model/generate", "只负责提示词转发与模型输出，不管理素材目录。"));
        AiCapabilities.Add(new AiCapabilityRecord("向量化", "/api/v1/search/index", "把输入文本转换成 embedding，供桌面端本地召回使用。"));
        AiCapabilities.Add(new AiCapabilityRecord("重排序", "/api/v1/search/query", "对桌面端传入的候选描述做 rerank，不直接读取数据库。"));
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