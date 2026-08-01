# AssetsLibrarySystem — Avalonia Desktop

素材快速查询系统的本地桌面客户端。管理文本、图片、视频、音乐四类素材，支持 AI 描述生成和语义搜索，并直接返回符合描述的素材信息。

## 项目结构

```
src/avalonia/
├── AssetsLibrarySystem.Avalonia/       # 桌面 UI（入口）
├── AssetsLibrarySystem.Application/    # 领域层（可复用）
├── AssetsLibrarySystem.Console/        # 命令行工具
└── AssetsLibrarySystem.Application.Tests/  # 单元测试
```

`AssetLibraryService` 负责扫描编排、身份匹配和 SQLite 持久化，纯文件系统职责拆分为：

- `AssetFileScanner`：支持格式判断与目录枚举
- `AssetUidSidecarStore`：UID 生成及 `.uid` 侧写读写
- `AssetContentHasher`：SHA-256 内容指纹与扫描期缓存
- `AssetRecordFormatter`：标签与展示摘要生成

用户设置采用 500ms 防抖保存，并通过同目录临时文件原子替换；应用退出释放容器时会刷新最后一次待保存修改。

### 工作台 ViewModel 结构

- `LibraryWorkspaceViewModel`：素材库树、浏览区、选中详情与描述展示（选中时从 SQLite 加载描述）
- `AssetDescriptionPanelViewModel` / `AssetVectorizationPanelViewModel`：描述与向量化任务
- `LibraryPageViewModel`：组合上述子 VM，工具栏与右键菜单统一转发到面板实现
- 结构化描述主角度为 **「整体」**，读取时兼容历史 **「全面」**
- 内容 hash 变化：描述标 `stale`、删除向量；向量化会跳过过期描述
- 后台任务状态通过 `IBackgroundTaskUiScheduler` 调度到 UI 线程，避免跨线程改集合
- 浏览区支持图标 / 列表 / 详情三视图；`ViewMode` 写入 `user-settings.json`
- 树节点缩略图支持属性变更通知；详情侧可编辑主描述并保存
- 本地 HNSW 按 embedding 模型进程内缓存，指纹用 float 二进制哈希

## 功能概览

| 功能 | 说明 |
|---|---|
| **素材库管理** | 目录扫描、SHA256 去重、UID 侧写文件、SQLite 持久化 |
| **描述生成** | 调用 Python 后端 AI 模型，生成多角度结构化描述 |
| **语义搜索** | 本地 HNSW 近似召回 + 远程精排 + 混合评分 |
| **搜索索引** | 向量持久化 + HNSW 图文件持久化 + 指纹校验 |
| **模型引擎** | 默认嵌入 Python.NET 初始化（`in-process`）；就绪前不放行描述/检索 |
| **快捷搜索** | 全局热键 `Ctrl+Shift+Space`，快速搜索任意素材 |
| **系统托盘** | 最小化到托盘，后台常驻 |

## 技术栈

- **.NET 10** + **Avalonia 11**（FluentTheme Dark）
- **CommunityToolkit.Mvvm** 源码生成器
- **Autofac** 依赖注入
- **Serilog** 日志
- **SQLite** 本地存储
- **HNSW.Net** 近似最近邻搜索
- **Python FastAPI** 后端模型网关

## 快速开始

```bash
# 构建
dotnet build

# 运行桌面端
dotnet run --project AssetsLibrarySystem.Avalonia

# 命令行帮助
dotnet run --project AssetsLibrarySystem.Console -- --help
```

## 系统要求

- .NET 10 SDK
- Python 3.11+（用于后端模型服务）
- Windows 10+（全局热键依赖 Win32 API）

## 配置

`appsettings.json` 中主要配置项：

| 配置 | 默认值 | 说明 |
|---|---|---|
| `Runtime.DataRoot` | `""` | 数据根目录（空=自动检测） |

## 相关文档

- [项目整体说明](../../README.md)
- [未来计划](../../docs/roadmap.md)
- [后端 Python 服务](../backend/README.md)
