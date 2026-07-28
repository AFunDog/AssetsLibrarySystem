# Avalonia 端完整 CRUD 功能设计

> 设计文档
> 日期：2026-07-28
> 状态：待评审

## 1. 目标

补齐当前系统的 CRUD 短板，在 Avalonia 桌面端实现素材库、素材、素材描述三个领域的完整增删改查能力，覆盖：删除素材库、删除素材、编辑标签、编辑描述、重命名等缺失操作。

## 2. 设计原则

- **沿用现有分层**：Domain Model → Service Interface → Service Implementation → UseCase(可选) → ViewModel → View
- **不引入新依赖**：不新增第三方库，不引入消息队列、独立向量库等
- **不重构现有功能**：只新增缺失操作，不修改已有的扫描、描述生成、向量化、搜索流程
- **交互两级**：右键菜单做快捷操作，详情面板做复杂编辑

## 3. 功能清单

### 3.1 素材库 (Libraries)

| 操作 | 现状 | 目标 |
|------|------|------|
| Delete | ❌ 无 | 删除素材库及级联所有关联数据 |
| Update | ❌ 无 | 重命名素材库 |

### 3.2 素材 (Assets)

| 操作 | 现状 | 目标 |
|------|------|------|
| Delete | ❌ 无 | 删除单个素材及级联关联数据 |
| Update Tags | ❌ 无 | 编辑素材标签（tags_json），LLM 描述驱动 + 手动编辑 |
| Update Name | ❌ 无 | 重命名素材（更新 assets.asset_name） |

### 3.3 素材描述 (Descriptions)

| 操作 | 现状 | 目标 |
|------|------|------|
| Update | ❌ 无 | 手动编辑描述文本，标记向量为过期，触发重新向量化 |

## 4. 接口设计

### 4.1 IAssetLibraryService 新增

```csharp
public interface IAssetLibraryService
{
    // 现有
    Task<IReadOnlyList<LibraryWorkspace>> GetLibrariesAsync(CancellationToken ct = default);
    Task<LibraryWorkspace> AddLibraryAsync(string folderPath, CancellationToken ct = default);
    Task<IReadOnlyList<ManagedAssetRecord>> ScanLibraryAsync(LibraryWorkspace library, CancellationToken ct = default);

    // 新增
    Task DeleteLibraryAsync(long libraryId, CancellationToken ct = default);
    Task UpdateLibraryAsync(long libraryId, string newName, CancellationToken ct = default);
    Task DeleteAssetAsync(long assetId, CancellationToken ct = default);
    Task UpdateAssetTagsAsync(long assetId, string[] tags, CancellationToken ct = default);
    Task UpdateAssetNameAsync(long assetId, string newName, CancellationToken ct = default);
}
```

### 4.2 IAssetDescriptionStore 新增

```csharp
public interface IAssetDescriptionStore
{
    // 现有
    string DatabasePath { get; }
    Task SaveAsync(AssetDescriptionDocument document, CancellationToken ct = default);
    Task<AssetDescriptionDocument?> TryGetAsync(long assetId, CancellationToken ct = default);
    Task<AssetDescriptionDocument?> TryGetForAssetAsync(ManagedAssetRecord asset, CancellationToken ct = default);
    Task<bool> DeleteAsync(long assetId, CancellationToken ct = default);

    // 新增
    Task UpdateDescriptionAsync(long assetId, string newDescription, CancellationToken ct = default);
}
```

## 5. 数据库操作

### 5.1 删除素材库

```sql
-- 由于外键 ON DELETE CASCADE，只需删除 libraries 行
-- 级联删除顺序：libraries → assets → asset_metadata
--                                   → asset_descriptions
--                                   → asset_description_vectors
DELETE FROM libraries WHERE id = $id;
```

### 5.2 删除单个素材

```sql
-- 同理，级联删除
DELETE FROM assets WHERE id = $id;
```

### 5.3 更新标签

```sql
UPDATE asset_metadata
SET tags_json = $tags_json, updated_at = $updated_at
WHERE asset_id = $asset_id;
```

### 5.4 更新素材名称

素材名称同时存储在 `assets` 和 `asset_descriptions` 两张表中，需要同步更新：

```sql
UPDATE assets SET asset_name = $asset_name, updated_at = $updated_at WHERE id = $asset_id;
UPDATE asset_descriptions SET asset_name = $asset_name WHERE asset_id = $asset_id;
```

### 5.5 更新描述文本

更新描述时需同时更新 `generated_at`（设为当前时间），使 `NeedsVectorizationAsync` 能够检测到描述已更新，从而触发重新向量化：

```sql
UPDATE asset_descriptions
SET description = $description,
    generated_at = $generated_at,
    metadata_status = 'edited'
WHERE asset_id = $asset_id;
```

更新描述后，同步更新 `asset_metadata` 的 `vector_state` 为 `'pending'`，以触发后续向量化流程重新生成向量。

## 6. 索引重建策略

删除操作后，如果被删除的素材存在向量，需要重建 HNSW 索引：

- 删除素材库：批量删除后统一重建索引
- 删除单个素材：删除后重建索引
- 更新描述：标记向量为过期，由后续向量化流程自动处理

重建复用现有的 `IAssetSearchService.ReindexAsync()`。

## 7. UI 交互设计

### 7.1 右键菜单扩展

**素材库节点（Library）右键菜单：**

```
┌──────────────────────────────┐
│ 定位文件或文件夹位置         │  ← 已有
│ 加入描述任务队列             │  ← 已有
│ 向量化当前素材/文件夹        │  ← 已有
│ ─────────────────────────── │
│ 重命名素材库                 │  ← 新增
│ 删除素材库                   │  ← 新增
└──────────────────────────────┘
```

**素材文件节点（File）右键菜单：**

```
┌──────────────────────────────┐
│ 定位文件或文件夹位置         │  ← 已有
│ 加入描述任务队列             │  ← 已有
│ 向量化当前素材/文件夹        │  ← 已有
│ 删除描述记录                 │  ← 已有
│ ─────────────────────────── │
│ 编辑标签                     │  ← 新增
│ 重命名素材                   │  ← 新增
│ 删除素材                     │  ← 新增
└──────────────────────────────┘
```

### 7.2 详情面板增强

右侧详情面板在现有布局基础上，在基本信息区域增加：

**标签编辑：**
- 展示已有标签为 chip 样式（带 × 删除按钮）
- 底部有输入框 + "添加"按钮
- 修改后自动保存到数据库

**操作按钮：**
- "编辑描述"按钮 — 点击弹出编辑对话框，可修改描述 JSON 文本
- "删除素材"按钮 — 触发确认弹窗

### 7.3 确认弹窗

删除操作按场景区分：

- **删除单个素材**：简单确认弹窗
  > "确定要删除素材「xxx」吗？相关的描述和向量记录也会被一并删除。"
  > [取消] [确认删除]

- **删除素材库**：带警告的确认弹窗
  > "确定要删除素材库「xxx」吗？该操作将删除库内所有素材、描述和向量记录，不可撤销。"
  > [取消] [确认删除]

- **批量删除**：显示汇总信息
  > "共删除 X 个素材，Y 个描述记录，Z 个向量记录。"

## 8. 实现顺序

| 阶段 | 内容 | 工作量估计 |
|------|------|-----------|
| 1 | `IAssetLibraryService` 新增 DeleteLibraryAsync / DeleteAssetAsync 实现 | 小 |
| 2 | 右键菜单"删除素材库"、"删除素材" + 确认弹窗 | 中 |
| 3 | `IAssetLibraryService` 新增 UpdateAssetTagsAsync 实现 | 小 |
| 4 | 标签编辑 UI（chip 展示 + 添加/删除） | 中 |
| 5 | `IAssetLibraryService` 新增 UpdateLibraryAsync / UpdateAssetNameAsync | 小 |
| 6 | 重命名素材库/素材 UI | 中 |
| 7 | `IAssetDescriptionStore` 新增 UpdateDescriptionAsync | 小 |
| 8 | 编辑描述 UI（弹窗 + 文本编辑器） | 中 |
| 9 | 删除后索引重建联动 | 小 |

## 9. 边界与约束

- 删除素材库时，如果数据库外键约束未正确级联，需要手动实现级联删除
- 删除后重建索引仅在存在向量数据时触发
- 标签编辑不影响描述生成流程，只是手动补充
- 描述编辑后不自动重新向量化，由用户后续手动触发"批量向量化"或等待增量向量化流程
- 素材重命名只更新数据库中的 `asset_name` 字段，不修改实际文件名