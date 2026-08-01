# Assets Library System

一个面向文本、图片、视频、音乐素材的本地素材管理与快速查询系统。

本软件的核心目的，是让用户通过自然语言快速找到符合描述的素材。系统只需要返回匹配素材的名称、类型、路径、描述、标签和相关度等信息，不承担知识问答或内容生成职责。

当前阶段已经把职责重新收敛为四部分：

- `Avalonia/.NET` 负责图形界面工作台
- `Console/.NET` 负责无界面命令行操作
- `Application/.NET` 负责共享服务、模型和本地存储
- `Python` 通过 Python.NET 嵌入桌面端进程内，负责模型生成、向量化和重排

也就是说，素材库、目录、元数据、向量召回、索引持久化和工作流由本地 .NET 侧承担；Python 侧不包含素材管理功能，只提供模型生成、向量化和重排序能力。

当前桌面端以 **进程内嵌入 Python.NET** 调用模型网关逻辑（`BaseUrl = in-process`），不再提供独立 HTTP 服务（FastAPI/uvicorn 已移除）。
桌面端会把素材描述和向量集中保存到本地 SQLite，通过本地 exact/HNSW 完成召回，再调用 embedding/rerank。
数据库内部使用数值 `libraries.id` 和 `assets.id` 建立外键关系。`asset_uid` 仅保留在 `assets` 表中，用于兼容素材文件旁的同名 `.uid` 文件；路径只作为 `current_path` 保存。
结构化描述的主角度键为 **「整体」**（兼容历史数据中的「全面」）。
扫描发现文件内容 hash 变化时，会将旧描述标为 `stale`、删除旧向量并要求重新打标。
桌面端现在同时支持托盘常驻模式，主窗口可以隐藏到托盘，快捷键 `Ctrl+Shift+Space` 可弹出极简快速检索窗口。
桌面端启动后会初始化嵌入 Python 引擎；引擎就绪前不会放行描述/向量化/检索。

## 目标

- 管理多种素材类型：文本、图片、视频、音乐
- 参考 `D:\GitRepository\RenderTest\test2.py` 的“召回 + 精排 + 索引持久化”思路，支持素材打标、向量检索与自然语言搜索
- 快速返回符合查询描述的素材信息，不实现 RAG、问答生成或检索上下文拼装
- 提供明确分层的前后端结构，便于后续逐步实现

## 当前结构

```text
docs/
  roadmap.md               # 只记录未来计划
scripts/                   # 一次性数据库迁移等 Python 脚本
src/
  avalonia/
    AssetsLibrarySystem.Application/ # 共享服务、模型、本地存储与后端启动器
    AssetsLibrarySystem.Avalonia/ # 桌面端主入口，承担素材管理工作台
    AssetsLibrarySystem.Console/   # 命令行入口，支持库管理、扫描、描述
  backend/
    app/                   # Python 模型网关（嵌入式，无 HTTP 层）
      application/         # 模型调用服务
      core/                # provider / prompt 配置
      schemas/             # 进程内调用使用的输入输出模型
    pyproject.toml
    configs/
      providers.example.yaml # 私有 provider 模板，实际 providers.yaml 不进仓库
      prompts.yaml
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
- Python 模型网关（嵌入式）
  - 文本与多模态素材描述生成
  - 文本向量化与候选集重排
  - 视频场景检测与片段帧提取
  - 后续统一扩展多模型调用

## 现有检索入口

- Avalonia 库页：描述当前范围 / 描述当前素材 / 批量向量化、素材树右键描述与向量化
- Avalonia 快速检索：托盘模式下 `Ctrl+Shift+Space` 打开弹窗，回车后查看最相关结果
- 本地 HNSW：按 embedding 模型进程内缓存；库中无向量时清空索引文件并成功返回
- 命令行入口支持：
  - `assets search <query>`
  - `assets reindex`

## 文档约定

- 项目整体说明写在本文件。
- Avalonia、Application 与 Console 说明写在 `src/avalonia/README.md`。
- Python 模型网关说明写在 `src/backend/README.md`。
- 智能体开发约束写在各级 `AGENTS.md`、`CLAUDE.md`。
- `docs/` 只保留尚未落地的未来计划，见 `docs/roadmap.md`。

## 本地启动方式

当前主要有两个入口。

Python 模型网关：由桌面端启动时自动嵌入（Python.NET），无需单独启动；若需手动验证可执行：

```powershell
cd src/backend
copy configs\providers.example.yaml configs\providers.yaml
pip install -e .
pytest
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
