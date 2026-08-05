# Assets Library System Backend

这是 `Assets Library System` 的 Python 模型网关，通过 **Python.NET 嵌入桌面端进程** 调用，不提供独立 HTTP 服务（FastAPI/uvicorn 已移除）。

当前后端承担：

- 文本与多模态素材的描述生成（DashScope / OpenAI 兼容路径）
- 文本向量化和候选集重排序
- 视频场景分割（PySceneDetect）与片段起始帧提取（ffmpeg）
- 在多模态打标前对图片/视频做轻量预处理压缩

当前后端不再承担：

- 素材库登记
- 文件扫描
- 素材元数据管理
- 本地目录浏览
- 任何 HTTP 接口（健康检查、能力清单等已随 FastAPI 层一并移除）

## 调用方式

桌面端（Avalonia）启动时通过 `PythonEngineService` 嵌入 Python 运行时（`python312.dll` + `src/backend` 源码目录），C# 侧用 Python.NET 直接调用本包中的服务：

- `app.application.services.model_service.ModelService`：描述生成（`generate_text`）
- `app.application.services.search_service.SearchService`：向量化与重排
- `app.application.services.video_frame_extractor.extract_frame`：片段帧提取

对应的 C# 包装服务位于 `src/avalonia/AssetsLibrarySystem.Application/Services/Python/`（`PythonModelService`、`PythonSearchService`、`VideoFrameService`）。

## 配置方式

后端使用 `.env` 和环境变量加载配置（`app/core/config.py`），推荐本地开发时放一份 `src/backend/.env`，并用 `.env.example` 作为模板。

常用字段：

- `APP_ENV`：`dev` 或 `prod`
- `DATA_ROOT`：共享 `data` 目录，留空时按环境推导默认值
- `DASHSCOPE_API_KEY`：模型 API Key
- `ALS_MEDIA_TEMP_DIR`：多模态预处理临时目录（默认 `DATA_ROOT/temp/`）
- `ALS_ENABLE_MEDIA_PREPROCESS`：是否启用图片/视频预处理压缩
- `ALS_IMAGE_MAX_SIDE` / `ALS_IMAGE_JPEG_QUALITY`：图片压缩参数
- `ALS_VIDEO_CRF` / `ALS_VIDEO_AUDIO_BITRATE`：视频压缩参数

规则：

1. 环境变量优先于 `.env`
2. 如果 `DATA_ROOT` 为空
   - `APP_ENV=dev` 时，默认使用仓库根目录下的 `data/`
   - 其他环境下，如果是打包后的可执行文件，则默认使用程序目录下的 `data/`
   - 否则回退到后端源码目录下的 `data/`

## 与桌面端的关系

- 桌面端默认 **嵌入进程内** 调用本包中的模型与检索服务（Python.NET），不启动独立进程、无 HTTP 端口。
- 结构化描述主角度为 **「整体」**，解析时兼容历史键 **「全面」**。
- 视频切片会透传 `min_scene_len` / `adaptive_threshold`；时长未知时不切片；LLM 失败会向上抛错而非空描述。
- OpenAI 兼容路径的视频抽帧有硬上限（默认 32 帧）；DashScope 响应会检查 `status_code`。

## 模型调用约定

`ModelService.generate_text` 接收的是素材打标请求，而不是对话消息流。请求体的核心字段是：

- `asset_format`：`文本`、`图片`、`视频`、`音频` 之一，用来选择对应的 `prompts.yaml` 配置
- `asset_path`：素材文件的绝对路径
- `prompt`：可选，覆盖默认提示词
- `system_prompt`：可选，覆盖默认系统提示词
- `mock_response`：可选，强制返回占位结果

如果 `prompt` 和 `system_prompt` 都不传，后端会根据 `asset_format` 自动读取 `configs/prompts.yaml` 中对应的系统提示词和默认提示词。当前默认提示词配置为空，后端会把素材格式和绝对路径一并带入实际请求上下文。

接口返回会同时带上 token 用量统计 `token_usage`，其字段与百炼官方 `usage` 保持一致，核心包括 `input_tokens`、`output_tokens`、`total_tokens`，并尽量透传 `input_tokens_details`、`output_tokens_details`、`prompt_tokens_details` 等细分信息。如果是 mock 模式或底层响应未提供 usage，则该字段为空。

`configs/providers.yaml` 也已经按素材类型分组，分别为 `文本`、`图片`、`视频`、`音频` 配置独立模型。后端会优先读取与 `asset_format` 同名的槽位，再按兼容顺序回退到 `llm_gateway`、`asset_describer` 或第一个可用槽位。

向量化（embedding）与重排（rerank）以 **DashScope 云端** 为主。桌面端默认使用：

- embedding：`dashscope / text-embedding-v4`
- rerank：`dashscope / qwen3-rerank`

多模态素材预处理默认开启，临时文件会写到 `DATA_ROOT/temp/`（或 `ALS_MEDIA_TEMP_DIR` 指定目录）：

- 图片：优先使用 Pillow 进行缩放和有损/无损压缩
- 视频：如果系统存在 `ffmpeg`，会压到较小分辨率和码率后再送给模型
- 音频：直接使用原始文件，不做压缩或转码
- 如果当前环境缺少所需依赖或压缩失败，图片/视频会自动回退到原始文件，不阻断打标
- 图片/视频预处理生成的临时文件会在模型调用结束后清理，调用失败时也会清理

当前推荐架构下，`asset_descriptions.db`、HNSW 索引文件和向量召回都由 Avalonia/C# 本地维护；Python 保持为纯模型网关，只负责 embedding 与 rerank。

`api_key` 建议统一放在 `providers.yaml` 顶层，这样四个素材类型槽位都能继承同一把 Key；实际回退顺序为：**槽位自己的 `api_key` 优先**，槽位未配置时回退到顶层 `api_key`，最后才读取 `DASHSCOPE_API_KEY`（`app/core/provider_config.py`）。注意 `providers.yaml` 已被 gitignore，真实 Key 推荐通过环境变量 `DASHSCOPE_API_KEY` 或本目录 `.env`（gitignore）注入，避免明文入库。

当前 DashScope 传参方式如下：

- `文本`：后端读取 `asset_path` 指向的文本文件内容，通过 `Generation.call()` 发送给大模型
- `图片`：后端优先使用预处理后的临时文件路径，并转成 `file://` 形式，通过 `MultiModalConversation.call()` 的 `image` 项发送
- `视频`：后端优先使用预处理后的临时文件路径，并转成 `file://` 形式，通过 `MultiModalConversation.call()` 的 `video` 项发送，并默认附带 `fps=5`
- `音频`：后端直接使用原始音频文件路径，并转成 `file://` 形式，通过 `MultiModalConversation.call()` 的 `audio` 项发送；如果当前配置模型不是音频兼容模型，会自动回退到 `qwen3-omni-30b-a3b-captioner`
- 四类素材的描述请求都会显式携带 `response_format={"type":"json_object"}`，按阿里云百炼的结构化输出方式要求模型返回 JSON 字符串；结构化描述的解析、存储和多角度向量化由 .NET Application 层负责

## 本地验证

```powershell
cd src/backend
copy .env.example .env
copy configs\providers.example.yaml configs\providers.yaml
pip install -e .
pytest
```

如果未配置真实 API Key，模型生成会返回占位响应（mock 模式），便于先和桌面端联调。
