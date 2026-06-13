# CLAUDE

## Project Context

这是一个素材管理系统项目，当前只要求建立基础架构与入口，不要求实现完整功能。

## What To Optimize For

- 目录结构清楚
- 前后端边界清楚
- 文档先行
- 为后续素材打标、RAG、自然语言搜索预留稳定扩展点

## Technical Direction

- 后端：`FastAPI`
- 前端：`Vue 3 + TypeScript + Vite`
- 检索链路设计参考：`D:\GitRepository\RenderTest\test2.py`

## Guardrails

- 当前阶段不要实现真实模型推理、数据库落库、文件上传、向量索引构建
- 可以写占位 API、占位服务、占位页面
- 一次性数据库迁移等 Python 脚本统一放在 `scripts/` 下
- 复杂逻辑放进独立模块，不要堆在 `main.py` 或 `App.vue`
- 如果新增文档，优先写到 `docs/`
