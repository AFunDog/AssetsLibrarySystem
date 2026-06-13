# Assets Library System

一个面向文本、图片、视频、音乐素材的管理系统骨架项目。

当前阶段已经把职责重新收敛为四部分：

- `Avalonia/.NET` 负责图形界面工作台
- `Console/.NET` 负责无界面命令行操作
- `Application/.NET` 负责共享服务、模型和本地存储
- `Python FastAPI` 负责大模型 HTTP 服务，以及素材检索所需的向量化、召回和重排接口

也就是说，素材库、目录、元数据和工作流由本地 .NET 侧承担；Python 后端不再包含素材管理功能，但会提供检索与索引重建能力。
当前桌面端会把素材描述结果集中保存到本地 SQLite，并通过后端检索接口完成向量召回和重排序。
数据库内部使用数值 `libraries.id` 和 `assets.id` 建立外键关系。`asset_uid` 仅保留在 `assets` 表中，用于兼容素材文件旁的同名 `.uid` 文件；路径只作为 `current_path` 保存。
桌面端现在同时支持托盘常驻模式，主窗口可以隐藏到托盘，快捷键 `Ctrl+Shift+Space` 可弹出极简快速检索窗口。
桌面端启动后会主动预热向量模型和重排序模型，减少第一次检索等待。

## 目标

- 管理多种素材类型：文本、图片、视频、音乐
- 参考 `D:\GitRepository\RenderTest\test2.py` 的思路，支持素材打标、向量检索、RAG 与自然语言搜索
- 提供明确分层的前后端结构，便于后续逐步实现

## 当前结构

```text
docs/
  architecture.md          # 方案说明与演进路线
scripts/                   # 一次性数据库迁移等 Python 脚本
src/
  avalonia/
    AssetsLibrarySystem.Application/ # 共享服务、模型、本地存储与后端启动器
    AssetsLibrarySystem.Avalonia/ # 桌面端主入口，承担素材管理工作台
    AssetsLibrarySystem.Console/   # 命令行入口，支持库管理、扫描、描述
  backend/
    app/                   # Python 模型网关
      api/                 # HTTP 路由层
      application/         # 模型调用服务
      core/                # provider / prompt 配置
      schemas/             # HTTP 输入输出模型
      main.py              # FastAPI 入口
    pyproject.toml
    configs/
      providers.example.yaml # 私有 provider 模板，实际 providers.yaml 不进仓库
      prompts.yaml
  frontend/
    ...                    # 早期 Web 骨架，当前不是素材管理主入口
AGENTS.md
CLAUDE.md
README.md
```

## 当前边界

- Avalonia/.NET
  - 素材库登记
  - 本地目录管理
  - `.uid` 身份文件维护
  - 素材元数据维护
  - 工作台状态展示
  - 自然语言检索入口
  - 索引重建入口
- Python FastAPI
  - 模型网关健康检查
  - 模型能力清单
  - 文本提示词转发
  - 素材向量检索与重排
  - 素材索引重建
  - 后续统一扩展多模型 HTTP 调用

## 现有检索入口

- Avalonia 页面已经提供自然语言检索和“重建向量索引”按钮
- 托盘模式下可用 `Ctrl+Shift+Space` 打开快速检索弹窗，回车后直接查看最相关结果
- 后端额外暴露向量模型和重排序模型预热接口，桌面端启动时会提前调用
- 命令行入口支持：
  - `assets search <query>`
  - `assets reindex`

## 后续建议

1. 继续在 .NET 侧补齐围绕 `asset_uid` 的版本处理、冲突处理和任务状态持久化
2. 继续增强桌面端对 Python 网关的调用与结果展示
3. 后续补齐更完整的 RAG、上下文拼装与批量索引维护能力

## 本地启动方式

当前主要有两个入口。

Python 模型网关：

```powershell
cd src/backend
copy configs\providers.example.yaml configs\providers.yaml
pip install -e .
uvicorn app.main:app --reload
```

后端测试：

```powershell
cd src/backend
pytest
```

Avalonia 桌面端：

```powershell
cd src/avalonia
dotnet build AssetsLibrarySystem.sln
```

## 一次性数据库迁移

升级到数据库素材库、数值外键与多 embedding 模型向量结构时，先关闭桌面端和控制台，再执行：

```powershell
python scripts/migrate_to_surrogate_ids_and_multi_model_vectors.py --dry-run
python scripts/migrate_to_surrogate_ids_and_multi_model_vectors.py
```

脚本会把旧 `libraries.json` 中的素材库信息迁入数据库，并在修改前创建带 UTC 时间戳的 `.bak` 备份。可通过 `--db <path>` 指定数据库文件；旧 JSON 不在数据库同目录时，可通过 `--libraries-json <path>` 指定。
