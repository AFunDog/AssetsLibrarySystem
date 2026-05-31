using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.AssetLibrary;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IAssetLibraryService? _assetLibraryService;
    private readonly IBackendLauncher? _backendLauncher;
    private readonly List<ManagedAssetRecord> _allAssets = [];
    private bool _isLibraryScanRunning;

    public MainWindowViewModel() : this(null, null)
    {
    }

    public MainWindowViewModel(IBackendLauncher? backendLauncher, IAssetLibraryService? assetLibraryService)
    {
        _backendLauncher = backendLauncher;
        _assetLibraryService = assetLibraryService;

        Metrics = new ObservableCollection<DashboardMetric>();
        Libraries = new ObservableCollection<LibraryWorkspace>();
        VisibleAssets = new ObservableCollection<ManagedAssetRecord>();
        AiCapabilities = new ObservableCollection<AiCapabilityRecord>();
        SelectedAssetTags = new ObservableCollection<string>();
        ActivityFeed = new ObservableCollection<string>();

        SeedStaticData();
        RebuildMetrics();
        SetEmptyWorkspaceState();
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
    private string workspaceSummary = "先登记素材库目录，再扫描本地文件，桌面端负责目录和元数据展示。";

    [ObservableProperty]
    private string assetSummary = "当前还没有扫描结果。选择一个素材库后，点击“扫描当前素材库”加载文件。";

    [ObservableProperty]
    private string operatorNotice = "先在桌面端选择一个文件夹并登记为素材库目录，再触发扫描。";

    [ObservableProperty]
    private string promptDraft = "请基于当前素材生成一段适合检索与人工校对的中文描述。";

    [ObservableProperty]
    private string selectedAssetName = "尚未选择素材";

    [ObservableProperty]
    private string selectedAssetLibrary = "请先添加并扫描一个素材库";

    [ObservableProperty]
    private string selectedAssetPath = "当前未加载本地文件路径";

    [ObservableProperty]
    private string selectedAssetType = "未选择";

    [ObservableProperty]
    private string selectedAssetStage = "待选择";

    [ObservableProperty]
    private string selectedAssetAiState = "未排队";

    [ObservableProperty]
    private string selectedAssetDetail = "右侧详情区域会展示当前素材的路径、类型和扫描结果。";

    public async Task InitializeAsync()
    {
        if (_backendLauncher is null)
        {
            BackendStatusTitle = "设计时模式";
            BackendStatusDetail = "当前界面使用桌面端本地逻辑，没有注入 Python 模型服务。";
        }
        else
        {
            await InitializeBackendAsync();
        }

        await LoadLibrariesAsync();
    }

    public async Task AddLibraryDirectoryAsync(string folderPath)
    {
        if (_assetLibraryService is null)
        {
            OperatorNotice = "素材库服务尚未注册，当前无法保存目录。";
            return;
        }

        var library = await _assetLibraryService.AddLibraryAsync(folderPath);
        var existing = Libraries.FirstOrDefault(item =>
            string.Equals(item.RootPath, library.RootPath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            Libraries.Add(library);
            ActivityFeed.Insert(0, $"已登记素材库目录：{library.RootPath}");
        }
        else
        {
            library = existing;
            ActivityFeed.Insert(0, $"素材库目录已存在：{library.RootPath}");
        }

        SelectedLibrary = library;
        OperatorNotice = $"已登记素材库“{library.Name}”，下一步请执行扫描。";
        RebuildMetrics();
        await ScanLibraryCoreAsync(library);
    }

    [RelayCommand]
    private void SelectLibrary(LibraryWorkspace? library)
    {
        if (library is null)
        {
            return;
        }

        SelectedLibrary = library;
    }

    partial void OnSelectedLibraryChanged(LibraryWorkspace? value)
    {
        _ = LoadSelectedLibraryAsync(value);
    }

    partial void OnSelectedAssetChanged(ManagedAssetRecord? value)
    {
        SelectedAssetTags.Clear();

        if (value is null)
        {
            SelectedAssetName = "尚未选择素材";
            SelectedAssetLibrary = "请先扫描一个素材库";
            SelectedAssetPath = "当前未加载本地文件路径";
            SelectedAssetType = "未选择";
            SelectedAssetStage = "待选择";
            SelectedAssetAiState = "未排队";
            SelectedAssetDetail = "右侧详情区域会展示当前素材的路径、类型和扫描结果。";
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
    private async Task RefreshWorkspaceAsync()
    {
        if (SelectedLibrary is null)
        {
            OperatorNotice = "请先选择一个素材库，再刷新扫描结果。";
            return;
        }

        await ScanLibraryCoreAsync(SelectedLibrary);
    }

    [RelayCommand]
    private async Task ScanSelectedLibraryAsync()
    {
        if (SelectedLibrary is null)
        {
            OperatorNotice = "请先添加或选择一个素材库。";
            return;
        }

        await ScanLibraryCoreAsync(SelectedLibrary);
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
            ? $"提示词已准备发送到 {BackendEndpoint}，当前先保留为桌面端联动占位。"
            : "Python 模型服务尚未就绪，当前先完成本地素材扫描与管理。";
        ActivityFeed.Insert(0, $"提示词草稿已更新：{target}");
    }

    private async Task InitializeBackendAsync()
    {
        BackendStatusTitle = "Python 模型服务启动中";
        BackendStatusDetail = "正在等待 /health 返回，就绪后桌面端可将提示词任务转发给 HTTP 后端。";

        try
        {
            await _backendLauncher!.StartAsync();
            BackendEndpoint = _backendLauncher.BaseUrl;
            BackendStatusTitle = "Python 模型服务已连接";
            BackendStatusDetail = "模型服务只负责大模型 HTTP 接口，不再承担素材库、文件扫描或目录管理。";
            ActivityFeed.Insert(0, $"模型网关就绪：{BackendEndpoint}");
        }
        catch (Exception ex)
        {
            BackendStatusTitle = "Python 模型服务未就绪";
            BackendStatusDetail = ex.Message;
            OperatorNotice = "后端启动失败，当前仍可继续使用桌面端素材库管理。";
            ActivityFeed.Insert(0, $"模型网关启动失败：{ex.Message}");
        }
    }

    private async Task LoadLibrariesAsync()
    {
        Libraries.Clear();
        _allAssets.Clear();
        VisibleAssets.Clear();

        if (_assetLibraryService is null)
        {
            SetEmptyWorkspaceState();
            return;
        }

        var libraries = await _assetLibraryService.GetLibrariesAsync();
        foreach (var library in libraries)
        {
            Libraries.Add(library);
        }

        RebuildMetrics();

        if (Libraries.Count == 0)
        {
            SetEmptyWorkspaceState();
            ActivityFeed.Insert(0, "当前尚未登记素材库目录。");
            return;
        }

        SelectedLibrary = Libraries[0];
    }

    private async Task LoadSelectedLibraryAsync(LibraryWorkspace? library)
    {
        if (library is null)
        {
            VisibleAssets.Clear();
            SetEmptyWorkspaceState();
            return;
        }

        WorkspaceTitle = library.Name;
        WorkspaceSummary = library.RootPath;
        AssetSummary = library.AssetCount > 0
            ? $"当前素材库已加载 {library.AssetCount} 个支持的素材文件。"
            : library.Summary;

        if (!_allAssets.Any(asset => asset.LibraryName == library.Name))
        {
            await ScanLibraryCoreAsync(library);
            return;
        }

        RebuildVisibleAssets(library);
    }

    private async Task ScanLibraryCoreAsync(LibraryWorkspace library)
    {
        if (_assetLibraryService is null || _isLibraryScanRunning)
        {
            return;
        }

        try
        {
            _isLibraryScanRunning = true;
            library.SyncMode = "扫描中";
            library.Summary = "正在扫描目录下的文本、图片、视频和音频文件。";
            OperatorNotice = $"正在扫描素材库：{library.RootPath}";

            var assets = await _assetLibraryService.ScanLibraryAsync(library);
            _allAssets.RemoveAll(asset => asset.LibraryName == library.Name);
            _allAssets.AddRange(assets);

            library.AssetCount = assets.Count;
            library.SyncMode = "已扫描";
            library.Summary = assets.Count == 0
                ? "目录中没有找到受支持的文本、图片、视频或音频文件。"
                : $"已扫描 {assets.Count} 个素材文件，可在右侧列表查看。";

            WorkspaceTitle = library.Name;
            WorkspaceSummary = library.RootPath;
            AssetSummary = library.Summary;

            RebuildVisibleAssets(library);
            RebuildMetrics();
            ActivityFeed.Insert(0, $"扫描完成：{library.Name}，共 {assets.Count} 个素材文件。");
        }
        catch (Exception ex)
        {
            library.SyncMode = "扫描失败";
            library.Summary = ex.Message;
            OperatorNotice = $"扫描失败：{ex.Message}";
            ActivityFeed.Insert(0, $"扫描失败：{library.Name} -> {ex.Message}");
        }
        finally
        {
            _isLibraryScanRunning = false;
        }
    }

    private void RebuildVisibleAssets(LibraryWorkspace? library)
    {
        VisibleAssets.Clear();

        IEnumerable<ManagedAssetRecord> items = _allAssets;
        if (library is not null)
        {
            items = items.Where(asset => asset.LibraryName == library.Name);
        }

        foreach (var asset in items.OrderBy(asset => asset.AssetType).ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase))
        {
            VisibleAssets.Add(asset);
        }

        SelectedAsset = VisibleAssets.FirstOrDefault();
    }

    private void RebuildMetrics()
    {
        Metrics.Clear();

        var totalAssets = _allAssets.Count;
        var pendingModel = _allAssets.Count(asset => asset.AiState.Contains("待", StringComparison.Ordinal));
        var textImageVideoAudio = _allAssets
            .Select(asset => asset.AssetType)
            .Distinct(StringComparer.Ordinal)
            .Count();

        Metrics.Add(new DashboardMetric("本地素材库", Libraries.Count.ToString("D2"), "Avalonia 侧维护目录登记"));
        Metrics.Add(new DashboardMetric("已扫描素材", totalAssets.ToString("D2"), "文本 | 图片 | 视频 | 音频"));
        Metrics.Add(new DashboardMetric("待模型处理", pendingModel.ToString("D2"), "仅把提示词和推理请求交给 Python"));
        Metrics.Add(new DashboardMetric("素材类型", textImageVideoAudio.ToString("D2"), "当前已识别的文件类型数量"));
    }

    private void SeedStaticData()
    {
        AiCapabilities.Clear();
        AiCapabilities.Add(new AiCapabilityRecord("健康检查", "/health", "供桌面端确认 Python 模型服务是否可达。"));
        AiCapabilities.Add(new AiCapabilityRecord("能力清单", "/api/v1/model/capabilities", "返回当前模型网关的槽位、模式和占位能力。"));
        AiCapabilities.Add(new AiCapabilityRecord("文本生成", "/api/v1/model/generate", "只负责提示词转发与模型输出，不管理素材目录。"));

        ActivityFeed.Clear();
        ActivityFeed.Add("桌面端作为素材管理主入口，先固定本地工作流边界。");
        ActivityFeed.Add("本地素材库目录会持久化为 JSON，并由 .NET 负责目录扫描与文件展示。");
        ActivityFeed.Add("Python 进程仅暴露 HTTP 模型能力，避免再次把素材管理逻辑塞回后端。");
    }

    private void SetEmptyWorkspaceState()
    {
        WorkspaceTitle = "尚未添加素材库";
        WorkspaceSummary = "请选择一个本地文件夹并登记为素材库目录。";
        AssetSummary = "支持扫描文本、图片、视频和音频文件。";
        SelectedAsset = null;
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
