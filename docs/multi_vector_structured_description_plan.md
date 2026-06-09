# 结构化描述与多向量检索方案

## 1. 目标

当前系统对每个素材只保存一段 `description`，并只生成一个向量。这样实现简单，但有两个明显问题：

- 单段描述很难同时覆盖多个检索角度
- 某个角度的信息被弱化后，召回容易漏掉

本方案的目标是把素材描述改成结构化 JSON，并按多个语义片段分别向量化，再在检索阶段按权重聚合，从而提升召回全面性和排序稳定性。

典型例子：

- 音频：整体概括、乐器、风格、情绪
- 图片：整体概括、主体、场景、构图、风格、情绪
- 视频：整体概括、主体、动作、镜头、场景、节奏、情绪
- 文本：整体概括、主题、人物、场景、语气

## 2. 当前实现的真实限制

按当前代码，系统的向量链路是：

1. Avalonia 生成一条素材描述，存入 `asset_descriptions.description`
2. `AssetTextVectorizationService` 把这段完整文本发给后端 `/api/v1/search/index`
3. 后端只对一个 `description` 字段编码一次
4. `asset_description_vectors` 一行只对应一个素材
5. HNSW 索引也是“一素材一向量”
6. 搜索时只按这一条向量召回，再做 rerank

这意味着：

- 现在的表结构不支持一个素材多个向量
- 现在的索引标签也不支持一个素材多个检索片段
- 现在的聚合逻辑只接受单一相似度，不接受“分片段加权”

所以要做多向量，必须一起调整：

- 描述生成格式
- 本地存储结构
- 向量持久化结构
- 索引输入粒度
- 查询聚合算法

## 3. 设计原则

- 保持 `domain -> application -> infrastructure -> api` 分层清晰
- 不把复杂聚合逻辑塞进入口文件
- 描述结构优先面向检索，不追求一次性覆盖所有展示需求
- 一个素材允许多个片段向量，但仍保持一个素材作为最终结果单位
- 先支持音频，再把同一机制扩展到图片、视频、文本

## 4. 推荐总体设计

推荐采用“一个素材，多个结构化描述片段，多条向量记录，查询时按片段权重聚合”的方案。

### 4.1 描述层

模型输出不再是单条纯文本，而是结构化 JSON。

以音频为例：

```json
{
  "comprehensive": {
    "text": "一段以钢琴和弦乐为主的舒缓配乐，整体温暖而略带伤感。",
    "tags": ["舒缓配乐", "钢琴弦乐", "温暖伤感"]
  },
  "instruments": {
    "text": "主要由钢琴、弦乐和大提琴构成，伴随柔和的铺底音色。",
    "tags": ["钢琴", "弦乐", "大提琴", "柔和铺底"]
  },
  "style": {
    "text": "整体节奏平稳，旋律性较强，偏影视配乐和抒情氛围音乐。",
    "tags": ["舒缓", "配乐", "节奏平稳", "旋律性强", "抒情"]
  },
  "emotion": {
    "text": "情绪温暖、安静，带有轻微伤感和怀旧感。",
    "tags": ["温暖", "安静", "伤感", "怀旧", "治愈"]
  }
}
```

### 4.2 向量层

每个 JSON 字段拆成一个可检索片段，例如：

- `comprehensive`
- `instruments`
- `style`
- `emotion`

每个片段生成一条向量记录。

### 4.3 检索层

查询时仍然只编码一次用户查询文本，但会与多个片段向量进行匹配，然后按片段类型权重聚合为“素材级分数”。

## 5. 数据结构方案

## 5.1 asset_descriptions 保留为“素材主描述表”

不建议把 `asset_descriptions` 直接删掉，而是把它升级为“素材描述主记录”。

建议字段：

- `asset_id`
- `asset_name`
- `asset_type`
- `asset_path`
- `description_text`
  - 用于展示的主描述，可默认取 `comprehensive.text`
- `description_json`
  - 结构化 JSON 原文
- `description_schema`
  - 例如 `audio_v1`
- `backend_endpoint`
- `mode`
- `generated_at`
- `token_usage_json`
- `prompt`
- `system_prompt`
- `content_hash`
- `metadata_status`

说明：

- `description_text` 保留给 UI 展示、日志和兼容路径
- `description_json` 才是新的权威描述内容

## 5.2 新增素材描述片段表

建议新增一张独立表，例如 `asset_description_segments`。

建议字段：

- `segment_id TEXT PRIMARY KEY`
- `asset_id TEXT NOT NULL`
- `asset_type TEXT NOT NULL`
- `segment_key TEXT NOT NULL`
  - 例如 `comprehensive`、`instruments`、`style`、`emotion`
- `segment_order INTEGER NOT NULL`
- `text TEXT NOT NULL`
- `tags_json TEXT NOT NULL DEFAULT '[]'`
- `weight REAL NOT NULL`
- `schema_version TEXT NOT NULL`
- `generated_at TEXT NOT NULL`
- `content_hash TEXT NULL`

推荐唯一约束：

- `UNIQUE(asset_id, segment_key)`

说明：

- 这张表存结构化拆分后的片段文本和标签
- `weight` 可以直接落表，避免查询时硬编码

## 5.3 新增素材片段向量表

建议新增 `asset_description_segment_vectors`，不要继续把一对多关系塞回现在的 `asset_description_vectors`。

建议字段：

- `segment_id TEXT PRIMARY KEY`
- `asset_id TEXT NOT NULL`
- `segment_key TEXT NOT NULL`
- `embedding_model TEXT NOT NULL`
- `vector_dim INTEGER NOT NULL`
- `vector_blob BLOB NOT NULL`
- `vectorized_at TEXT NOT NULL`
- `content_hash TEXT NULL`

推荐索引：

- `INDEX ix_segment_vectors_asset_id ON asset_description_segment_vectors(asset_id)`
- `INDEX ix_segment_vectors_segment_key ON asset_description_segment_vectors(segment_key)`

说明：

- `segment_id` 作为向量记录主键最自然
- `asset_id` 方便按素材聚合

## 6. 描述生成方案

## 6.1 Prompt 侧

每种素材类型都可以有自己的结构化 schema。

第一阶段建议只上线音频：

- `comprehensive`
- `instruments`
- `style`
- `emotion`

第二阶段再扩展其他素材类型。

## 6.2 后端模型返回

当前 `/api/v1/model/generate` 返回的是：

- `output_text`

建议升级为兼容双模式：

- `output_text`
- `output_json`
- `output_mode`
  - `plain_text`
  - `structured_json`

兼容策略：

- 老 prompt 仍然只走 `output_text`
- 新结构化 prompt 走 `output_json`
- Avalonia 端按 `output_mode` 决定写入哪条链路

## 6.3 解析与校验

结构化输出必须在后端做最小校验：

- 是否是合法 JSON
- 是否包含要求字段
- 每个字段是否同时包含 `text` 和 `tags`
- `tags` 是否为字符串数组

如果校验失败：

- 整次描述记为失败，返回明确错误
- 不要悄悄回退成脏文本

## 7. 向量化方案

## 7.1 向量化输入粒度

对每个结构化片段分别向量化。

例如音频会得到 4 条输入：

- `comprehensive.text`
- `instruments.text`
- `style.text`
- `emotion.text`

是否把 `tags` 一起拼进去，有两种方案：

### 方案 A：只向量化 text

优点：

- 文本更干净
- 避免标签堆叠污染语义

缺点：

- 某些短标签的召回能力偏弱

### 方案 B：向量化 `text + tags`

格式例如：

```text
[style]
整体节奏平稳，旋律性较强，偏影视配乐和抒情氛围音乐。
标签：舒缓，配乐，节奏平稳，旋律性强，抒情
```

优点：

- 标签可直接参与召回

缺点：

- 如果标签质量一般，容易引入噪声

建议第一版采用方案 B，但要加字段名前缀，避免不同片段之间语义混淆。

## 7.2 建议的片段编码文本

建议统一编码格式：

```text
[segment=comprehensive]
一段以钢琴和弦乐为主的舒缓配乐，整体温暖而略带伤感。
标签：舒缓配乐，钢琴弦乐，温暖伤感
```

这样有两个好处：

- 同一个词在不同片段下的语义会更稳定
- 后续 rerank 或调试时更容易追踪来源

## 8. 检索与加权聚合方案

## 8.1 可以做到，但不建议“先把多个向量平均成一个向量”

你提到“按权重进行加权平均”，这里要区分两种做法：

### 做法 1：离线把多个片段向量加权平均成一个素材向量

优点：

- 实现简单
- 可以继续沿用现有“一素材一向量”的查询逻辑

缺点：

- 丢失片段级信息
- 某个角度很强时会被别的角度稀释
- 无法解释是哪个片段命中

不推荐作为主方案。

### 做法 2：保留片段级向量，查询时按片段分数聚合

优点：

- 保留多角度语义
- 可以解释命中来源
- 更适合后续调权

推荐采用这个方案。

## 8.2 推荐的召回流程

### 第一步：片段级向量召回

HNSW 中存的不是“素材向量”，而是“片段向量”。

也就是说：

- 一个素材有 4 个音频片段，就向索引写 4 条记录
- HNSW label 对应 `segment_id`，而不是 `asset_id`

### 第二步：候选片段转素材候选

查询返回若干片段：

- `segment_id`
- `asset_id`
- `segment_key`
- `embedding_similarity`

然后按 `asset_id` 分组。

### 第三步：按片段类型加权聚合

建议公式：

```text
asset_vector_score =
    w_comprehensive * max(sim of comprehensive segments)
  + w_instruments   * max(sim of instruments segments)
  + w_style         * max(sim of style segments)
  + w_emotion       * max(sim of emotion segments)
```

再除以总权重：

```text
normalized_asset_vector_score =
    asset_vector_score / (w_comprehensive + w_instruments + w_style + w_emotion)
```

推荐初始权重：

- `comprehensive = 0.40`
- `style = 0.25`
- `emotion = 0.20`
- `instruments = 0.15`

理由：

- `comprehensive` 对多数自然语言检索最稳
- `style` 与 `emotion` 对音乐搜索很重要
- `instruments` 有价值，但通常不应压过整体描述

## 8.3 为什么取 `max` 而不是 `avg`

同一个片段类型下，当前设计通常只有一条记录；即使以后允许多条子片段，也建议同类型先取 `max`：

- 用户提到某个强特征时，最匹配的那条片段最重要
- 平均值会把强命中拉低

## 8.4 rerank 仍然保留

向量聚合只负责粗召回和初排，最终仍建议保留 rerank。

但 rerank 输入不再只是一段 `description`，而是结构化拼接文本，例如：

```text
[整体]
一段以钢琴和弦乐为主的舒缓配乐，整体温暖而略带伤感。
[乐器]
主要由钢琴、弦乐和大提琴构成，伴随柔和的铺底音色。
[风格]
整体节奏平稳，旋律性较强，偏影视配乐和抒情氛围音乐。
[情绪]
情绪温暖、安静，带有轻微伤感和怀旧感。
```

最终分数建议仍然使用：

```text
final_score = alpha * normalized_asset_vector_score + beta * normalized_rerank_score
```

建议初始值：

- `alpha = 0.35`
- `beta = 0.65`

这与当前代码的总策略一致，只是把单向量分数替换成了“多片段聚合后的向量分数”。

## 9. 对现有代码的改造点

## 9.1 Avalonia / Application 层

### AssetDescriptionService

当前只接收 `output_text`。

建议改造为：

- 接收结构化 `output_json`
- 生成 `AssetDescriptionDocument`
- 再拆分出多个 `AssetDescriptionSegmentDocument`

### AssetDescriptionStore

当前只写 `asset_descriptions.description`。

建议改造为：

- 保存 `description_text`
- 保存 `description_json`
- 保存 `description_schema`

### VectorizeDescriptionsUseCase

当前逻辑是：

- 一条描述 -> 一个向量文档

建议改成：

- 一条结构化描述 -> 多个片段文档
- 逐片段调用 `TextVectorizationService`
- 写入多条 `asset_description_segment_vectors`

### AssetTextVectorizationService

当前请求结构只支持一个 `description`。

建议改成批量接口：

- 输入 `segments[]`
- 每个元素含 `segment_id`、`segment_key`、`text`

如果暂时不想改后端批量接口，也可以先保留逐条调用。

## 9.2 Backend / Search 层

### search/index

当前只接受一段描述并返回一个向量。

建议升级两种方式任选其一：

#### 方式 A：增加批量接口

- `POST /api/v1/search/index/batch`

输入：

- `asset_id`
- `asset_format`
- `segments[]`

输出：

- 每个 segment 对应一个向量

推荐这一种。

#### 方式 B：保留单条接口

Avalonia 端循环调用多次。

实现简单，但效率低。

### sqlite_vector_repository.py

当前读取的是“一个素材一条向量记录”。

建议改造为：

- 读取片段向量表
- 返回 `segment_id`、`asset_id`、`segment_key`、`segment_text`、`weight`、`vector`

### hnsw_index_manager.py

当前 label 对应 `doc_id`，而 `doc_id` 对应素材级记录。

建议改成片段级：

- 每条片段一个 `doc_id`
- metadata 中保留 `doc_id -> segment_id -> asset_id`

### search_service.py

当前逻辑是：

- HNSW 返回素材记录
- 直接计算单素材分数

建议改成：

1. HNSW 返回片段候选
2. 以 `asset_id` 分组
3. 按 `segment_key` 权重聚合
4. 取 Top-N 素材
5. 用拼接后的结构化文本做 rerank

## 10. 向后兼容策略

不建议一次性把所有素材描述都强制迁到结构化模式。

建议做兼容分层：

### 阶段 1

- 只有音频启用结构化描述和多向量
- 其他类型继续单描述单向量

### 阶段 2

- 查询逻辑同时支持两类素材：
  - 结构化多向量素材
  - 旧单向量素材

聚合时：

- 结构化素材走片段聚合
- 旧素材直接把原有向量分数当作 `comprehensive`

### 阶段 3

- 对旧描述做批量重生成和重建索引

## 11. 推荐落地顺序

建议按下面顺序做，不要一次改完。

### 第一步：只改描述存储结构

- `asset_descriptions` 增加 `description_json`
- 新增 `asset_description_segments`
- 先不改搜索

目标：

- 先确认结构化描述稳定生成并能正确落库

### 第二步：支持多片段向量写入

- 新增 `asset_description_segment_vectors`
- 保留旧 `asset_description_vectors`

目标：

- 新结构先能写，不急着切换搜索主链路

### 第三步：实现片段级 HNSW 与聚合搜索

- 改后端 repository
- 改 HNSW 构建
- 改 `search_service.py`

目标：

- 真正跑通“多向量加权召回”

### 第四步：接入 rerank 与 UI 展示

- 搜索结果可展示“命中片段”
- 支持调权

### 第五步：清理旧单向量链路

- 确认效果稳定后，再考虑下线旧表或只保留兼容读取

## 12. 推荐的最低可用版本

如果先做一个最小闭环，建议只做这些：

- 音频结构化 JSON 描述
- 新增 `asset_description_segments`
- 新增 `asset_description_segment_vectors`
- 片段级 HNSW
- 查询时按 `comprehensive / instruments / style / emotion` 加权聚合
- rerank 输入改为结构化拼接文本

先不要做：

- 可配置权重 UI
- 多 schema 动态注册中心
- 批量重生成后台任务编排
- 所有素材类型一起改

## 13. 风险与注意事项

### 13.1 结构化输出不稳定

风险：

- 大模型返回脏 JSON
- 字段缺失
- 标签过多或过碎

对策：

- 后端严格校验
- 每个素材类型固定 schema
- 保留 schema version

### 13.2 召回候选数不够

一个素材有多个片段后，HNSW 的 Top-K 如果还按旧值，可能只拿到片段，不足以覆盖足够多素材。

对策：

- 查询时把片段候选数放大
- 例如最终想要 20 个素材候选，先取 100 到 200 个片段候选

### 13.3 聚合权重过拟合

风险：

- 某类查询被某个片段权重压制

对策：

- 第一版先把权重写死
- 通过人工样本评估后再调

### 13.4 数据迁移复杂度上升

对策：

- 新表优先增量引入
- 旧表先保留
- 使用一次性 Python 脚本做物理结构迁移

## 14. 结论

这个功能完全可以做，而且很适合当前仓库的搜索目标。

但要注意，真正有价值的不是“把 JSON 存下来”，而是同时完成这 4 件事：

1. 结构化描述稳定生成
2. 每个片段独立向量化
3. 检索阶段按片段类型加权聚合
4. 最终仍以素材为单位输出和 rerank

对当前仓库来说，推荐路线是：

- 先只做音频
- 保留旧链路兼容
- 新增片段表和片段向量表
- HNSW 改为片段级索引
- 搜索服务按素材聚合片段分数

这是当前复杂度、收益和可维护性之间最稳的方案。
