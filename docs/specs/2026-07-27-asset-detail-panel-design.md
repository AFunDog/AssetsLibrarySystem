# 素材详情面板：子类型展示 + 角度描述查看

## 背景

当前素材详情面板只显示原始 JSON 描述文本，没有按角度拆分展示，也没有设置子类型的功能。用户需要：
1. 查看素材当前子类型，并能手动修改
2. 按角度（时间线/人物/场景/动作/情感/镜头等）分别查看描述内容

## 设计

### 布局方案（方案 B）

左侧信息面板 + 右侧角度描述列表，不切换 Tab。

```
┌──────────────────────────────────────────────────┐
│ 素材名                              类型  子类型  │
│                                                   │
│ ┌──────────┐  ┌────────────────────────────────┐ │
│ │ 类型 视频 │  │ 🎬 时间线            300字    │ │
│ │ 子类型    │  │ 00:00-00:08：雨天街景...     │ │
│ │ 动画  ✏️ │  ├────────────────────────────────┤ │
│ │ 时长      │  │ 👤 人物             160字    │ │
│ │ 146.8s   │  │ 银发双马尾少女、棕发少女...   │ │
│ │ 状态     │  ├────────────────────────────────┤ │
│ │ 已描述   │  │ 📷 场景             140字    │ │
│ │ [描述]   │  │ 昏暗的乐队排练室...           │ │
│ │ [向量化] │  ├────────────────────────────────┤ │
│ └──────────┘  │ ...更多角度...                 │ │
│               └────────────────────────────────┘ │
└──────────────────────────────────────────────────┘
```

### 架构

```
C# 端
  AngleProfileManager  ──→  提供子类型列表 + 角度配置
        ↑
  angle_profiles.yaml  ──→  子类型与角度定义

  LibraryCatalogService  ──→  解析 JSON → 角度列表
        ↑
  AssetDescriptionStore  ──→  读取 description JSON
        ↑
  SQLite (asset_metadata + asset_descriptions)

  UI (LibraryPage.axaml)  ←──  绑定角度列表 + 子类型
```

### 新增模型

`Models/AngleDescriptionRecord.cs`：

```csharp
public sealed record AngleDescriptionRecord(
    string AngleKey,       // "场景", "动作"
    string Label,          // "场景环境", "动作事件"
    string Text,           // 描述文本
    string[] Tags,         // 标签
    int MaxLength);        // 最大字数
```

### 数据流

**加载描述：**
1. 用户选中素材 → `LibraryCatalogService.LoadAssetDescription()`
2. 从 `AssetDescriptionStore` 获取原始 JSON 描述
3. `StructuredDescriptionHelper.ExtractSegments()` 解析角度
4. 结合 `AngleProfileManager` 获取每个角度的 Label/MaxLength
5. 构建 `ObservableCollection<AngleDescriptionRecord>` → 绑定 UI

**修改子类型：**
1. 用户点击 ✏️ → 弹出下拉菜单（A 方案）或右键菜单（C 方案）
2. 选中新子类型 → `UpdateAssetSubtypeAsync(newSubtype)`
3. UPDATE `asset_metadata.subtype`
4. 刷新角度列表（原始 JSON 不变，但按新子类型的角度配置重新解析）

### 子类型修改方式

- **A：下拉选择** — 点击 ✏️ 图标，弹出 ComboBox 下拉菜单
- **C：右键菜单** — 素材列表右键 → 修改子类型 → 选择子类型

### 涉及文件

| 文件 | 操作 |
|------|------|
| `Models/AngleDescriptionRecord.cs` | 新增 |
| `Services/Library/LibraryCatalogService.cs` | 修改 |
| `ViewModels/AssetDetailViewModel.cs` | 修改 |
| `Views/Pages/LibraryPage.axaml` | 修改 |
| `Views/Pages/LibraryPage.axaml.cs` | 修改 |
| `Services/AssetDescription/AssetDescriptionStore.cs` | 修改 |
| `Services/Infrastructure/SqliteAssetDatabase.cs` | 可选 |

### 不涉及

- Python 后端：不需要改动
- 角度配置 YAML：不需要改动
- 提示词模板：不需要改动