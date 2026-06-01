# Assets Library System

一个面向文本、图片、视频、音乐素材的管理系统骨架项目。

当前阶段已经把职责重新收敛为两部分：

- `Avalonia/.NET` 负责素材管理主流程
- `Python FastAPI` 只负责大模型 HTTP 服务

也就是说，素材库、目录、元数据和桌面工作流由本地桌面端承担；Python 后端不再包含素材管理功能。
当前桌面端会把素材描述结果集中保存到本地 SQLite，避免把描述文件分散写到每个素材目录下。

## 目标

- 管理多种素材类型：文本、图片、视频、音乐
- 参考 `D:\GitRepository\RenderTest\test2.py` 的思路，后续支持素材打标、向量检索、RAG 与自然语言搜索
- 提供明确分层的前后端结构，便于后续逐步实现

## 当前结构

```text
docs/
  architecture.md          # 方案说明与演进路线
src/
  avalonia/
    AssetsLibrarySystem.Avalonia/ # 桌面端主入口，承担素材管理工作台
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
  - 后续检索与索引编排入口
- Python FastAPI
  - 模型网关健康检查
  - 模型能力清单
  - 文本提示词转发
  - 后续统一扩展多模型 HTTP 调用

检索链路后续仍参考 `RenderTest/test2.py` 中的两阶段方案：

- Embedding 建索引与召回
- Reranker 精排
- 索引持久化与热加载

## 后续建议

1. 先在 .NET 侧补齐真实素材库扫描、本地仓储和任务状态持久化
2. 再让桌面端通过 Python 网关接入真实模型调用
3. 最后补齐召回、精排、索引持久化与 RAG 相关能力

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
