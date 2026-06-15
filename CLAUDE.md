# CLAUDE

## Project Context

这是一个本地素材管理与语义检索系统，已经具备素材扫描、SQLite 持久化、模型描述、向量召回和重排能力。

## What To Optimize For

- 目录结构清楚
- 前后端边界清楚
- 保持实现与 README、智能体文档一致
- 为后续素材打标、RAG、自然语言搜索预留稳定扩展点

## Technical Direction

- 后端：`FastAPI`
- 桌面端：`.NET 10 + Avalonia`
- 模型网关：`Python FastAPI`
- Web：`Vue 3 + TypeScript + Vite` 早期骨架，当前不是主入口
- 检索链路设计参考：`D:\GitRepository\RenderTest\test2.py`

## Guardrails

- 不在没有明确需求时引入独立向量库、消息队列或复杂部署
- 一次性数据库迁移等 Python 脚本统一放在 `scripts/` 下
- 复杂逻辑放进独立模块，不要堆在 `main.py` 或 `App.vue`
- 项目现状和使用说明写入对应 `README.md`
- 智能体约束写入对应 `AGENTS.md` 或 `CLAUDE.md`
- `docs/` 只记录尚未实现或仍需完善的未来计划
