using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IBackendLauncher? _backendLauncher;
    private readonly List<ManagedAssetRecord> _allAssets = [];

    public MainWindowViewModel() : this(null)
    {
    }

    public MainWindowViewModel(IBackendLauncher? backendLauncher)
    {
        _backendLauncher = backendLauncher;

        Metrics = new ObservableCollection<DashboardMetric>();
        Libraries = new ObservableCollection<LibraryWorkspace>();
        VisibleAssets = new ObservableCollection<ManagedAssetRecord>();
        AiCapabilities = new ObservableCollection<AiCapabilityRecord>();
        SelectedAssetTags = new ObservableCollection<string>();
        ActivityFeed = new ObservableCollection<string>();

        SeedWorkspace();
    }

    public ObservableCollection<DashboardMetric> Metrics { get; }
    public ObservableCollection<LibraryWorkspace> Libraries { get; }
    public ObservableCollection<ManagedAssetRecord> VisibleAssets { get; }
    public ObservableCollection<AiCapabilityRecord> AiCapabilities { get; }
    public ObservableCollection<string> SelectedAssetTags { get; }
    public ObservableCollection<string> ActivityFeed { get; }

    [ObservableProperty]
    private LibraryWorkspace? selectedLibrary;

    [ObservableProperty]
    private ManagedAssetRecord? selectedAsset;

    [ObservableProperty]
    private string backendStatusTitle = "Python 模型服务待连接";

    [ObservableProperty]
    private string backendStatusDetail = "桌面端承担素材目录、元数据和工作流编排；Python 只负责 HTTP 模型能力。";

    [ObservableProperty]
    private string backendEndpoint = "http://127.0.0.1:8000";

    [ObservableProperty]
    private string workspaceTitle = "本地素材工作台";

    [ObservableProperty]
    private string workspaceSummary = "以 Avalonia/.NET 为主入口，围绕素材库登记、状态编排和模型任务队列组织桌面体验。";

    [ObservableProperty]
    private string assetSummary = "当前展示的是桌面端素材目录样例数据，后续可在此挂接真实扫描与本地仓储。";

    [ObservableProperty]
    private string operatorNotice = "先完成边界收敛：素材管理留在 .NET，本地模型调用走 Python HTTP 服务。";

    [ObservableProperty]
    private string promptDraft = "请基于当前素材生成一段适合检索与人工校对的中文描述。";

    [ObservableProperty]
    private string selectedAssetName = "尚未选择素材";

    [ObservableProperty]
    private string selectedAssetLibrary = "请从中间列表选择一个条目";

    [ObservableProperty]
    private string selectedAssetPath = "当前未加载本地文件路径";

    [ObservableProperty]
    private string selectedAssetType = "未选择";

    [ObservableProperty]
    private string selectedAssetStage = "待选择";

    [ObservableProperty]
    private string selectedAssetAiState = "未排队";

    [ObservableProperty]
    private string selectedAssetDetail = "右侧详情区域当前只展示桌面端维护的元数据和任务状态。";

    public async Task InitializeAsync()
    {
        if (_backendLauncher is null)
        {
            BackendStatusTitle = "设计时模式";
            BackendStatusDetail = "当前界面使用样例数据渲染，没有注入 Python 模型服务。";
            return;
        }

        BackendStatusTitle = "Python 模型服务启动中";
        BackendStatusDetail = "正在等待 /health 返回，就绪后桌面端可将提示词任务转发给 HTTP 后端。";

        try
        {
            await _backendLauncher.StartAsync();
            BackendEndpoint = _backendLauncher.BaseUrl;
            BackendStatusTitle = "Python 模型服务已连接";
            BackendStatusDetail = "模型服务只负责大模型 HTTP 接口，不再承担素材库、文件扫描或目录管理。";
            ActivityFeed.Insert(0, $"模型网关就绪：{BackendEndpoint}");
        }
        catch (Exception ex)
        {
            BackendStatusTitle = "Python 模型服务未就绪";
            BackendStatusDetail = ex.Message;
            OperatorNotice = "后端启动失败，当前仍可继续设计桌面端素材流程。";
            ActivityFeed.Insert(0, $"模型网关启动失败：{ex.Message}");
        }
    }

    partial void OnSelectedLibraryChanged(LibraryWorkspace? value)
    {
        RebuildVisibleAssets(value);
    }

    partial void OnSelectedAssetChanged(ManagedAssetRecord? value)
    {
        SelectedAssetTags.Clear();

        if (value is null)
        {
            SelectedAssetName = "尚未选择素材";
            SelectedAssetLibrary = "请从中间列表选择一个条目";
            SelectedAssetPath = "当前未加载本地文件路径";
            SelectedAssetType = "未选择";
            SelectedAssetStage = "待选择";
            SelectedAssetAiState = "未排队";
            SelectedAssetDetail = "右侧详情区域当前只展示桌面端维护的元数据和任务状态。";
            return;
        }

        SelectedAssetName = value.Name;
        SelectedAssetLibrary = value.LibraryName;
        SelectedAssetPath = value.LocalPath;
        SelectedAssetType = value.AssetType;
        SelectedAssetStage = value.Stage;
        SelectedAssetAiState = value.AiState;
        SelectedAssetDetail = value.Summary;
        foreach (var tag in value.Tags)
        {
            SelectedAssetTags.Add(tag);
        }

        PromptDraft = $"请围绕素材“{value.Name}”输出更适合检索的中文描述和标签建议。";
    }

    [RelayCommand]
    private void RefreshWorkspace()
    {
        OperatorNotice = "当前刷新的是桌面端素材视图；真实目录扫描和本地持久化后续接入 .NET 服务层。";
        ActivityFeed.Insert(0, "已刷新桌面端工作台视图。");
    }

    [RelayCommand]
    private void QueueDescription()
    {
        if (SelectedAsset is null)
        {
            OperatorNotice = "请先选择一个素材，再把描述生成任务送入 Python 模型服务。";
            return;
        }

        SelectedAsset.Stage = "待模型描述";
        SelectedAsset.AiState = "待发送到 HTTP 服务";
        SyncSelectedAssetFields();
        OperatorNotice = $"已为 {SelectedAsset.Name} 排入描述任务，后续由 Python HTTP 服务处理提示词。";
        ActivityFeed.Insert(0, $"描述任务排队：{SelectedAsset.Name}");
    }

    [RelayCommand]
    private void QueueEmbedding()
    {
        if (SelectedAsset is null)
        {
            OperatorNotice = "请先选择一个素材，再安排索引预处理任务。";
            return;
        }

        SelectedAsset.Stage = "待索引";
        SelectedAsset.AiState = "等待桌面端索引流水线";
        SyncSelectedAssetFields();
        OperatorNotice = $"已把 {SelectedAsset.Name} 标记为待索引，后续可沿着召回 + 精排链路扩展。";
        ActivityFeed.Insert(0, $"索引任务排队：{SelectedAsset.Name}");
    }

    [RelayCommand]
    private void MarkManaged()
    {
        if (SelectedAsset is null)
        {
            OperatorNotice = "请先选择一个素材，再调整其桌面端管理状态。";
            return;
        }

        SelectedAsset.Stage = "桌面端已接管";
        SelectedAsset.AiState = "无需后端素材处理";
        SyncSelectedAssetFields();
        OperatorNotice = $"{SelectedAsset.Name} 已切换到 .NET 素材管理视图。";
        ActivityFeed.Insert(0, $"状态更新：{SelectedAsset.Name} -> 桌面端已接管");
    }

    [RelayCommand]
    private void SubmitPrompt()
    {
        if (string.IsNullOrWhiteSpace(PromptDraft))
        {
            OperatorNotice = "请输入要发送给 Python 模型服务的提示词。";
            return;
        }

        var target = SelectedAsset?.Name ?? "当前会话";
        OperatorNotice = _backendLauncher?.IsRunning == true
            ? $"提示词已准备发送到 {BackendEndpoint}，当前先保留为界面占位联动。"
            : "Python 模型服务尚未就绪，当前只演示桌面端任务编排。";
        ActivityFeed.Insert(0, $"提示词草稿已更新：{target}");
    }

    private void SeedWorkspace()
    {
        Metrics.Clear();
        Metrics.Add(new DashboardMetric("本地素材库", "03", "Avalonia 侧维护目录与元数据"));
        Metrics.Add(new DashboardMetric("待模型处理", "07", "仅把提示词和推理请求交给 Python"));
        Metrics.Add(new DashboardMetric("检索候选位", "12", "为后续召回 + 精排链路预留"));
        Metrics.Add(new DashboardMetric("后端职责", ".PY", "HTTP 模型网关"));

        Libraries.Clear();
        Libraries.Add(new LibraryWorkspace(
            "角色立绘总库",
            @"D:\Assets\Characters",
            "面向图片与表情差分，优先沉淀本地目录和人工校对状态。",
            "桌面端本地目录",
            18));
        Libraries.Add(new LibraryWorkspace(
            "演出视频素材",
            @"D:\Assets\StageClips",
            "面向镜头段落、节奏片段和后续摘要切片的管理。",
            "桌面端扫描任务",
            9));
        Libraries.Add(new LibraryWorkspace(
            "音乐与环境声",
            @"D:\Assets\Music",
            "面向音乐、氛围声和后续标签索引预处理。",
            "桌面端人工整理",
            24));

        _allAssets.Clear();
        _allAssets.AddRange([
            new ManagedAssetRecord(
                "asset-001",
                "mahiro_smile_pose.png",
                "角色立绘总库",
                "图片",
                @"poses\mahiro_smile_pose.png",
                @"D:\Assets\Characters\poses\mahiro_smile_pose.png",
                "角色正面半身像，表情偏平稳，适合用作日常对话立绘基准帧。",
                "桌面端已登记",
                "未提交模型",
                ["立绘", "正面", "日常"]),
            new ManagedAssetRecord(
                "asset-002",
                "concert_cut_07.mp4",
                "演出视频素材",
                "视频",
                @"cuts\concert_cut_07.mp4",
                @"D:\Assets\StageClips\cuts\concert_cut_07.mp4",
                "高能段落切片，后续可补节奏标签、镜头说明和摘要文本。",
                "待片段标注",
                "待摘要生成",
                ["舞台", "节奏", "高潮"]),
            new ManagedAssetRecord(
                "asset-003",
                "night_train_loop.flac",
                "音乐与环境声",
                "音乐",
                @"ambient\night_train_loop.flac",
                @"D:\Assets\Music\ambient\night_train_loop.flac",
                "低速循环环境声，适合作为场景氛围素材，后续可补情绪与场景标签。",
                "待索引",
                "待向量化",
                ["环境声", "夜晚", "列车"]),
            new ManagedAssetRecord(
                "asset-004",
                "scene_notes_intro.txt",
                "角色立绘总库",
                "文本",
                @"notes\scene_notes_intro.txt",
                @"D:\Assets\Characters\notes\scene_notes_intro.txt",
                "文本素材用于描述镜头场景和角色关系，可直接进入后续检索上下文。",
                "桌面端已登记",
                "待扩写摘要",
                ["文本", "场景说明", "关系"]),
        ]);

        AiCapabilities.Clear();
        AiCapabilities.Add(new AiCapabilityRecord("健康检查", "/health", "供桌面端确认 Python 模型服务是否可达。"));
        AiCapabilities.Add(new AiCapabilityRecord("能力清单", "/api/v1/model/capabilities", "返回当前模型网关的槽位、模式和占位能力。"));
        AiCapabilities.Add(new AiCapabilityRecord("文本生成", "/api/v1/model/generate", "只负责提示词转发与模型输出，不管理素材目录。"));

        ActivityFeed.Clear();
        ActivityFeed.Add("桌面端作为素材管理主入口，先固定本地工作流边界。");
        ActivityFeed.Add("Python 进程仅暴露 HTTP 模型能力，避免再次把素材管理逻辑塞回后端。");
        ActivityFeed.Add("后续检索链路仍参考 RenderTest/test2.py 的召回 + 精排 + 索引持久化思路。");

        SelectedLibrary = Libraries.FirstOrDefault();
    }

    private void RebuildVisibleAssets(LibraryWorkspace? library)
    {
        VisibleAssets.Clear();

        IEnumerable<ManagedAssetRecord> items = _allAssets;
        if (library is not null)
        {
            items = items.Where(asset => asset.LibraryName == library.Name);
            WorkspaceTitle = library.Name;
            WorkspaceSummary = $"{library.SyncMode} · {library.RootPath}";
            AssetSummary = library.Summary;
        }
        else
        {
            WorkspaceTitle = "本地素材工作台";
            WorkspaceSummary = "以 Avalonia/.NET 为主入口，围绕素材库登记、状态编排和模型任务队列组织桌面体验。";
            AssetSummary = "当前展示的是桌面端素材目录样例数据，后续可在此挂接真实扫描与本地仓储。";
        }

        foreach (var asset in items)
        {
            VisibleAssets.Add(asset);
        }

        SelectedAsset = VisibleAssets.FirstOrDefault();
    }

    private void SyncSelectedAssetFields()
    {
        if (SelectedAsset is null)
        {
            return;
        }

        SelectedAssetStage = SelectedAsset.Stage;
        SelectedAssetAiState = SelectedAsset.AiState;
    }
}
