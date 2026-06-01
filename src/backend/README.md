# Assets Library System Backend

这是 `Assets Library System` 的 Python 模型网关。

当前后端只承担一类职责：

- 暴露大模型 HTTP 接口
- 提供健康检查与能力清单
- 作为 Avalonia/.NET 桌面端的模型调用出口
- 提供文本向量化和候选集重排序能力

当前后端不再承担：

- 素材库登记
- 文件扫描
- 素材元数据管理
- 本地目录浏览

## 当前接口

- `GET /health`
- `GET /api/v1/model/capabilities`
- `POST /api/v1/model/generate`
- `POST /api/v1/search/index`
- `POST /api/v1/search/query`
- `POST /api/v1/search/explore`
- `POST /api/v1/search/reindex`

`POST /api/v1/model/generate` 现在接收的是素材打标请求，而不是对话消息流。请求体的核心字段是：

- `asset_format`：`文本`、`图片`、`视频`、`音频` 之一，用来选择对应的 `prompts.yaml` 配置
- `asset_path`：素材文件的绝对路径
- `prompt`：可选，覆盖默认提示词
- `system_prompt`：可选，覆盖默认系统提示词
- `mock_response`：可选，强制返回占位结果

如果 `prompt` 和 `system_prompt` 都不传，后端会根据 `asset_format` 自动读取 `configs/prompts.yaml` 中对应的系统提示词和默认提示词。当前默认提示词配置为空，后端会把素材格式和绝对路径一并带入实际请求上下文。

接口返回会同时带上 token 用量统计 `token_usage`，其字段与百炼官方 `usage` 保持一致，核心包括 `input_tokens`、`output_tokens`、`total_tokens`，并尽量透传 `input_tokens_details`、`output_tokens_details`、`prompt_tokens_details` 等细分信息。如果是 mock 模式或底层响应未提供 usage，则该字段为空。

`configs/providers.yaml` 也已经按素材类型分组，分别为 `文本`、`图片`、`视频`、`音频` 配置独立模型。后端会优先读取与 `asset_format` 同名的槽位，再按兼容顺序回退到 `llm_gateway`、`asset_describer` 或第一个可用槽位。

`POST /api/v1/search/index` 只负责把传入文本转换成向量，不会写入数据库。调用方拿到向量后，可以自己写入 Avalonia 侧管理的 SQLite。向量化与重排序默认使用本地小模型：

- embedding：`Qwen/Qwen3-Embedding-0.6B`
- rerank：`Qwen/Qwen3-Reranker-0.6B`

如果需要改模型名或 HuggingFace 缓存目录，可以分别设置：

- `ALS_SEARCH_EMBED_MODEL`
- `ALS_SEARCH_RERANK_MODEL`
- `ALS_SEARCH_CACHE_DIR`

`POST /api/v1/search/query` 只对调用方传入的候选文本做本地 rerank，不负责数据库读取或写入。

`POST /api/v1/search/reindex` 会直接读取 Avalonia 已写入的 `asset_descriptions.db` 中的 `asset_description_vectors` 表，重新构建本地 HNSW 索引文件，不再维护第二份向量 SQLite。

`api_key` 建议统一放在 `providers.yaml` 顶层，这样四个素材类型槽位都能继承同一把 Key；如果顶层没配，后端再回退到对应槽位自己的 `api_key`，最后才读取环境变量 `DASHSCOPE_API_KEY`。

当前 DashScope 传参方式如下：

- `文本`：后端读取 `asset_path` 指向的文本文件内容，通过 `Generation.call()` 发送给大模型
- `图片`：后端将 `asset_path` 转成 `file://` 形式，通过 `MultiModalConversation.call()` 的 `image` 项发送
- `视频`：后端将 `asset_path` 转成 `file://` 形式，通过 `MultiModalConversation.call()` 的 `video` 项发送，并默认附带 `fps=2`
- `音频`：后端将 `asset_path` 转成 `file://` 形式，通过 `MultiModalConversation.call()` 的 `audio` 项发送；如果当前配置模型不是音频兼容模型，会自动回退到 `qwen3-omni-30b-a3b-captioner`

## 本地启动

```powershell
cd src/backend
copy configs\providers.example.yaml configs\providers.yaml
pip install -e .
uvicorn app.main:app --reload
```

如果未配置真实 API Key，`/api/v1/model/generate` 会返回占位响应，便于先和桌面端联调。

## 单素材控制台测试入口

安装后可以直接运行：

```powershell
assets-library-system-tag --asset-format 图片 --asset-path D:\Data\sample.png --mock-response
```

或者在源码目录下直接用模块方式运行：

```powershell
python -m app.cli --asset-format 图片 --asset-path D:\Data\sample.png --mock-response
```

可选参数：

- `--prompt`：覆盖默认提示词
- `--system-prompt`：覆盖默认系统提示词
- `--json`：输出完整 JSON 响应，便于脚本化检查
