# AGENTS

## Repo Goal

本仓库用于搭建一个素材管理系统，覆盖文本、图片、视频、音乐四类素材，并预留打标、RAG、自然语言搜索的扩展位。

## Current Stage

当前阶段是架构搭建期：

- 先保证前后端入口清晰
- 先保证目录分层明确
- 先写文档，不提前堆真实业务实现

不要在没有明确需求时擅自补充数据库、向量库、消息队列或复杂部署脚本。

## Code Layout

- `src/backend`：Python FastAPI 后端骨架
- `src/frontend`：Vue 3 + TypeScript 前端骨架
- `scripts/`：一次性数据库迁移等 Python 脚本
- `docs/architecture.md`：系统方案与演进规划

## Implementation Rules

- 优先保持分层清晰，再考虑功能完整
- 当前允许使用 `TODO`、占位实现、假数据说明，但要明确边界
- 一次性数据库迁移等 Python 脚本统一放在 `scripts/` 下
- 搜索/RAG 相关设计需要对齐 `D:\GitRepository\RenderTest\test2.py` 的“召回 + 精排 + 索引持久化”思路
- 后续新增真实能力时，优先沿着 `domain -> application -> infrastructure -> api` 的方向扩展

## Editing Notes

- 尽量保持中文注释和中文文档
- 不要把未来复杂实现直接塞进入口文件
- 如果文档和代码不一致，以代码中的当前结构为准，并同步更新文档
