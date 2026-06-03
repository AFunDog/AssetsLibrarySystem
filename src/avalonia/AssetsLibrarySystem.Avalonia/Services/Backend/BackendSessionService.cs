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

namespace AssetsLibrarySystem.Avalonia.Services.Backend;

public sealed partial class BackendSessionService : ObservableObject
{
    private IBackendLauncher? BackendLauncher { get; }
    private IAssetSearchService? AssetSearchService { get; }
    private IBackgroundTaskService? BackgroundTaskService { get; }
    private IUserSettingsService UserSettings { get; }
    private ActivityFeedService ActivityFeedService { get; }

    public BackendSessionService()
        : this(null, null, null, new ActivityFeedService(), new UserSettingsService())
    {
    }

    public BackendSessionService(
        IBackendLauncher? backendLauncher,
        IAssetSearchService? assetSearchService,
        IBackgroundTaskService? backgroundTaskService,
        ActivityFeedService activityFeedService,
        IUserSettingsService userSettingsService)
    {
        BackendLauncher = backendLauncher;
        AssetSearchService = assetSearchService;
        BackgroundTaskService = backgroundTaskService;
        ActivityFeedService = activityFeedService;
        UserSettings = userSettingsService;
        AiCapabilities = [];

        BackendStatusTitle = "Python 模型服务待连接";
        BackendStatusStage = "等待启动 [0/2]";
        BackendStatusDetail = "桌面端承担素材目录、元数据和工作流编排；Python 只负责 HTTP 模型能力。";
        BackendEndpoint = backendLauncher?.BaseUrl ?? "http://127.0.0.1:8000";
        SearchModelStatusTitle = "本地搜索模型待查询";
        SearchModelStatusStage = "等待后端启动 [0/2]";
        SearchModelStatusDetail = "尚未获取 embedding / rerank 模型的驻留状态。";

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
            BackendStatusStage = "本地预览 [0/0]";
            BackendStatusDetail = "当前界面使用桌面端本地逻辑，没有注入 Python 模型服务。";
            SearchModelStatusTitle = "设计时模式";
            SearchModelStatusStage = "本地预览 [0/0]";
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
        BackendStatusStage = "启动服务 [0/2]";
        BackendStatusDetail = "正在等待 /health 返回，就绪后桌面端可将提示词任务转发给 HTTP 后端。";
        Log.Information("开始启动 Python 模型服务。");

        try
        {
            await BackendLauncher!.StartAsync();
            BackendEndpoint = BackendLauncher.BaseUrl;
            BackendStatusTitle = "Python 模型服务已连接";
            BackendStatusStage = "模型加载完毕 [2/2]";
            BackendStatusDetail = "模型服务只负责大模型 HTTP 接口，不再承担素材库、文件扫描或目录管理。";
            Log.Information("Python 模型服务已连接，baseUrl={BaseUrl}", BackendEndpoint);
            ActivityFeedService.Add($"模型网关就绪：{BackendEndpoint}");

            if (AssetSearchService is not null)
            {
                var shouldWarmEmbedding = UserSettings.AutoWarmupEmbeddingModel;
                var shouldWarmRerank = UserSettings.AutoWarmupRerankModel;

                if (shouldWarmEmbedding || shouldWarmRerank)
                {
                    UpdateTask(taskId, "正在预热模型", "按设置预热选定的本地搜索模型");
                    BackendStatusStage = "正在预热模型 [1/2]";
                    BackendStatusDetail = "正在按设置预热本地搜索模型，减少第一次检索等待。";

                    try
                    {
                        Log.Information(
                            "开始按设置预热本地搜索模型，autoWarmupEmbeddingModel={AutoWarmupEmbeddingModel}, autoWarmupRerankModel={AutoWarmupRerankModel}",
                            shouldWarmEmbedding,
                            shouldWarmRerank);

                        AssetSearchWarmupDocument? embeddingWarmup = null;
                        AssetSearchWarmupDocument? rerankWarmup = null;

                        var embeddingWarmupTask = shouldWarmEmbedding
                            ? AssetSearchService.WarmupEmbeddingAsync(BackendEndpoint)
                            : null;
                        var rerankWarmupTask = shouldWarmRerank
                            ? AssetSearchService.WarmupRerankAsync(BackendEndpoint)
                            : null;

                        if (embeddingWarmupTask is not null && rerankWarmupTask is not null)
                        {
                            Log.Information("并行预热 embedding 与 rerank 本地搜索模型。");
                            await Task.WhenAll(embeddingWarmupTask, rerankWarmupTask);
                            embeddingWarmup = await embeddingWarmupTask;
                            rerankWarmup = await rerankWarmupTask;
                        }
                        else if (embeddingWarmupTask is not null)
                        {
                            embeddingWarmup = await embeddingWarmupTask;
                        }
                        else if (rerankWarmupTask is not null)
                        {
                            rerankWarmup = await rerankWarmupTask;
                        }

                        BackendStatusStage = shouldWarmEmbedding && shouldWarmRerank
                            ? "模型加载完毕 [2/2]"
                            : "模型部分加载 [1/2]";
                        BackendStatusDetail = shouldWarmEmbedding && shouldWarmRerank
                            ? $"检索模型已预热：{embeddingWarmup?.ModelName} / {rerankWarmup?.ModelName}"
                            : $"已按设置预热：{embeddingWarmup?.ModelName ?? rerankWarmup?.ModelName}";

                        Log.Information(
                            "检索模型按设置预热完成，embeddingEnabled={EmbeddingEnabled}, rerankEnabled={RerankEnabled}, embeddingModel={EmbeddingModel}, rerankModel={RerankModel}",
                            shouldWarmEmbedding,
                            shouldWarmRerank,
                            embeddingWarmup?.ModelName ?? string.Empty,
                            rerankWarmup?.ModelName ?? string.Empty);

                        ActivityFeedService.Add(
                            shouldWarmEmbedding && shouldWarmRerank
                                ? $"检索模型预热完成：{embeddingWarmup?.ModelName} / {rerankWarmup?.ModelName}"
                                : $"检索模型按设置预热：{embeddingWarmup?.ModelName ?? rerankWarmup?.ModelName}");
                        CompleteTask(taskId, "模型加载完毕", BackendStatusDetail);
                    }
                    catch (Exception ex)
                    {
                        BackendStatusStage = "模型预热失败 [2/2]";
                        BackendStatusDetail = $"模型预热失败：{ex.Message}";
                        Log.Warning(ex, "检索模型预热失败。");
                        ActivityFeedService.Add($"检索模型预热失败：{ex.Message}");
                        FailTask(taskId, "模型预热失败", ex.Message);
                    }

                    await RefreshSearchModelStatusAsync(suppressErrors: true);
                }
                else
                {
                    BackendStatusStage = "模型已连接 [1/2]";
                    BackendStatusDetail = "已连接到 Python 模型服务，未启用本地搜索模型自动预热。";
                    SearchModelStatusTitle = "本地搜索模型自动预热已关闭";
                    SearchModelStatusStage = "等待手动触发 [0/2]";
                    SearchModelStatusDetail = "可在设置页勾选 embedding / rerank 自动预热，下次启动生效。";
                    Log.Information("本地搜索模型自动预热已关闭，跳过 warmup。");
                    CompleteTask(taskId, "模型已连接", BackendStatusDetail);
                }
            }
            else
            {
                BackendStatusStage = "模型已连接 [1/2]";
                SearchModelStatusTitle = "本地搜索模型未注入";
                SearchModelStatusStage = "桌面端未注册检索服务 [0/2]";
                SearchModelStatusDetail = "当前只有后端连接状态，没有本地搜索模型控制能力。";
                Log.Debug("本地搜索服务未注册，仅更新后端连接状态。");
                CompleteTask(taskId, "模型已连接", BackendStatusDetail);
            }
        }
        catch (Exception ex)
        {
            BackendStatusTitle = "Python 模型服务未就绪";
            BackendStatusStage = "启动失败 [0/2]";
            BackendStatusDetail = ex.Message;
            SearchModelStatusTitle = "本地搜索模型未就绪";
            SearchModelStatusStage = "后端启动失败 [0/2]";
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
        AiCapabilities.Add(new AiCapabilityRecord("向量检索", "/api/v1/search/explore", "输入自然语言后，先召回再重排返回最符合的素材。"));
        AiCapabilities.Add(new AiCapabilityRecord("索引重建", "/api/v1/search/reindex", "从 asset_descriptions.db 重新构建本地 HNSW 索引。"));
        AiCapabilities.Add(new AiCapabilityRecord("模型状态", "/api/v1/search/models/status", "查看 embedding 与 rerank 模型是否已驻留在后端进程中。"));
        AiCapabilities.Add(new AiCapabilityRecord("关闭模型", "/api/v1/search/models/close", "主动释放指定本地搜索模型，便于在空闲时腾出显存。"));
    }

    public async Task<AssetSearchModelStatusDocument?> RefreshSearchModelStatusAsync(bool suppressErrors = false)
    {
        if (AssetSearchService is null)
        {
            SearchModelStatusTitle = "本地搜索模型未注入";
            SearchModelStatusStage = "桌面端未注册检索服务 [0/2]";
            SearchModelStatusDetail = "当前只有后端连接状态，没有本地搜索模型控制能力。";
            return null;
        }

        try
        {
            Log.Debug("查询本地搜索模型状态，baseUrl={BaseUrl}", BaseUrl);
            var status = await AssetSearchService.GetModelStatusAsync(BaseUrl);
            SearchModelStatusTitle = status.LoadedCount > 0
                ? "本地搜索模型已驻留"
                : "本地搜索模型未驻留";
            SearchModelStatusStage = $"已驻留 {status.LoadedCount}/2";
            SearchModelStatusDetail = $"embedding: {(status.EmbeddingLoaded ? "已驻留" : "未驻留")} ({status.EmbeddingModelName}) · rerank: {(status.RerankLoaded ? "已驻留" : "未驻留")} ({status.RerankModelName})";
            Log.Debug(
                "本地搜索模型状态已刷新: loadedCount={LoadedCount}, embeddingLoaded={EmbeddingLoaded}, rerankLoaded={RerankLoaded}, device={Device}",
                status.LoadedCount,
                status.EmbeddingLoaded,
                status.RerankLoaded,
                status.Device);
            return status;
        }
        catch (Exception ex)
        {
            SearchModelStatusTitle = "本地搜索模型状态获取失败";
            SearchModelStatusStage = "状态刷新失败";
            SearchModelStatusDetail = ex.Message;

            if (suppressErrors)
            {
                Log.Debug(ex, "本地搜索模型状态刷新失败（已抑制错误）。");
                return null;
            }

            Log.Warning(ex, "本地搜索模型状态刷新失败。");
            throw;
        }
    }

    public async Task<AssetSearchModelCloseDocument> CloseSearchModelAsync(string modelKind)
    {
        if (AssetSearchService is null)
        {
            throw new InvalidOperationException("检索服务未注册，无法关闭本地搜索模型。");
        }

        Log.Information("用户请求关闭本地搜索模型，modelKind={ModelKind}", modelKind);
        var result = await AssetSearchService.CloseModelAsync(BaseUrl, modelKind);
        ActivityFeedService.Add($"关闭本地搜索模型：{result.ModelKind} -> {(result.Closed ? "已释放" : "未驻留")}");
        Log.Information(
            "本地搜索模型关闭结果: modelKind={ModelKind}, closed={Closed}, remainingLoadedModels={RemainingLoadedModels}",
            result.ModelKind,
            result.Closed,
            string.Join(", ", result.RemainingLoadedModels));
        await RefreshSearchModelStatusAsync(suppressErrors: true);
        return result;
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
