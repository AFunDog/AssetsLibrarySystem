# 数据库与搜索链路优化清单

本文档记录当前项目中已经确认的问题与可优化点，并按优先级和修改复杂度排序，方便后续逐项收敛。

## 1. 评估标准

- 优先级 `P0`：会直接导致搜索失败、数据不一致或迁移风险。
- 优先级 `P1`：会造成结构冗余、维护困难或后续扩展成本明显上升。
- 优先级 `P2`：属于工程质量优化，短期不影响主流程，但建议尽快补齐。

复杂度说明：

- `低`：单文件调整、文档更新、简单脚本或字段删除。
- `中`：涉及多个服务或表结构，但迁移路径清晰。
- `高`：涉及数据库重建、跨层接口收敛或历史兼容处理。

## 2. 问题与优化点

| 优先级 | 问题 / 优化点 | 修改复杂度 | 建议 |
|---|---|---:|---|
| P0 | 后端搜索链路对 SQLite schema 变更敏感，曾出现列别名、`rowid`、时间格式兼容问题。 | 中 | 增加后端启动/查询前的 schema 校验，避免运行时 500。 |
| P0 | `asset_description_vectors` 之前重复存了 `asset_name`、`asset_type`、`asset_path`、`description`，容易和主表不一致。 | 中 | 继续收敛向量表，只保留向量必需字段和来源字段，展示信息全部回链。 |
| P0 | `asset_metadata.description_text` 与 `asset_descriptions.description` 语义重复。 | 低 | 继续保持 `description_text` 为空或移除，描述正文只保留在 `asset_descriptions`。 |
| P1 | `asset_descriptions` 仍保存 `asset_name`、`asset_type`、`asset_path` 等快照字段，与 `assets` 存在重复。 | 中 | 评估是否保留为历史快照；若不需要回溯，可改为只保留 `asset_uid + description + 生成上下文`。 |
| P1 | `asset_locations` 被证明是冗余表，当前不再承载主流程。 | 低 | 直接移除该表和写入逻辑，改为只保留 `assets.current_path` 作为当前位置。 |
| P1 | `asset_metadata`、`asset_descriptions`、`asset_description_vectors` 的 schema 定义分散在多个 C# 类里。 | 中 | 抽出统一 schema 管理入口，避免多处 `CREATE TABLE IF NOT EXISTS` 漂移。 |
| P1 | 当前迁移依赖一次性 Python 脚本，缺少版本化迁移机制。 | 高 | 引入 `PRAGMA user_version` 或等价版本号，后续按版本增量迁移。 |
| P2 | 后端索引记录 `doc_id` 目前是按查询结果枚举生成，不是数据库中的稳定 ID。 | 低 | 若后续需要增量索引，建议把 `vector_id` 持久化到向量表或索引元数据中。 |
| P2 | 规范化脚本是一次性工具，但目前仍放在 `scripts/` 目录中，容易和日常脚本混淆。 | 低 | 迁移完成后归档脚本或在文件名中明确标注“one-time”。 |
| P2 | 现有测试更偏单元验证，缺少跨“扫描 -> 描述 -> 向量 -> 搜索”的端到端回归。 | 中 | 补一组集成测试，重点覆盖旧库迁移后、规范化后以及搜索回链字段。 |

## 3. 推荐实施顺序

1. 先稳定 `P0` 项，避免搜索 500 和数据不一致。
2. 再收敛 `P1` 项，减少冗余字段与 schema 漂移。
3. 最后补 `P2` 项，把工程化和测试闭环补齐。

## 4. 当前建议的最小目标

- `assets` 作为素材主身份表。
- `asset_metadata` 只保存标签和状态，不保存描述正文。
- `asset_descriptions` 只保留描述结果与生成上下文。
- `asset_description_vectors` 只保存向量与向量来源，不保存展示字段。
- 后端搜索只从 `assets + asset_descriptions + asset_metadata` 回链展示信息。

## 5. 备注

本文档只记录“当前已经确认的问题”和“建议优化方向”，不替代具体迁移脚本或实现代码。
