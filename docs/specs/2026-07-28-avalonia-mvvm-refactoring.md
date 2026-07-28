# Avalonia 端 MVVM 重构方案

> 设计文档
> 日期：2026-07-28
> 状态：待评审

## 1. 目标

修复当前 Avalonia 端 UI 代码中的 MVVM 违反问题，使 ViewModel 层职责清晰、Service 层回归纯服务、View 层只做绑定。

## 2. 核心问题回顾

| # | 问题 | 根因 |
|---|------|------|
| 1 | `LibraryCatalogService` 是 Service 却承担 ViewModel 职责 | 最核心的架构问题 |
| 2 | 属性转发反模式（所有 ViewModel） | 问题 1 的直接后果 |
| 3 | Code-Behind 包含业务逻辑 | 缺乏 Command 封装 |
| 4 | ViewModel 依赖具体类而非接口 | 缺少接口抽象 |
| 5 | 设计时构造函数创建真实依赖 | 缺少设计时数据 |
| 6 | `App.axaml.cs` 手动装配视图 | DI 使用不完整 |
| 7 | `ShellWindowService` 依赖 View 类型 | 缺少抽象 |
| 8 | 窗口操作用事件而非命令 | 未充分利用 Avalonia 绑定 |
| 9 | 违反单一职责原则 | 缺少拆分 |

## 3. 目标架构

```
┌─────────────────────────────────────────────────────────────┐
│  AXAML (Views)                                              │
│  - 只有绑定，没有事件处理逻辑                                  │
│  - Command、Binding 直接绑定到 ViewModel 属性                  │
│  - 窗口操作通过 WindowChrome 或 ViewModel Command              │
│  - 设计时使用 d:DataContext 指向 Mock ViewModel                │
├─────────────────────────────────────────────────────────────┤
│  ViewModels                                                  │
│  - 持有自己的状态，不转发 Service 属性                          │
│  - 通过接口依赖 Service，不依赖具体类                           │
│  - 设计时构造函数使用 mock 数据                                │
│  - 所有用户操作通过 [RelayCommand] 暴露                        │
├─────────────────────────────────────────────────────────────┤
│  Services (纯服务层)                                          │
│  - 没有 [ObservableProperty]，没有 UI 状态                    │
│  - 方法返回 Task<T>，不修改自身状态                            │
│  - ViewModel 通过接口调用，自行管理返回结果                      │
├─────────────────────────────────────────────────────────────┤
│  Application / Infrastructure (现有，不变)                     │
└─────────────────────────────────────────────────────────────┘
```

## 4. 分阶段修复方案

### Phase 1: 提取 ViewModel 状态 + 抽取接口（最核心）

**目标：** 消除 `LibraryCatalogService` 和 `BackendSessionService` 中的 ViewModel 职责，将它们持有的 UI 状态迁移到 ViewModel 中。

#### 1a. 抽取 `ILibraryCatalogService` 接口

```csharp
// 新建：Application/Services/AssetLibrary/ILibraryCatalogService.cs
// 纯服务接口，没有 [ObservableProperty]，没有 UI 状态
public interface ILibraryCatalogService
{
    Task<IReadOnlyList<LibraryWorkspace>> GetLibrariesAsync(CancellationToken ct = default);
    Task<LibraryWorkspace> AddLibraryAsync(string folderPath, CancellationToken ct = default);
    Task<IReadOnlyList<ManagedAssetRecord>> ScanLibraryAsync(LibraryWorkspace library, CancellationToken ct = default);
    // CRUD 方法
    Task DeleteLibraryAsync(long libraryId, CancellationToken ct = default);
    Task DeleteAssetAsync(long assetId, CancellationToken ct = default);
    Task UpdateAssetTagsAsync(long assetId, string[] tags, CancellationToken ct = default);
    Task UpdateAssetNameAsync(long assetId, string newName, CancellationToken ct = default);
    Task UpdateLibraryAsync(long libraryId, string newName, CancellationToken ct = default);
}
```

`LibraryCatalogService` 改为实现 `ILibraryCatalogService`，**移除所有 `[ObservableProperty]` 和 UI 状态属性**，只保留方法实现。

#### 1b. 抽取 `IBackendSessionService` 接口

```csharp
public interface IBackendSessionService
{
    bool IsBackendReady { get; }
    string BaseUrl { get; }
    Task InitializeAsync();
    // 事件或回调，不持有状态
    event Action? BackendStatusChanged;
}
```

#### 1c. 创建 `LibraryWorkspaceViewModel`（代替 LibraryCatalogService 的 UI 状态）

```csharp
// 新建：ViewModels/LibraryWorkspaceViewModel.cs
public sealed partial class LibraryWorkspaceViewModel : ObservableObject
{
    private ILibraryCatalogService CatalogService { get; }

    [ObservableProperty] public partial LibraryWorkspace? SelectedLibrary { get; set; }
    [ObservableProperty] public partial ManagedAssetRecord? SelectedAsset { get; set; }
    [ObservableProperty] public partial AssetLibraryTreeNode? SelectedAssetTreeNode { get; set; }
    [ObservableProperty] public partial string WorkspaceTitle { get; set; }
    [ObservableProperty] public partial string AssetSummary { get; set; }
    [ObservableProperty] public partial string OperatorNotice { get; set; }
    [ObservableProperty] public partial string SelectedAssetName { get; set; }
    // ... 其他 UI 状态属性

    public ObservableCollection<LibraryWorkspace> Libraries { get; }
    public ObservableCollection<AssetLibraryTreeNode> AssetTreeRoots { get; }
    public ObservableCollection<ManagedAssetRecord> AllAssets { get; }
    public ObservableCollection<AngleDescriptionRecord> SelectedAssetDescriptionAngles { get; }

    // 命令
    [RelayCommand] private async Task AddLibraryAsync(string folderPath) { ... }
    [RelayCommand] private async Task DeleteLibraryAsync() { ... }
    [RelayCommand] private async Task DeleteAssetAsync() { ... }
    // 导航命令
    [RelayCommand] private void NavigateUp() { ... }
    [RelayCommand] private void OpenExplorerItem(AssetLibraryTreeNode? node) { ... }
}
```

#### 1d. 创建 `BackendStatusViewModel`（代替 BackendSessionService 的 UI 状态）

```csharp
// 新建：ViewModels/BackendStatusViewModel.cs
public sealed partial class BackendStatusViewModel : ObservableObject
{
    private IBackendSessionService BackendSession { get; }

    [ObservableProperty] public partial string BackendStatusTitle { get; set; }
    [ObservableProperty] public partial string BackendStatusStage { get; set; }
    [ObservableProperty] public partial string BackendStatusDetail { get; set; }
    [ObservableProperty] public partial string SearchModelStatusTitle { get; set; }
    [ObservableProperty] public partial string SearchModelStatusStage { get; set; }

    public ObservableCollection<AiCapabilityRecord> AiCapabilities { get; }
}
```

#### 1e. 重构后的依赖关系

```
Before:
  LibraryPageViewModel → LibraryCatalogService (ObservableObject, 有 UI 状态)
  ExplorerViewModel → LibraryCatalogService
  AssetDetailViewModel → LibraryCatalogService
  OverviewPageViewModel → LibraryCatalogService, BackendSessionService

After:
  LibraryWorkspaceViewModel → ILibraryCatalogService (纯接口, 无 UI 状态)
  ExplorerViewModel → LibraryWorkspaceViewModel (共享状态)
  AssetDetailViewModel → LibraryWorkspaceViewModel (共享状态)
  OverviewPageViewModel → LibraryWorkspaceViewModel, BackendStatusViewModel
  BackendStatusViewModel → IBackendSessionService (纯接口, 无 UI 状态)
```

### Phase 2: 消除属性转发（全 ViewModel 层）

**目标：** 删除所有 ViewModel 中的转发属性，让 AXAML 直接绑定到持有状态的 ViewModel。

#### 2a. LibraryPageViewModel 重构

```csharp
// 重构后：不再有 50 行转发属性
public sealed partial class LibraryPageViewModel : ObservableObject
{
    // 直接暴露子 ViewModel，AXAML 通过 {Binding Workspace.Title} 访问
    public LibraryWorkspaceViewModel Workspace { get; }
    public AssetDetailViewModel AssetDetail { get; }
    public AssetSearchPanelViewModel SearchPanel { get; }
    public BackendStatusViewModel BackendStatus { get; }
    public ObservableCollection<string> ActivityFeed { get; }
}
```

#### 2b. AXAML 绑定调整

```xml
<!-- Before: 转发路径 -->
<TextBlock Text="{Binding SelectedAssetType}" />

<!-- After: 直接绑定到子 ViewModel -->
<TextBlock Text="{Binding AssetDetail.SelectedAssetType}" />
```

### Phase 3: Code-Behind 逻辑迁移到 Command

**目标：** 将 `LibraryPage.axaml.cs` 中的 12 个事件处理器迁移到 ViewModel Command。

#### 3a. 追加命令到对应的 ViewModel

```csharp
// LibraryWorkspaceViewModel 追加
[RelayCommand]
private async Task AddLibraryFolderAsync()
{
    // 打开文件夹选择器的逻辑留在 View 层（需要 StorageProvider），
    // 但 ViewModel 提供结果处理方法
}

[RelayCommand]
private async Task DeleteNodeAsync(AssetLibraryTreeNode? node) { ... }

[RelayCommand]
private async Task RenameNodeAsync(AssetLibraryTreeNode? node) { ... }
```

#### 3b. 右键菜单改用 Command 绑定

```xml
<!-- Before: 事件 -->
<MenuItem Header="删除" Click="DeleteNode_Click" CommandParameter="{Binding}" />

<!-- After: Command -->
<MenuItem Header="删除"
          Command="{Binding DataContext.Workspace.DeleteNodeCommand,
                    RelativeSource={RelativeSource FindAncestor,
                    AncestorType={x:Type pagevm:LibraryPageViewModel}}}"
          CommandParameter="{Binding}" />
```

#### 3c. 保留在 View 层的代码

只有以下代码可以留在 View 层：
- 系统对话框交互（`OpenFolderPickerAsync`）
- 窗口管理（`WindowState`、`Hide`/`Show`）
- 输入焦点控制（`FocusSearchBox`）

### Phase 4: 修复 DI 和接口抽象

**目标：** 让 ViewModel 只依赖接口，不依赖具体类。

#### 4a. 注册接口

```csharp
// AvaloniaModule.cs
builder.RegisterType<LibraryCatalogService>()
    .As<ILibraryCatalogService>()
    .SingleInstance();

builder.RegisterType<BackendSessionService>()
    .As<IBackendSessionService>()
    .SingleInstance();
```

#### 4b. ViewModel 改为接口依赖

```csharp
// Before
public LibraryWorkspaceViewModel(ILibraryCatalogService catalogService) { ... }

// After
public LibraryWorkspaceViewModel(ILibraryCatalogService catalogService) { ... }
```

### Phase 5: 修复设计时构造函数

**目标：** 设计时构造函数使用假数据，不创建真实服务。

```csharp
// 设计时专用 ViewModel（或用 d:DataContext 指向 mock）
public sealed class DesignTimeLibraryWorkspaceViewModel : LibraryWorkspaceViewModel
{
    public DesignTimeLibraryWorkspaceViewModel()
        : base(new NullLibraryCatalogService())  // 空实现
    {
        Libraries.Add(new LibraryWorkspace(1, "示例素材库", @"D:\素材", "示例", "已登记", 42));
        SelectedLibrary = Libraries[0];
        WorkspaceTitle = "示例素材库";
    }
}
```

### Phase 6: 修复 App.axaml.cs

**目标：** 让 Application 只负责注册容器，不负责创建视图。

```csharp
// 方案：使用 ViewModel-First + 自动匹配
// 通过 DI 容器解析 ViewModel，通过命名约定匹配 View
public override async void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        BuildContainer();

        var mainWindow = Container!.Resolve<MainWindow>();  // MainWindow 从 DI 获取
        desktop.MainWindow = mainWindow;
        mainWindow.Show();
    }
}
```

### Phase 7: 修复 ShellWindowService

**目标：** 不依赖具体 View 类型。

```csharp
public interface IShellWindowService
{
    void ShowMainWindow();
    void ShowQuickSearchWindow();
    void ToggleQuickSearchWindow();
    // 不暴露 View 类型
}
```

实现类接收 `Window` 基类：

```csharp
public sealed class ShellWindowService : IShellWindowService
{
    private Window? MainWindow { get; set; }
    private Window? QuickSearchWindow { get; set; }

    public void AttachMainWindow(Window window) { MainWindow = window; }
    public void AttachQuickSearchWindow(Window window) { QuickSearchWindow = window; }
}
```

## 5. 实施顺序

| 阶段 | 内容 | 风险 | 工作量 |
|------|------|------|--------|
| **Phase 1a** | 抽取 `ILibraryCatalogService` 接口 | 低 | 小 |
| **Phase 1b** | 抽取 `IBackendSessionService` 接口 | 低 | 小 |
| **Phase 1c** | 创建 `LibraryWorkspaceViewModel`，迁移 UI 状态 | 高 | 大 |
| **Phase 1d** | 创建 `BackendStatusViewModel`，迁移 UI 状态 | 中 | 中 |
| **Phase 1e** | 调整 DI 注册，替换依赖引用 | 中 | 中 |
| **Phase 2** | 消除属性转发，调整 AXAML 绑定 | 中 | 中 |
| **Phase 3** | Code-Behind 逻辑迁移到 Command | 中 | 中 |
| **Phase 4** | 修复 DI 接口注册 | 低 | 小 |
| **Phase 5** | 设计时数据 | 低 | 小 |
| **Phase 6** | 修复 App.axaml.cs | 中 | 小 |
| **Phase 7** | 修复 ShellWindowService | 低 | 小 |

## 6. 不变的原则

- **不修改 Application 层代码**：`IAssetLibraryService`、`AssetLibraryService`、`IAssetDescriptionStore` 等保持不变
- **不修改数据库 Schema**：所有 CRUD 操作路径不变
- **不修改 Python 后端**：API 路由和模型调用不变
- **不修改现有测试**：新增 ViewModel 测试，不改现有 Application 测试
- **每个 Phase 可独立测试**：每个 Phase 完成后，构建和测试通过才能继续

## 7. 风险与注意事项

1. **Phase 1c 风险最高**：`LibraryCatalogService` 被 5 个 ViewModel 引用，需要同时调整所有引用点
2. **AXAML 绑定路径变更**：Phase 2 需要同步修改所有 AXAML 文件中的绑定路径
3. **设计时兼容性**：Phase 5 之前，设计器可能无法正常工作
4. **逐步迁移**：建议每完成一个 Phase 就提交一次，每个 Phase 都可以独立构建通过