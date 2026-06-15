# Assets Library System Backend

这是 `Assets Library System` 的 Python 模型网关。

当前后端只承担一类职责：

- 暴露大模型 HTTP 接口
- 提供健康检查与能力清单
- 作为 Avalonia/.NET 桌面端的模型调用出口
- 提供文本向量化和候选集重排序能力
- 在多模态打标前对图片/视频做轻量预处理压缩

当前后端不再承担：

- 素材库登记
- 文件扫描
- 素材元数据管理
- 本地目录浏览

## 配置方式

后端使用 `.env` 和环境变量加载配置，推荐本地开发时放一份 `src/backend/.env`，并用 `.env.example` 作为模板。

常用字段：

- `APP_ENV`：`dev` 或 `prod`
- `DATA_ROOT`：共享 `data` 目录，留空时按环境推导默认值
- `DATABASE_URL`：保留给后续数据库接入
- `LOG_LEVEL`：日志级别
- `HOST`：服务监听地址
- `PORT`：服务监听端口

规则：

1. 环境变量优先于 `.env`
2. 如果 `DATA_ROOT` 为空
   - `APP_ENV=dev` 时，默认使用仓库根目录下的 `data/`
   - 其他环境下，如果是打包后的可执行文件，则默认使用程序目录下的 `data/`
   - 否则回退到后端源码目录下的 `data/`

桌面端启动后端时，会把 `APP_ENV` 和 `DATA_ROOT` 传给子进程，保证 Avalonia 和 Python 后端看到的是同一个数据目录。

## 当前接口

- `GET /health`
- `GET /api/v1/model/capabilities`
- `POST /api/v1/model/generate`
- `POST /api/v1/search/index`
- `POST /api/v1/search/query`
- `GET /api/v1/search/models/status`
- `POST /api/v1/search/models/close`

`POST /api/v1/model/generate` 现在接收的是素材打标请求，而不是对话消息流。请求体的核心字段是：

- `asset_format`：`文本`、`图片`、`视频`、`音频` 之一，用来选择对应的 `prompts.yaml` 配置
- `asset_path`：素材文件的绝对路径
- `prompt`：可选，覆盖默认提示词
- `system_prompt`：可选，覆盖默认系统提示词
- `mock_response`：可选，强制返回占位结果

如果 `prompt` 和 `system_prompt` 都不传，后端会根据 `asset_format` 自动读取 `configs/prompts.yaml` 中对应的系统提示词和默认提示词。当前默认提示词配置为空，后端会把素材格式和绝对路径一并带入实际请求上下文。

接口返回会同时带上 token 用量统计 `token_usage`，其字段与百炼官方 `usage` 保持一致，核心包括 `input_tokens`、`output_tokens`、`total_tokens`，并尽量透传 `input_tokens_details`、`output_tokens_details`、`prompt_tokens_details` 等细分信息。如果是 mock 模式或底层响应未提供 usage，则该字段为空。

`configs/providers.yaml` 也已经按素材类型分组，分别为 `文本`、`图片`、`视频`、`音频` 配置独立模型。后端会优先读取与 `asset_format` 同名的槽位，再按兼容顺序回退到 `llm_gateway`、`asset_describer` 或第一个可用槽位。

`POST /api/v1/search/index` 只负责把传入文本转换成向量，不会写入数据库。请求需携带 `provider` 与 `model`，可选择 `local` 或 `dashscope`。桌面端默认使用：

- embedding：`dashscope / text-embedding-v4`
- rerank：`dashscope / qwen3-rerank`

如果需要改模型名或 HuggingFace 缓存目录，可以分别设置：

- `ALS_SEARCH_EMBED_MODEL`
- `ALS_SEARCH_RERANK_MODEL`
- `ALS_SEARCH_CACHE_DIR`

如果没有显式设置 `ALS_SEARCH_CACHE_DIR`，后端会默认使用 `DATA_ROOT/huggingface` 作为本地搜索模型缓存目录；只有在 `DATA_ROOT` 也不可用时，才会退回 `sentence-transformers` / `huggingface_hub` 自己的默认缓存规则。

多模态素材预处理默认开启，临时文件会写到 `DATA_ROOT/temp/`（或 `ALS_MEDIA_TEMP_DIR` 指定目录）：

- 图片：优先使用 Pillow 进行缩放和有损/无损压缩
- 视频：如果系统存在 `ffmpeg`，会压到较小分辨率和码率后再送给模型
- 音频：直接使用原始文件，不做压缩或转码
- 如果当前环境缺少所需依赖或压缩失败，图片/视频会自动回退到原始文件，不阻断打标
- 图片/视频预处理生成的临时文件会在模型调用结束后清理，调用失败时也会清理

相关可选配置：

- `ALS_ENABLE_MEDIA_PREPROCESS`
- `ALS_MEDIA_TEMP_DIR`
- `ALS_IMAGE_MAX_SIDE`
- `ALS_IMAGE_JPEG_QUALITY`
- `ALS_VIDEO_CRF`
- `ALS_VIDEO_AUDIO_BITRATE`

`POST /api/v1/search/query` 只对调用方传入的候选文本做 rerank，不负责数据库读取或写入。`provider` 与 `model` 同样由调用方显式指定。

当前推荐架构下，`asset_descriptions.db`、HNSW 索引文件和向量召回都由 Avalonia/C# 本地维护；Python 后端保持为纯模型网关，只负责 embedding、rerank 和模型状态控制。

`POST /api/v1/search/models/close` 用来主动释放本地搜索模型缓存，当前可选值为 `embedding` 和 `rerank`。这个接口只影响进程内已经加载的 `SentenceTransformer` / `CrossEncoder` 对象，适合在长时间空闲后手动腾出显存；如果对应模型还没被加载，接口也会正常返回，只是 `closed=false`。

`GET /api/v1/search/models/status` 用来查看当前进程里哪些本地搜索模型已经驻留。返回里会包含 `embedding_loaded`、`rerank_loaded`、`loaded_model_kinds` 和对应模型名，便于前端直接展示缓存状态。

`api_key` 建议统一放在 `providers.yaml` 顶层，这样四个素材类型槽位都能继承同一把 Key；如果顶层没配，后端再回退到对应槽位自己的 `api_key`，最后才读取 `DASHSCOPE_API_KEY`。

当前 DashScope 传参方式如下：

- `文本`：后端读取 `asset_path` 指向的文本文件内容，通过 `Generation.call()` 发送给大模型
- `图片`：后端优先使用预处理后的临时文件路径，并转成 `file://` 形式，通过 `MultiModalConversation.call()` 的 `image` 项发送
- `视频`：后端优先使用预处理后的临时文件路径，并转成 `file://` 形式，通过 `MultiModalConversation.call()` 的 `video` 项发送，并默认附带 `fps=2`
- `音频`：后端直接使用原始音频文件路径，并转成 `file://` 形式，通过 `MultiModalConversation.call()` 的 `audio` 项发送；如果当前配置模型不是音频兼容模型，会自动回退到 `qwen3-omni-30b-a3b-captioner`
- 四类素材的描述请求都会显式携带 `response_format={"type":"json_object"}`，按阿里云百炼的结构化输出方式要求模型返回 JSON 字符串；结构化描述的解析、存储和多角度向量化由 .NET Application 层负责

## 本地启动

```powershell
cd src/backend
copy .env.example .env
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
