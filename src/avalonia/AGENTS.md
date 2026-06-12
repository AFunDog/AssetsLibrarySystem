# AGENTS.md — AssetsLibrarySystem (Avalonia Desktop)

## 项目定位

Avalonia 桌面端 — 素材管理系统的本地 UI 界面。本质上是 Python 后端模型网关的桌面操作面板，涵盖扫描、描述、向量搜索、库管理全流程。

## 代码布局

```
AssetsLibrarySystem.Avalonia/    # 桌面 UI 入口
  ├── Program.cs                 # 启动入口
  ├── App.axaml(.cs)             # DI 容器 (Autofac) + 主题 + 托盘
  ├── ViewModels/                # MVVM ViewModel 层
  ├── Views/                     # AXAML 视图
  │   └── Pages/                 # 四个标签页
  └── Services/                  # UI 专属服务（Catalog/Session/Shell/Hotkey/Settings/Activity）

AssetsLibrarySystem.Application/ # 领域层（与 Avalonia 解耦，可被 Console/Test 复用）
  ├── Services/                  # 核心业务服务
  │   ├── AssetLibrary/          # 资产库扫描/注册/去重
  │   ├── AssetDescription/      # 描述生成 + 向量化存储
  │   ├── AssetSearch/           # HNSW 近似搜索 + 精排
  │   ├── BackendLauncher/       # Python 子进程管理
  │   ├── BackgroundTasks/       # 异步任务追踪
  │   └── Infrastructure/        # SQLite + 写入队列
  ├── UseCases/AssetOperations/  # 编排用例（描述/向量化/删除/重建索引）
  └── Models/                    # 领域模型 + 记录/DTO

AssetsLibrarySystem.Console/     # 命令行运行器（复用 Application 层）
AssetsLibrarySystem.Application.Tests/  # xUnit 单元测试
```

## 关键技术选型

- **UI**: Avalonia 11.x, FluentTheme Dark, compiled bindings (`AvaloniaUseCompiledBindingsByDefault=true`)
- **MVVM**: CommunityToolkit.Mvvm 8.4.1 源码生成器 (`[ObservableProperty]`, `[RelayCommand]`)
- **DI**: Autofac（所有注册在 `App.BuildContainer()` 中手动完成）
- **日志**: Serilog（appsettings.json 配置）
- **持久化**: SQLite + Channel 串行写入队列
- **搜索**: HNSW.Net 本地近似搜索 + HTTP 远程精排
- **IPC**: HTTP JSON 调用 Python 后端（localhost:8000）

## 架构规则

1. **分层方向**: ViewModel → UseCase → Service → Infrastructure，不可反向依赖
2. **Application 层不可引用 Avalonia 程序集** — 保持被 Console/Test 复用的能力
3. **ViewModel 不包含业务逻辑** — 仅做状态转发和 UI 命令编排，业务逻辑在 UseCase/Service 中
4. **SQLite 写操作必须通过 `DatabaseWriteQueue`** — 避免并发写入冲突
5. **Python 后端是无状态模型网关** — 文件扫描、元数据管理全在 .NET 侧完成

## ViewModel 设计约定

- 每个 ViewModel 有双重构造：参数化（运行时 DI）+ 无参（设计器标记 `[Obsolete("仅供设计器使用。")]`）
- 设计时数据通过 AXAML 的 `<Design.DataContext>` 提供，不在运行时路径注入假数据
- 服务状态通过属性转发暴露（如 `BackendStatusTitle => BackendSessionService.BackendStatusTitle`），并订阅 `PropertyChanged` 事件同步

## 导航方案

- **主窗口**: `TabControl` 四标签页（Overview / Library / DescriptionTasks / Settings）
- **快捷搜索**: 独立 `QuickSearchWindow`，`Ctrl+Shift+Space` 全局热键切换
- **窗口管理**: `ShellWindowService` 接管显示/隐藏/托盘逻辑，`ShutdownMode.OnExplicitShutdown`

## 搜索架构

```
用户查询 → 本地 HNSW/exact 召回候选集 → HTTP POST 远程精排 → 混合评分(0.35*cosine + 0.65*rerank)
```

- 记录数 ≤5000 时用 exact cosine 暴力搜索，否则用 HNSW
- HNSW 索引持久化到文件，带指纹校验
- 描述分"角度"存储（全面/风格/情感/乐器等），向量化时按角度分别建立索引

## 当前阶段

功能基本成型，处于迭代优化阶段。添加新功能时沿 `Model → Service/UseCase → ViewModel → View` 方向扩展。
