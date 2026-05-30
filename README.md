# Assets Library System

一个面向文本、图片、视频、音乐素材的管理系统骨架项目。

当前阶段只完成前后端基础入口、分层目录与架构文档，不实现真实素材管理、打标、RAG 或自然语言搜索功能。

## 目标

- 管理多种素材类型：文本、图片、视频、音乐
- 参考 `D:\GitRepository\RenderTest\test2.py` 的思路，后续支持素材打标、向量检索、RAG 与自然语言搜索
- 提供明确分层的前后端结构，便于后续逐步实现

## 当前结构

```text
docs/
  architecture.md          # 方案说明与演进路线
src/
  backend/
    app/
      api/                 # HTTP 路由层
      application/         # 用例与编排层
      core/                # 配置与基础设施抽象
      domain/              # 领域模型
      infrastructure/      # 仓储、索引、外部能力适配
      schemas/             # API 输入输出模型
      main.py              # FastAPI 入口
    pyproject.toml         # 后端项目配置与依赖声明
  frontend/
    src/
      components/          # 页面组件
      App.vue              # 应用壳层
      main.ts              # Vue 入口
      styles.css           # 全局样式
    index.html
    package.json
    tsconfig.json
    vite.config.ts
AGENTS.md
CLAUDE.md
README.md
```

## 规划中的能力映射

- 素材管理：统一素材实体、文件元数据、标签、描述、状态
- 打标：文件解析、内容提取、多模态标注、人工修订
- 检索：关键词检索、向量召回、重排序
- RAG：基于素材描述、标签、摘要和扩展知识构造回答上下文
- 自然语言搜索：把用户查询转成过滤条件、召回请求和回答结果

其中检索链路将参考 `RenderTest/test2.py` 中的两阶段方案：

- Embedding 建索引与召回
- Reranker 精排
- 索引持久化与热加载

## 后续建议

1. 先实现素材元数据落库与文件扫描
2. 再接入打标与向量索引构建流程
3. 最后补齐搜索、RAG 与前端交互闭环

## 本地启动方式

当前仅为骨架，命令主要用于后续开发阶段约定。

后端：

```powershell
cd src/backend
pip install -e .
uvicorn app.main:app --reload
```

前端：

```powershell
cd src/frontend
npm install
npm run dev
```
