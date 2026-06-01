# Assets Library System

一个面向文本、图片、视频、音乐素材的管理系统骨架项目。

当前阶段已经把职责重新收敛为四部分：

- `Avalonia/.NET` 负责图形界面工作台
- `Console/.NET` 负责无界面命令行操作
- `Application/.NET` 负责共享服务、模型和本地存储
- `Python FastAPI` 负责大模型 HTTP 服务，以及素材检索所需的向量化、召回和重排接口

也就是说，素材库、目录、元数据和工作流由本地 .NET 侧承担；Python 后端不再包含素材管理功能，但会提供检索与索引重建能力。
当前桌面端会把素材描述结果集中保存到本地 SQLite，并通过后端检索接口完成向量召回和重排序。

## 目标

- 管理多种素材类型：文本、图片、视频、音乐
- 参考 `D:\GitRepository\RenderTest\test2.py` 的思路，支持素材打标、向量检索、RAG 与自然语言搜索
- 提供明确分层的前后端结构，便于后续逐步实现

## 当前结构

```text
docs/
  architecture.md          # 方案说明与演进路线
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
- 命令行入口支持：
  - `assets search <query>`
  - `assets reindex`

## 后续建议

1. 先在 .NET 侧继续补齐真实素材库扫描、本地仓储和任务状态持久化
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
