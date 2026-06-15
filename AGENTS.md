# AGENTS

## Repo Goal

本仓库用于搭建一个素材管理系统，覆盖文本、图片、视频、音乐四类素材，并预留打标、RAG、自然语言搜索的扩展位。

## Current Stage

当前已经具备素材扫描、SQLite 持久化、描述生成、向量化、召回和重排闭环，处于迭代优化阶段。

不要在没有明确需求时擅自补充独立向量库、消息队列或复杂部署脚本。

## Code Layout

- `src/avalonia`：Avalonia 桌面端、共享 Application 层、Console 与测试
- `src/backend`：Python FastAPI 模型网关
- `scripts/`：一次性数据库迁移等 Python 脚本
- `docs/roadmap.md`：只记录尚未实现或仍需完善的未来计划

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
- 项目现状、结构和使用说明写入对应 `README.md`；`docs/` 不重复记录当前实现
