# Avalonia MVVM 重构实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 Avalonia 端 MVVM 架构违反问题，消除 `LibraryCatalogService` 等 Service 中的 ViewModel 职责，消除属性转发反模式，将 code-behind 逻辑迁移到 Command。

**Architecture:** 7 个 Phase 逐步迁移，每个 Phase 保持构建可运行。核心思路：提取 Service 接口 → 创建真正 ViewModel → 迁移状态 → 消除转发 → 迁移 Command → 修复 DI/设计时/装配。

**Tech Stack:** .NET 10 + Avalonia + CommunityToolkit.Mvvm + Autofac

---

## 文件改动总览

### 新建文件
| 文件 | 说明 |
|------|------|
| `Application/Services/AssetLibrary/ILibraryCatalogService.cs` | LibraryCatalogService 纯接口 |
| `Avalonia/Services/Backend/IBackendSessionService.cs` | BackendSessionService 纯接口 |
| `Avalonia/ViewModels/LibraryWorkspaceViewModel.cs` | 持有素材库 UI 状态的 ViewModel |
| `Avalonia/ViewModels/BackendStatusViewModel.cs` | 持有后端状态 UI 的 ViewModel |

### 修改文件
| 文件 | 说明 |
|------|------|
| `Avalonia/Services/Library/LibraryCatalogService.cs` | 移除 `[ObservableProperty]`，实现 `ILibraryCatalogService` |
| `Avalonia/Services/Backend/BackendSessionService.cs` | 移除 `[ObservableProperty]`，实现 `IBackendSessionService` |
| `Avalonia/ViewModels/LibraryPageViewModel.cs` | 暴露子 ViewModel，消除转发 |
| `Avalonia/ViewModels/LibraryExplorerViewModel.cs` | 依赖 `LibraryWorkspaceViewModel`，消除转发 |
| `Avalonia/ViewModels/AssetDetailViewModel.cs` | 依赖 `LibraryWorkspaceViewModel`，消除转发 |
| `Avalonia/ViewModels/OverviewPageViewModel.cs` | 依赖 `BackendStatusViewModel`，消除转发 |
| `Avalonia/ViewModels/SettingsPageViewModel.cs` | 依赖 `BackendStatusViewModel`，消除转发 |
| `Avalonia/ViewModels/MainWindowViewModel.cs` | 消除转发属性 |
| `Avalonia/ViewModels/AssetSearchPanelViewModel.cs` | 消除转发 |
| `Avalonia/ViewModels/AssetVectorizationPanelViewModel.cs` | 消除转发 |
| `Avalonia/ViewModels/AssetDescriptionPanelViewModel.cs` | 消除转发 |
| `Avalonia/Views/Pages/LibraryPage.axaml` | 修复绑定路径 |
| `Avalonia/Views/Pages/LibraryPage.axaml.cs` | 移除事件处理器，改用 Command |
| `Avalonia/Views/MainWindow.axaml` | 修复绑定路径 |
| `Avalonia/Views/QuickSearchWindow.axaml` | 修复绑定路径 |
| `Avalonia/DependencyInjection/AvaloniaModule.cs` | 更新 DI 注册 |
| `Avalonia/App.axaml.cs` | 修复视图装配 |

---

### Phase 1a: 抽取 `ILibraryCatalogService` 接口

**目标：** 创建纯服务接口，不包含任何 UI 状态。

**Files:**
- Create: `src/avalonia/AssetsLibrarySystem.Application/Services/AssetLibrary/ILibraryCatalogService.cs`

- [ ] **Step 1: 创建 ILibraryCatalogService 接口**

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetLibrary;

/// <summary>素材库目录服务，纯数据操作，不持有 UI 状态</summary>
public interface ILibraryCatalogService
{
    Task<IReadOnlyList<LibraryWorkspace>> GetLibrariesAsync(CancellationToken ct = default);
    Task<LibraryWorkspace> AddLibraryAsync(string folderPath, CancellationToken ct = default);
    Task<IReadOnlyList<ManagedAssetRecord>> ScanLibraryAsync(LibraryWorkspace library, CancellationToken ct = default);

    // CRUD
    Task DeleteLibraryAsync(long libraryId, CancellationToken ct = default);
    Task UpdateLibraryAsync(long libraryId, string newName, CancellationToken ct = default);
    Task DeleteAssetAsync(long assetId, CancellationToken ct = default);
    Task UpdateAssetTagsAsync(long assetId, string[] tags, CancellationToken ct = default);
    Task UpdateAssetNameAsync(long assetId, string newName, CancellationToken ct = default);
}
```

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 0 errors

- [ ] **Step 3: 提交**

```bash
git add -A && git commit -m "refactor: extract ILibraryCatalogService interface"
```

---

### Phase 1b: 抽取 `IBackendSessionService` 接口

**目标：** 创建纯服务接口，不包含 UI 状态。

**Files:**
- Create: `src/avalonia/AssetsLibrarySystem.Avalonia/Services/Backend/IBackendSessionService.cs`

- [ ] **Step 1: 创建 IBackendSessionService 接口**

```csharp
using System;
using System.Threading.Tasks;

namespace AssetsLibrarySystem.Avalonia.Services.Backend;

/// <summary>后端会话服务，纯操作，不持有 UI 状态</summary>
public interface IBackendSessionService
{
    bool IsBackendReady { get; }
    string BaseUrl { get; }

    Task InitializeAsync();
    event Action? BackendStatusChanged;
}
```

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 0 errors

- [ ] **Step 3: 提交**

```bash
git add -A && git commit -m "refactor: extract IBackendSessionService interface"
```

---

### Phase 1c: 创建 `LibraryWorkspaceViewModel`

**目标：** 将 `LibraryCatalogService` 中的 UI 状态迁移到真正的 ViewModel。

**Files:**
- Create: `src/avalonia/AssetsLibrarySystem.Avalonia/ViewModels/LibraryWorkspaceViewModel.cs`
- Modify: `src/avalonia/AssetsLibrarySystem.Avalonia/Services/Library/LibraryCatalogService.cs` — 移除 `[ObservableProperty]` 和 UI 状态，保留纯服务方法

- [ ] **Step 1: 创建 LibraryWorkspaceViewModel**

```csharp
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetLibrary;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

/// <summary>
/// 素材库工作台 ViewModel，持有所有 UI 状态。
/// 代替原来 LibraryCatalogService 中的 ObservableProperty 职责。
/// </summary>
public sealed partial class LibraryWorkspaceViewModel : ObservableObject
{
    private ILibraryCatalogService CatalogService { get; }
    private ActivityFeedService ActivityFeedService { get; }
    private List<ManagedAssetRecord> AllAssets { get; } = [];

    public LibraryWorkspaceViewModel(
        ILibraryCatalogService catalogService,
        ActivityFeedService activityFeedService)
    {
        CatalogService = catalogService;
        ActivityFeedService = activityFeedService;

        Metrics = [];
        AssetTreeRoots = [];
        Libraries = [];
        CurrentExplorerItems = [];
        SelectedAssetDescriptionAngles = [];
        SetEmptyWorkspaceState();
    }

    // ===== Observable 状态 =====

    public ObservableCollection<DashboardMetric> Metrics { get; }
    public ObservableCollection<AssetLibraryTreeNode> AssetTreeRoots { get; }
    public ObservableCollection<LibraryWorkspace> Libraries { get; }
    public ObservableCollection<AssetLibraryTreeNode> CurrentExplorerItems { get; }
    public ObservableCollection<AngleDescriptionRecord> SelectedAssetDescriptionAngles { get; }

    [ObservableProperty] public partial LibraryWorkspace? SelectedLibrary { get; set; }
    [ObservableProperty] public partial ManagedAssetRecord? SelectedAsset { get; set; }
    [ObservableProperty] public partial AssetLibraryTreeNode? SelectedAssetTreeNode { get; set; }
    [ObservableProperty] public partial string WorkspaceTitle { get; set; } = "本地素材工作台";
    [ObservableProperty] public partial string WorkspaceSummary { get; set; }
    [ObservableProperty] public partial string AssetSummary { get; set; }
    [ObservableProperty] public partial string ExplorerTitle { get; set; } = "素材库";
    [ObservableProperty] public partial string ExplorerSummary { get; set; }
    [ObservableProperty] public partial string ExplorerPath { get; set; } = "尚未选择";
    [ObservableProperty] public partial bool CanNavigateUp { get; set; }
    [ObservableProperty] public partial string OperatorNotice { get; set; }
    [ObservableProperty] public partial string SelectedAssetName { get; set; } = "尚未选择素材";
    [ObservableProperty] public partial string SelectedAssetLibrary { get; set; } = "请先添加并扫描一个素材库";
    [ObservableProperty] public partial string SelectedAssetPath { get; set; } = "当前未加载本地文件路径";
    [ObservableProperty] public partial string SelectedAssetType { get; set; } = "未选择";
    [ObservableProperty] public partial string SelectedAssetStage { get; set; } = "待选择";
    [ObservableProperty] public partial string SelectedAssetAiState { get; set; } = "未描述";
    [ObservableProperty] public partial string SelectedAssetDetail { get; set; } = "当前素材还没有可显示的 AI 描述。";
    [ObservableProperty] public partial string SelectedAssetSubtype { get; set; } = "";
    [ObservableProperty] public partial string SelectedAssetDescriptionState { get; set; } = "未描述";
    [ObservableProperty] public partial string SelectedAssetDescriptionStorePath { get; set; } = "尚未生成描述记录";
    [ObservableProperty] public partial string SelectedAssetDescriptionGeneratedAt { get; set; } = "未生成";
    [ObservableProperty] public partial string SelectedAssetDescriptionMode { get; set; } = "未生成";
    [ObservableProperty] public partial string SelectedAssetDescriptionTokenUsage { get; set; } = "未返回 token 用量";
    [ObservableProperty] public partial string SelectedAssetDescriptionPrompt { get; set; } = "尚未生成 prompt。";
    [ObservableProperty] public partial string SelectedAssetDescriptionSystemPrompt { get; set; } = "尚未生成 system prompt。";
    [ObservableProperty] public partial string SelectedAssetDescriptionText { get; set; } = "当前素材还没有可显示的 AI 描述。";
    [ObservableProperty] public partial string DescriptionSelectionSummary { get; set; } = "请选择左侧素材库、目录或单个素材，再安排描述任务。";

    // ===== 初始化 =====

    public async Task InitializeAsync()
    {
        Log.Information("初始化素材库工作台。");
        await LoadLibrariesAsync();
    }

    private async Task LoadLibrariesAsync()
    {
        Libraries.Clear();
        AssetTreeRoots.Clear();
        AllAssets.Clear();

        var libraries = await CatalogService.GetLibrariesAsync();
        foreach (var library in libraries)
            Libraries.Add(library);

        RebuildAssetTree();
        RebuildMetrics();

        if (Libraries.Count == 0)
        {
            SetEmptyWorkspaceState();
            ActivityFeedService.Add("当前尚未登记素材库目录。");
            return;
        }

        SelectedLibrary = Libraries[0];
        WorkspaceTitle = SelectedLibrary.Name;
        WorkspaceSummary = SelectedLibrary.RootPath;
        _ = LoadAllLibraryDataAsync();
    }

    // ===== 导航命令 =====

    [RelayCommand]
    private void NavigateUp()
    {
        // 从 LibraryCatalogService.Tree.cs 迁移
        var container = GetExplorerContainerNode(SelectedAssetTreeNode);
        if (container is null) return;
        if (container.Kind == AssetLibraryTreeNodeKind.Library)
        {
            SelectedAssetTreeNode = null;
            return;
        }
        SelectedAssetTreeNode = FindParentTreeNode(container);
    }

    [RelayCommand]
    private void OpenExplorerItem(AssetLibraryTreeNode? node)
    {
        if (node is not null)
            SelectedAssetTreeNode = node;
    }

    [RelayCommand]
    private void SelectLibrary(LibraryWorkspace? library)
    {
        if (library is null) return;
        SelectedLibrary = library;
        SelectedAssetTreeNode = FindLibraryTreeNode(library.Id);
    }

    // ===== CRUD 命令 =====

    [RelayCommand]
    private async Task DeleteAssetAsync()
    {
        if (SelectedAsset is null || SelectedAssetTreeNode is null) return;
        await CatalogService.DeleteAssetAsync(SelectedAsset.DatabaseId);
        AllAssets.Remove(SelectedAsset);
        SelectedAsset = null;
        ResetSelectedAssetDescription();
        RebuildAssetTree();
        RebuildMetrics();
        ActivityFeedService.Add($"素材已删除：{SelectedAsset?.Name ?? "unknown"}");
    }

    [RelayCommand]
    private async Task DeleteLibraryAsync()
    {
        if (SelectedLibrary is null) return;
        var id = SelectedLibrary.Id;
        var name = SelectedLibrary.Name;
        AllAssets.RemoveAll(a => a.LibraryName == name);
        await CatalogService.DeleteLibraryAsync(id);
        Libraries.Remove(SelectedLibrary);
        SelectedLibrary = null;
        SelectedAsset = null;
        SelectedAssetTreeNode = null;
        SetEmptyWorkspaceState();
        RebuildMetrics();
        ActivityFeedService.Add($"素材库已删除：{name}");
    }

    [RelayCommand]
    private async Task AddLibraryAsync(string folderPath)
    {
        var library = await CatalogService.AddLibraryAsync(folderPath);
        Libraries.Add(library);
        SelectedLibrary = library;
        RebuildAssetTree();
        RebuildMetrics();
        _ = Task.Run(() => ScanLibraryAsync(library));
    }

    [RelayCommand]
    private async Task ScanLibraryAsync(LibraryWorkspace? library = null)
    {
        library ??= SelectedLibrary;
        if (library is null) return;
        var assets = await CatalogService.ScanLibraryAsync(library);
        AllAssets.RemoveAll(a => a.LibraryName == library.Name);
        AllAssets.AddRange(assets);
        library.AssetCount = assets.Count;
        RebuildAssetTree();
        RebuildMetrics();
    }

    // ===== 辅助方法 =====

    public void SetOperatorNotice(string message)
    {
        OperatorNotice = message;
    }

    public void RebuildMetrics()
    {
        Metrics.Clear();
        Metrics.Add(new DashboardMetric("素材总数", AllAssets.Count.ToString("D2"), $"{Libraries.Count} 个本地素材库"));
        Metrics.Add(new DashboardMetric("文本", AllAssets.Count(a => a.AssetType == "文本").ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("图片", AllAssets.Count(a => a.AssetType == "图片").ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("视频", AllAssets.Count(a => a.AssetType == "视频").ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("音频", AllAssets.Count(a => a.AssetType == "音频").ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("已描述", AllAssets.Count(a => a.IsDescribed).ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("已向量化", AllAssets.Count(a => a.IsVectorized).ToString("D2"), ""));
        Metrics.Add(new DashboardMetric("待描述", AllAssets.Count(a => !a.IsDescribed).ToString("D2"), ""));
    }

    private void RebuildAssetTree()
    {
        AssetTreeRoots.Clear();
        foreach (var lib in Libraries.OrderBy(l => l.Name))
            AssetTreeRoots.Add(BuildLibraryTree(lib));
        UpdateExplorerView(SelectedAssetTreeNode);
    }

    private void ResetSelectedAssetDescription()
    {
        SelectedAssetDescriptionState = "未描述";
        SelectedAssetDescriptionStorePath = "尚未生成描述记录";
        SelectedAssetDescriptionGeneratedAt = "未生成";
        SelectedAssetDescriptionMode = "未生成";
        SelectedAssetDescriptionTokenUsage = "未返回 token 用量";
        SelectedAssetDescriptionPrompt = "尚未生成 prompt。";
        SelectedAssetDescriptionSystemPrompt = "尚未生成 system prompt。";
        SelectedAssetDescriptionText = "当前素材还没有可显示的 AI 描述。";
        SelectedAssetDescriptionAngles.Clear();
    }

    private void SetEmptyWorkspaceState()
    {
        WorkspaceTitle = "尚未添加素材库";
        WorkspaceSummary = "请选择一个本地文件夹并登记为素材库目录。";
        AssetSummary = "支持扫描文本、图片、视频和音频文件。";
        SelectedAsset = null;
    }

    // ===== 树形导航方法（从 LibraryCatalogService.Tree.cs 迁移） =====

    private AssetLibraryTreeNode BuildLibraryTree(LibraryWorkspace library) { /* 从 LibraryCatalogService.Tree.cs 迁移 */ }
    private void UpdateExplorerView(AssetLibraryTreeNode? node) { /* 迁移 */ }
    private AssetLibraryTreeNode? FindLibraryTreeNode(long id) { /* 迁移 */ }
    private AssetLibraryTreeNode? FindParentTreeNode(AssetLibraryTreeNode node) { /* 迁移 */ }
    private AssetLibraryTreeNode? GetExplorerContainerNode(AssetLibraryTreeNode? node) { /* 迁移 */ }
    private void LoadAllLibraryDataAsync() { /* 迁移 */ }
    // ... 其余树形方法
}
```

> **注意：** 上述代码省略了树形导航方法的完整实现。实际实现时，需要将 `LibraryCatalogService.cs` 中以下方法完整迁移到 `LibraryWorkspaceViewModel`：
> - `BuildLibraryTree`, `UpdateExplorerView`, `FindLibraryTreeNode`, `FindParentTreeNode`, `GetExplorerContainerNode`
> - `NavigateUp`, `OpenExplorerItem`, `ApplyTreeSelection`
> - `RebuildMetrics`, `SetEmptyWorkspaceState`
> - `UpdateSelectedAssetDetails`, `ResetSelectedAssetDescription`
> - `LoadAllLibraryDataAsync`, `LoadLibrariesAsync`, `ScanLibraryAsync`

- [ ] **Step 2: 清理 LibraryCatalogService**

移除 LibraryCatalogService 中的所有 `[ObservableProperty]`、`partial` 方法、`OnXxxChanged` 回调、UI 状态属性。保留的只有实现 `ILibraryCatalogService` 的方法。

- [ ] **Step 3: 构建验证**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 编译错误（因为 ViewModel 还引用旧的 LibraryCatalogService 属性）

- [ ] **Step 4: 提交**

```bash
git add -A && git commit -m "refactor: create LibraryWorkspaceViewModel, migrate UI state from LibraryCatalogService"
```

---

### Phase 1d: 创建 `BackendStatusViewModel`

**目标：** 将 `BackendSessionService` 中的 UI 状态迁移到真正的 ViewModel。

**Files:**
- Create: `src/avalonia/AssetsLibrarySystem.Avalonia/ViewModels/BackendStatusViewModel.cs`
- Modify: `src/avalonia/AssetsLibrarySystem.Avalonia/Services/Backend/BackendSessionService.cs` — 移除 `[ObservableProperty]`

- [ ] **Step 1: 创建 BackendStatusViewModel**

```csharp
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

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
        try
        {
            await BackendSession.InitializeAsync();
            BackendStatusTitle = "Python 引擎已就绪";
            BackendStatusStage = "就绪";
        }
        catch (Exception ex)
        {
            BackendStatusTitle = "Python 引擎初始化失败";
            BackendStatusStage = "启动失败";
            BackendStatusDetail = ex.Message;
            Log.Error(ex, "Python 引擎初始化失败。");
        }
    }

    private void OnBackendStatusChanged()
    {
        // 当后端状态变更时刷新 UI 绑定
        OnPropertyChanged(nameof(IsBackendReady));
        OnPropertyChanged(nameof(BaseUrl));
    }
}
```

- [ ] **Step 2: 清理 BackendSessionService**

移除 BackendSessionService 中的所有 `[ObservableProperty]`、`partial` 方法。保留的只有实现 `IBackendSessionService` 的方法。

- [ ] **Step 3: 构建验证**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 编译错误（因为 ViewModel 还引用旧的 BackendSessionService 属性）

- [ ] **Step 4: 提交**

```bash
git add -A && git commit -m "refactor: create BackendStatusViewModel, migrate UI state from BackendSessionService"
```

---

### Phase 2: 消除属性转发

**目标：** 所有 ViewModel 不再转发 Service 属性，AXAML 直接绑定到子 ViewModel。

**Files:**
- Modify: `LibraryPageViewModel.cs`, `LibraryExplorerViewModel.cs`, `AssetDetailViewModel.cs`, `OverviewPageViewModel.cs`, `SettingsPageViewModel.cs`, `MainWindowViewModel.cs`, `AssetSearchPanelViewModel.cs`, `AssetVectorizationPanelViewModel.cs`, `AssetDescriptionPanelViewModel.cs`
- Modify: `LibraryPage.axaml`, `MainWindow.axaml`, `QuickSearchWindow.axaml`

- [ ] **Step 1: 重构 LibraryPageViewModel**

```csharp
public sealed partial class LibraryPageViewModel : ObservableObject
{
    // 不再有 50 行转发属性
    // 直接暴露子 ViewModel，AXAML 通过绑定路径访问

    public LibraryWorkspaceViewModel Workspace { get; }
    public AssetDetailViewModel AssetDetail { get; }
    public AssetSearchPanelViewModel SearchPanel { get; }
    public BackendStatusViewModel BackendStatus { get; }
    public ObservableCollection<string> ActivityFeed { get; }

    public LibraryPageViewModel(
        LibraryWorkspaceViewModel workspace,
        AssetDetailViewModel assetDetail,
        AssetSearchPanelViewModel searchPanel,
        BackendStatusViewModel backendStatus,
        ActivityFeedService activityFeedService)
    {
        Workspace = workspace;
        AssetDetail = assetDetail;
        SearchPanel = searchPanel;
        BackendStatus = backendStatus;
        ActivityFeed = activityFeedService.Entries;

        // 订阅子 ViewModel 的 PropertyChanged 事件，向上冒泡
        Workspace.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
        AssetDetail.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
        SearchPanel.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
        BackendStatus.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
    }
}
```

- [ ] **Step 2: 重构 AssetDetailViewModel**

```csharp
public sealed partial class AssetDetailViewModel : ObservableObject
{
    private LibraryWorkspaceViewModel Workspace { get; }

    public AssetDetailViewModel(LibraryWorkspaceViewModel workspace)
    {
        Workspace = workspace;
        Workspace.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
    }

    // 直接委托给 Workspace，不再转发
    public string SelectedAssetName => Workspace.SelectedAssetName;
    public string SelectedAssetType => Workspace.SelectedAssetType;
    // ... 其他属性仍然是转发，但只转发一层（从 WorkspaceViewModel），
    // 不再从 Service 转发。这是可接受的，因为 ViewModel 是同一层。
}
```

- [ ] **Step 3: 修复 AXAML 绑定路径**

LibraryPage.axaml 中的绑定路径从：

```xml
<!-- Before -->
<TextBlock Text="{Binding SelectedAssetType}" />
<TextBlock Text="{Binding SelectedAssetDescriptionText}" />
```

改为：

```xml
<!-- After -->
<TextBlock Text="{Binding AssetDetail.SelectedAssetType}" />
<TextBlock Text="{Binding AssetDetail.SelectedAssetDescriptionText}" />
```

- [ ] **Step 4: 更新 MainWindowViewModel**

`MainWindowViewModel` 中引用 `BackendSessionService` 的属性改为引用 `BackendStatusViewModel`。

- [ ] **Step 5: 构建验证**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 0 errors

- [ ] **Step 6: 提交**

```bash
git add -A && git commit -m "refactor: eliminate property forwarding, fix binding paths in AXAML"
```

---

### Phase 3: Code-Behind 逻辑迁移到 Command

**目标：** 将 `LibraryPage.axaml.cs` 中的事件处理器改为 Command 绑定。

**Files:**
- Modify: `LibraryWorkspaceViewModel.cs` — 追加命令
- Modify: `LibraryPage.axaml` — 右键菜单改用 Command
- Modify: `LibraryPage.axaml.cs` — 移除事件处理器

- [ ] **Step 1: 在 LibraryWorkspaceViewModel 追加命令**

```csharp
[RelayCommand]
private async Task AddLibraryFolderAsync()
{
    // View 通过 App.axaml.cs 或 DI 处理文件夹选择器交互
    // ViewModel 提供 AddLibraryAsync 方法
}

[RelayCommand]
private async Task RevealInExplorer(AssetLibraryTreeNode? node)
{
    if (node is null || string.IsNullOrWhiteSpace(node.FullPath)) return;
    var path = Path.GetFullPath(node.FullPath);
    Process.Start(new ProcessStartInfo
    {
        FileName = "explorer.exe",
        UseShellExecute = true,
        Arguments = node.Kind == AssetLibraryTreeNodeKind.File
            ? $"/select,\"{path}\""
            : $"\"{path}\""
    });
}
```

- [ ] **Step 2: 右键菜单改用 Command 绑定**

```xml
<!-- Before -->
<MenuItem Header="删除" Click="DeleteNode_Click" CommandParameter="{Binding}" />

<!-- After -->
<MenuItem Header="删除"
          Command="{Binding $parent[ItemsControl].DataContext.Workspace.DeleteAssetCommand}"
          CommandParameter="{Binding}" />
```

- [ ] **Step 3: 清理 LibraryPage.axaml.cs**

移除的方法：
- `DeleteNode_Click`
- `EditTags_Click`
- `RenameNode_Click`
- `QueueDescriptionForNode_Click`（已改为 Command）
- `VectorizeDescriptionsForNode_Click`（已改为 Command）
- `DeleteDescriptionForNode_Click`（已改为 Command）

保留的方法：
- `AddLibraryFolder_Click` — 需要系统对话框
- `RevealInExplorer_Click` — 如果还没有改为 Command

- [ ] **Step 4: 构建验证**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 0 errors

- [ ] **Step 5: 提交**

```bash
git add -A && git commit -m "refactor: migrate code-behind logic to Commands, remove event handlers"
```

---

### Phase 4: 修复 DI 注册

**目标：** ViewModel 依赖接口而非具体类。

**Files:**
- Modify: `AvaloniaModule.cs` — 注册接口
- Modify: 所有 ViewModel 构造函数 — 改为接口参数

- [ ] **Step 1: 更新 DI 注册**

```csharp
// AvaloniaServiceModule.cs
builder.RegisterType<LibraryCatalogService>()
    .As<ILibraryCatalogService>()
    .SingleInstance();

builder.RegisterType<BackendSessionService>()
    .As<IBackendSessionService>()
    .SingleInstance();
```

- [ ] **Step 2: 更新 ViewModel 构造函数**

```csharp
// LibraryWorkspaceViewModel 改为接口依赖
public LibraryWorkspaceViewModel(
    ILibraryCatalogService catalogService,  // 不再是具体类
    ActivityFeedService activityFeedService)

// BackendStatusViewModel 改为接口依赖
public BackendStatusViewModel(
    IBackendSessionService backendSession)  // 不再是具体类
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 0 errors

- [ ] **Step 4: 提交**

```bash
git add -A && git commit -m "refactor: update DI registrations to use interfaces"
```

---

### Phase 5: 修复设计时构造函数

**目标：** 设计时使用 mock 数据，不创建真实服务。

**Files:**
- Modify: 所有 ViewModel 的设计时构造函数

- [ ] **Step 1: 为设计时构造函数提供 mock 数据**

```csharp
// LibraryWorkspaceViewModel 设计时构造函数
[Obsolete("仅供设计器使用")]
public LibraryWorkspaceViewModel()
    : this(new NullLibraryCatalogService(), new ActivityFeedService())
{
    Libraries.Add(new LibraryWorkspace(1, "示例素材库", @"D:\素材", "示例", "已登记", 42));
    SelectedLibrary = Libraries[0];
    WorkspaceTitle = "示例素材库";
    WorkspaceSummary = @"D:\素材";
    SelectedAssetName = "示例素材.mp3";
    SelectedAssetType = "音频";
    // 填充设计时数据
}

// 空实现，不访问数据库
private sealed class NullLibraryCatalogService : ILibraryCatalogService
{
    public Task<IReadOnlyList<LibraryWorkspace>> GetLibrariesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LibraryWorkspace>>([]);
    public Task<LibraryWorkspace> AddLibraryAsync(string folderPath, CancellationToken ct = default)
        => throw new NotSupportedException();
    // ... 其他方法返回空或抛出 NotSupportedException
}
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 0 errors

- [ ] **Step 3: 提交**

```bash
git add -A && git commit -m "refactor: fix design-time constructors with mock data"
```

---

### Phase 6: 修复 App.axaml.cs

**目标：** 使用 DI 解析 View 而不是手动创建。

**Files:**
- Modify: `App.axaml.cs`
- Modify: `AvaloniaModule.cs` — 注册 View 类型

- [ ] **Step 1: 注册 View 到 DI**

```csharp
// AvaloniaModule.cs
builder.RegisterType<MainWindow>().AsSelf();
builder.RegisterType<QuickSearchWindow>().AsSelf();
```

- [ ] **Step 2: 简化 App.axaml.cs**

```csharp
public override async void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        BuildContainer();

        var mainWindow = Container!.Resolve<MainWindow>();
        desktop.MainWindow = mainWindow;
        mainWindow.Show();

        // 初始化
        if (Container!.Resolve<MainWindowViewModel>() is { } vm)
            await vm.InitializeAsync();
    }
    base.OnFrameworkInitializationCompleted();
}
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 0 errors

- [ ] **Step 4: 提交**

```bash
git add -A && git commit -m "refactor: use DI for view resolution in App.axaml.cs"
```

---

### Phase 7: 修复 ShellWindowService

**目标：** 不依赖具体 View 类型，使用 `Window` 基类。

**Files:**
- Modify: `IShellWindowService.cs`
- Modify: `ShellWindowService.cs`
- Modify: `App.axaml.cs` — 调整 Attach 调用

- [ ] **Step 1: 修改接口**

```csharp
public interface IShellWindowService
{
    void AttachMainWindow(Window window);  // Window 而非 MainWindow
    void AttachQuickSearchWindow(Window window);  // Window 而非 QuickSearchWindow
    // ... 其余方法不变
}
```

- [ ] **Step 2: 实现类使用 Window 基类**

```csharp
public sealed class ShellWindowService : IShellWindowService
{
    private Window? MainWindow { get; set; }
    private Window? QuickSearchWindow { get; set; }

    public void AttachMainWindow(Window window) { MainWindow = window; }
    public void AttachQuickSearchWindow(Window window) { QuickSearchWindow = window; }
    // ... 其余实现不变
}
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 0 errors

- [ ] **Step 4: 提交**

```bash
git add -A && git commit -m "refactor: decouple ShellWindowService from concrete View types"
```

---

## 自检清单

- [ ] 每个 Phase 独立构建通过
- [ ] 无 `[ObservableProperty]` 残留在 Service 层
- [ ] 无属性转发从 Service 到 ViewModel
- [ ] AXAML 绑定路径正确指向子 ViewModel
- [ ] 设计时构造函数不创建真实数据库连接
- [ ] `LibraryPage.axaml.cs` 只保留系统对话框代码
- [ ] `App.axaml.cs` 使用 DI 解析 View
- [ ] `ShellWindowService` 不引用具体 View 类型
- [ ] 所有测试通过
- [ ] 应用可正常启动和运行