# Avalonia 三项目代码质量与重构计划

本文记录对 `src/avalonia` 下三个项目的代码组织、冗余逻辑、无效代码和性能瓶颈检查结果：

- `AssetsLibrarySystem.Application`
- `AssetsLibrarySystem.Avalonia`
- `AssetsLibrarySystem.Console`

目标是让后续开发更快、更规范，并减少功能迭代时的重复修改成本。

## 总体结论

当前功能已经能跑通，但分层边界正在变模糊。最核心的问题不是单个方法写法，而是：

- `Application` 项目名义上是应用层，但命名空间、模型和依赖仍明显带有 Avalonia 桌面端痕迹。
- SQLite schema、连接、迁移和读写逻辑分散在多个服务中。
- 描述任务、向量化、索引重建等用例逻辑在 Avalonia 页、Console 命令和服务之间重复。
- 素材扫描已优化 hash，但仍存在大量 SQLite 小查询和小写入。
- 部分 ViewModel 的无参构造仍会 `new` 真实服务，设计时和运行时依赖容易分叉。

## P0：优先处理

### 1. 收紧 Application 项目边界

优先级：P0  
复杂度：中等  
影响范围：`AssetsLibrarySystem.Application`
状态：已完成

问题：

- 已修复：`AssetsLibrarySystem.Application.csproj` 的 `RootNamespace` 已改为 `AssetsLibrarySystem.Application`。
- 已修复：Application 项目内的模型、服务、基础设施命名空间已收敛为 `AssetsLibrarySystem.Application.*`。
- 已修复：Avalonia 与 Console 现在通过 `AssetsLibrarySystem.Application.*` 引用共享服务和模型。
- 已修复：Application 项目已移除 `CommunityToolkit.Mvvm`、`Autofac` 包引用。
- 已修复：原 `ServiceBootstrapper` 已收敛为 `ApplicationConfigurationFactory`，只负责配置创建，不再持有容器注册。
- 已修复：Application 内需要通知变更的模型改为基于 `INotifyPropertyChanged` 的轻量 `ObservableModel`，不再依赖 MVVM Toolkit。

风险：

- 命名空间边界已收紧，Console 不再被迫引用 Avalonia 服务命名空间。
- Console 与 Avalonia 的容器依赖已分别留在宿主项目，不再从 Application 传递。
- 仍需继续区分应用模型和 UI 展示模型，但这部分已转入后续“用例拆分”和“LibraryCatalogService 拆分”处理，不再阻塞 P0-1。

建议：

- 已完成：将 Application 项目根命名空间改为 `AssetsLibrarySystem.Application`。
- 已完成：将 Application 服务、模型、基础设施命名空间迁移为 `AssetsLibrarySystem.Application.*`。
- 已保留：`AssetLibraryTreeNode` 作为 Avalonia UI 投影模型继续放在 Avalonia 项目。
- 已完成：移除 Application 对 `ObservableObject` / `[ObservableProperty]` 的依赖。
- 已完成：将容器注册下沉到 Avalonia / Console 宿主。
- 继续将模型进一步分为应用模型和 UI 展示模型。
- Application 层只保留用例、接口、DTO、纯模型和基础设施抽象。

参考文件：

- `src/avalonia/AssetsLibrarySystem.Application/AssetsLibrarySystem.Application.csproj`
- `src/avalonia/AssetsLibrarySystem.Application/Models/DesktopRecords.cs`
- `src/avalonia/AssetsLibrarySystem.Application/Infrastructure/ApplicationConfigurationFactory.cs`
- `src/avalonia/AssetsLibrarySystem.Application/Models/ObservableModel.cs`

### 2. 收敛 SQLite 访问层

优先级：P0  
复杂度：中等  
影响范围：Application 服务、Console、Avalonia
状态：已完成

问题：

- 已修复：`AssetLibraryService`、`AssetDescriptionStore`、`AssetDescriptionVectorStore` 不再各自创建数据库路径和 schema。
- 已修复：新增 `IAssetDatabase` / `SqliteAssetDatabase`，集中负责数据库路径、连接打开、PRAGMA、schema 初始化和迁移字段。
- 已修复：`asset_metadata`、`asset_descriptions`、`asset_description_vectors`、`assets` schema 已集中在统一数据库服务中。
- 已修复：读路径不再通过写队列触发 Store 私有 `EnsureSchemaAsync`，统一由 `SqliteAssetDatabase` 的一次性 schema 初始化保护。

风险：

- schema 变更入口已集中，后续迁移漏改风险降低。
- SQLite 小查询、小写入和扫描快照优化仍属于 P0-3，不在本项内完成。

建议：

- 已完成：新建 `SqliteAssetDatabase`，统一负责数据库路径、SQLite 连接工厂、WAL / busy_timeout / foreign_keys 等 PRAGMA、schema 初始化和迁移字段。
- 已完成：Store 不再直接建表，只做仓储查询和保存。
- 已完成：`asset_metadata`、`asset_descriptions`、`asset_description_vectors`、`assets` schema 由统一数据库服务管理。
- 后续如 schema 继续复杂化，可将 `SqliteAssetDatabase` 内的 schema SQL 再拆成专门 migrator。

参考文件：

- `src/avalonia/AssetsLibrarySystem.Application/Services/AssetLibrary/AssetLibraryService.cs`
- `src/avalonia/AssetsLibrarySystem.Application/Services/AssetDescription/AssetDescriptionStore.cs`
- `src/avalonia/AssetsLibrarySystem.Application/Services/AssetDescription/AssetDescriptionVectorStore.cs`
- `src/avalonia/AssetsLibrarySystem.Application/Services/Infrastructure/IAssetDatabase.cs`
- `src/avalonia/AssetsLibrarySystem.Application/Services/Infrastructure/SqliteAssetDatabase.cs`
- `src/avalonia/AssetsLibrarySystem.Application/Services/Infrastructure/DatabaseWriteQueue.cs`

### 3. 优化素材扫描的数据库访问策略

优先级：P0  
复杂度：中等到高  
影响范围：素材库扫描性能

问题：

- 当前扫描已经避免了不必要的 hash，但每个文件仍可能执行多次 SQLite 查询。
- `Parallel.ForEachAsync` 并行处理文件时，每个文件都可能打开 SQLite 连接。
- 每个素材变更都可能进入写队列，导致大量小写入。
- 扫描期间还要查询描述是否存在，用于 UI 展示“已描述/未描述”状态。

风险：

- 素材量大时，性能瓶颈会从硬盘读文件转移到 SQLite 查询、连接创建和写队列排队。
- 并行度越高，不一定越快，可能导致磁盘和 SQLite 锁竞争。

建议：

- 扫描开始前批量读取数据库快照：
  - `assets` by `asset_uid`
  - `assets` by `content_hash`
  - `asset_metadata` tags
  - `asset_descriptions` 描述状态
- 扫描时只做内存匹配和文件系统 metadata 读取。
- 扫描结束后生成变更集，批量提交数据库写入。
- 并行度改为可配置，例如 `AssetScan:MaxDegreeOfParallelism`。
- 将“发现文件”和“导入/更新身份”拆成两个阶段。

参考文件：

- `src/avalonia/AssetsLibrarySystem.Application/Services/AssetLibrary/AssetLibraryService.cs`

### 4. 抽取描述、向量化、索引用例

优先级：P0  
复杂度：中等  
影响范围：Avalonia、Console、Application

问题：

- `LibraryPageViewModel` 有右键加入描述任务逻辑。
- `DescriptionTasksPageViewModel` 有批量描述、单项描述、向量化、索引重建逻辑。
- `ConsoleCommandRunner` 也有描述、目录描述、向量化、搜索、重建索引逻辑。

风险：

- 同一个能力在 UI 和 Console 中语义漂移。
- 后续增加“跳过已描述”“失败重试”“批量并发限制”“取消任务”等策略时，需要多处同步修改。

建议：

- 在 Application 层新增共享用例：
  - `DescribeAssetsUseCase`
  - `VectorizeDescriptionsUseCase`
  - `RebuildSearchIndexUseCase`
  - `RevealAssetLocationService` 或桌面端专用 `IFileRevealService`
- Avalonia ViewModel 只负责选择范围、调用用例、展示状态。
- Console 只负责解析参数、调用同一套用例、打印结果。

参考文件：

- `src/avalonia/AssetsLibrarySystem.Avalonia/ViewModels/LibraryPageViewModel.cs`
- `src/avalonia/AssetsLibrarySystem.Avalonia/ViewModels/DescriptionTasksPageViewModel.cs`
- `src/avalonia/AssetsLibrarySystem.Console/ConsoleCommandRunner.cs`

## P1：建议尽快处理

### 5. 清理 ViewModel 无参构造中的真实服务实例

优先级：P1  
复杂度：低  
影响范围：Avalonia ViewModel

问题：

- 多个 ViewModel 的无参构造会 `new BackendSessionService()`、`new LibraryCatalogService()`、`new BackgroundTaskService()`。
- 这些构造本意是设计时使用，但它们并不完全是纯设计时假数据。

风险：

- 设计时和运行时依赖关系分叉。
- 测试或预览时可能启动不必要的后台状态对象。
- 以后排查“为什么服务被创建了两份”会更困难。

建议：

- 无参构造只保留给 XAML 设计器，且只构造轻量 fake 数据。
- 或者改为在 `Design.DataContext` 中声明设计时对象，不在运行时代码路径中创建真实服务。

参考文件：

- `src/avalonia/AssetsLibrarySystem.Avalonia/ViewModels/MainWindowViewModel.cs`
- `src/avalonia/AssetsLibrarySystem.Avalonia/ViewModels/LibraryPageViewModel.cs`
- `src/avalonia/AssetsLibrarySystem.Avalonia/ViewModels/DescriptionTasksPageViewModel.cs`
- `src/avalonia/AssetsLibrarySystem.Avalonia/ViewModels/SettingsPageViewModel.cs`

### 6. 移除 Store 中的静态 fallback 写队列

优先级：P1  
复杂度：低  
影响范围：Application Store
状态：已完成

问题：

- 已修复：`AssetDescriptionStore` 和 `AssetDescriptionVectorStore` 的静态 `FallbackWriteQueue` 已移除。
- 已修复：两个 Store 不再提供无参构造，运行时必须显式注入 `IDatabaseWriteQueue` 和 `IAssetDatabase`。

风险：

- Store 内部不再产生隐藏后台 worker。
- 设计时构造改为在 ViewModel 中显式提供设计时写队列和数据库服务，后续仍建议继续改为 XAML `Design.DataContext`。

建议：

- 已完成：删除无参构造和静态 fallback。
- 已完成：设计时路径明确注入依赖。

参考文件：

- `src/avalonia/AssetsLibrarySystem.Application/Services/AssetDescription/AssetDescriptionStore.cs`
- `src/avalonia/AssetsLibrarySystem.Application/Services/AssetDescription/AssetDescriptionVectorStore.cs`

### 7. 统一后端会话与模型运行时管理

优先级：P1  
复杂度：中等  
影响范围：后端启动、快速搜索、设置页

问题：

- `BackendSessionService` 管启动、状态、预热、关闭模型。
- `QuickSearchViewModel` 又直接依赖 `IBackendLauncher`，搜索前自行启动后端。

风险：

- 后端启动和模型预热策略可能在不同入口不一致。
- 快速搜索窗口绕过 `BackendSessionService` 的状态更新。

建议：

- 将后端启动、模型状态、预热、关闭统一收敛到 `BackendSessionService` 或 `ModelRuntimeService`。
- `QuickSearchViewModel` 不直接依赖 `IBackendLauncher`，而是调用统一会话服务。

参考文件：

- `src/avalonia/AssetsLibrarySystem.Avalonia/Services/Backend/BackendSessionService.cs`
- `src/avalonia/AssetsLibrarySystem.Avalonia/ViewModels/QuickSearchViewModel.cs`

### 8. 继续拆分 LibraryCatalogService 的职责

优先级：P1  
复杂度：中等  
影响范围：素材库页面状态

问题：

- 虽然已经拆成 partial 文件，但 `LibraryCatalogService` 仍然同时负责：
  - 加载素材库
  - 扫描素材
  - 构建图标树
  - 选择状态
  - 描述详情读取
  - 描述任务状态回写
  - 活动日志

风险：

- partial 只是拆文件，不是真正拆职责。
- 后续添加筛选、排序、分页、缩略图、右键动作时会继续膨胀。

建议：

- 拆成：
  - `LibraryLoadService`
  - `AssetExplorerProjectionService`
  - `AssetSelectionState`
  - `DescriptionSelectionService`
  - `LibraryActivityReporter`

参考文件：

- `src/avalonia/AssetsLibrarySystem.Avalonia/Services/Library/LibraryCatalogService.cs`
- `src/avalonia/AssetsLibrarySystem.Avalonia/Services/Library/LibraryCatalogService.Loading.cs`
- `src/avalonia/AssetsLibrarySystem.Avalonia/Services/Library/LibraryCatalogService.Tree.cs`
- `src/avalonia/AssetsLibrarySystem.Avalonia/Services/Library/LibraryCatalogService.Description.cs`

## P2：后续清理

### 9. 拆分 ConsoleCommandRunner

优先级：P2  
复杂度：低到中等  
影响范围：Console 项目

问题：

- `ConsoleCommandRunner` 同时负责参数解析、命令分发、业务调用、输出格式。

建议：

- 拆为：
  - `LibraryCommands`
  - `AssetCommands`
  - `SearchCommands`
  - `ConsoleOutputFormatter`
- 如果命令继续增加，可以考虑轻量命令路由表。

参考文件：

- `src/avalonia/AssetsLibrarySystem.Console/ConsoleCommandRunner.cs`

### 10. 拆分组合根注册

优先级：P2  
复杂度：低  
影响范围：Application、Avalonia、Console

问题：

- 已完成：`ServiceBootstrapper` 不再位于 Application 项目，Application 只保留配置创建逻辑。
- 当前 Avalonia 和 Console 分别注册共享应用服务，边界清晰，但注册代码存在重复。

建议：

- 如后续要减少重复，可在宿主层新增共享注册扩展，例如 `RegisterApplicationServices`，但不要放回 Application 项目。
- Avalonia 项目注册 UI 服务和 ViewModel。
- Console 项目注册命令类和控制台输出服务。

参考文件：

- `src/avalonia/AssetsLibrarySystem.Application/Infrastructure/ApplicationConfigurationFactory.cs`
- `src/avalonia/AssetsLibrarySystem.Avalonia/App.axaml.cs`
- `src/avalonia/AssetsLibrarySystem.Console/Program.cs`

## 性能优化重点

### 当前主要瓶颈

1. 扫描时每个文件都可能打开 SQLite 连接并做多次查询。
2. 写队列虽然串行化写入，但大量小写入仍会造成排队。
3. schema 初始化已集中，但首次数据库访问仍会做一次 schema 检查和迁移。
4. 大素材库构建树时每次状态变化都可能重建整棵树。

### 推荐优化方向

1. 启动阶段可主动调用 `IAssetDatabase.EnsureSchemaAsync`，避免首次页面操作承担 schema 初始化成本。
2. 扫描阶段使用数据库快照和内存索引。
3. 扫描结束后批量写入变更集。
4. 图标树投影可以做增量刷新，至少避免每个素材状态变化都全量重建。
5. 大批量描述任务应有并发限制和取消能力。

## 推荐实施顺序

1. `P0-1`：先调整 Application 命名空间和模型边界。已完成。
2. `P0-2`：建立统一数据库访问层和 migrator。已完成。
3. `P0-3`：用数据库快照重写扫描读写路径。
4. `P0-4`：抽取描述、向量化、索引重建共享用例。
5. `P1-5`：清理 ViewModel 无参构造。
6. `P1-7`：统一后端会话和快速搜索后端启动路径。
7. `P1-8`：继续拆分 `LibraryCatalogService`。
8. `P2-9`：拆分 Console 命令。

## 构建验证记录

检查时执行过：

```powershell
dotnet build src/avalonia/AssetsLibrarySystem.sln
```

当前结果：构建通过，0 个警告，0 个错误。
