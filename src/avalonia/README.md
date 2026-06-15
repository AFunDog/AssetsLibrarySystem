# AssetsLibrarySystem — Avalonia Desktop

素材管理系统的本地桌面客户端。管理文本、图片、视频、音乐四类素材，支持 AI 描述生成和语义搜索。

## 项目结构

```
src/avalonia/
├── AssetsLibrarySystem.Avalonia/       # 桌面 UI（入口）
├── AssetsLibrarySystem.Application/    # 领域层（可复用）
├── AssetsLibrarySystem.Console/        # 命令行工具
└── AssetsLibrarySystem.Application.Tests/  # 单元测试
```

## 功能概览

| 功能 | 说明 |
|---|---|
| **素材库管理** | 目录扫描、SHA256 去重、UID 侧写文件、SQLite 持久化 |
| **描述生成** | 调用 Python 后端 AI 模型，生成多角度结构化描述 |
| **语义搜索** | 本地 HNSW 近似召回 + 远程精排 + 混合评分 |
| **搜索索引** | 向量持久化 + HNSW 图文件持久化 + 指纹校验 |
| **后端管理** | Python 子进程生命周期管理、健康检查、心跳 |
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
| `BackendLauncher.Host` | `127.0.0.1` | Python 后端地址 |
| `BackendLauncher.Port` | `8000` | Python 后端端口 |
| `BackendLauncher.StartupTimeoutSeconds` | `30` | 后端启动超时 |
| `Runtime.DataRoot` | `""` | 数据根目录（空=自动检测） |

## 相关文档

- [项目整体说明](../../README.md)
- [未来计划](../../docs/roadmap.md)
- [后端 Python 服务](../backend/README.md)
