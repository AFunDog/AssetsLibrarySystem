## 结构化描述系统 — 实现方案（修订版）

### 架构边界

```
[C# 端]                              [Python 端]
                                      |
  子类型检测 (启发式)                   |
  角度配置管理 (YAML + DB)              |
  构建 Prompt 指令 (含角度定义)  ─────→  ModelService.generate_text()
  用户可修改子类型                       │ 只管:
  管理全局角度配置                       │   - 接收 angles 列表
                                      │   - 动态构建 system prompt
                                      │   - 调 DashScope LLM
                                      │   - 返回 JSON
                                      │
```

**Python 只负责：** 接收 `asset_format` + `angles` 列表 + `asset_path` → 调 LLM → 返回 JSON
**Python 不负责：** 子类型检测、角度配置、素材管理

---

### 第一步：角度配置文件定义（C# 端）

**新增** `src/avalonia/.../Models/AngleProfileConfig.cs`：

```csharp
// 角度定义
public sealed record AngleDefinition(
    string Key,           // 如 "场景", "歌词大意"
    string Label,         // 展示标签，如 "场景环境"
    string Prompt,        // 给 LLM 的指导，如 "描述视频中的场景和环境"
    int MaxLength = 120   // 最大字数
);

// 子类型定义
public sealed record SubtypeProfile(
    string AssetType,     // "视频", "音频"
    string Subtype,       // "实拍", "歌曲"
    string Label,         // 展示名
    IReadOnlyList<AngleDefinition> Angles
);

// 角度配置管理器
public sealed class AngleProfileManager
{
    // 从 YAML 加载内置配置
    // 合并用户自定义配置（从 DB）
    SubtypeProfile GetProfile(string assetType, string? subtype);
    IReadOnlyList<SubtypeProfile> GetProfiles(string assetType);
}
```

**新增** `src/avalonia/.../angle_profiles.yaml`（嵌入资源或 Content 文件）：

```yaml
音频:
  歌曲:
    label: 歌曲
    angles:
      - key: 歌词大意
        label: 歌词大意
        prompt: 分析歌词主题和情感表达
        max_length: 150
      - key: 曲风
        label: 曲风
        prompt: 描述音乐风格和流派
      - key: 情感
        label: 情感
        prompt: 描述音乐传达的情感氛围
      - key: 乐器
        label: 乐器
        prompt: 列出主要使用的乐器
      - key: 整体
        label: 整体
        prompt: 一句话概括这段音乐
  纯音乐:
    label: 纯音乐/伴奏
    angles: [曲风, 情感, 乐器, 整体]
  音效:
    label: 音效
    angles:
      - key: 声音描述
        label: 声音描述
        prompt: 描述声音的特征和质感
      - key: 使用场景
        label: 使用场景
        prompt: 适合用在什么场景
      - key: 整体
  默认:
    label: 通用音频
    angles: [整体, 乐器, 风格, 情感]

视频:
  实拍:
    label: 实拍视频
    angles:
      - key: 场景
        label: 场景环境
        prompt: 描述视频中的场景、环境和背景
      - key: 动作
        label: 动作活动
        prompt: 描述视频中的人物动作或动态变化
      - key: 镜头
        label: 镜头语言
        prompt: 描述镜头类型、运动方式和剪辑节奏
      - key: 整体
        label: 整体
        prompt: 一句话概括视频内容
  动画:
    label: 动画/CG
    angles: [视觉风格, 场景, 动作, 整体]
  游戏录制:
    label: 游戏录制
    angles: [游戏类型, 场景, 动作, 整体]
  默认:
    label: 通用视频
    angles: [整体]
```

---

### 第二步：子类型检测（C# 端）

**新增** `SubtypeDetector.cs` → `ISubtypeDetector`：

```csharp
public interface ISubtypeDetector
{
    string? DetectSubtype(ManagedAssetRecord asset);
}
```

启发式规则：
- **视频/音频**：文件名前缀匹配（`sfx_` → 音效，`BGM_` → 纯音乐，`VO_` → 歌曲/人声）
- **视频**：编码格式、帧率、时长（<30s 短片段 → 可能是实拍/素材）
- **音频**：声道数、时长（<5s 多为音效，>60s 多为歌曲/纯音乐）
- 文件路径关键词（`sfx/`、`music/`、`bgm/`）

**用户可覆盖**：存储在 `asset_metadata.subtype` 中，UI 提供下拉修改

---

### 第三步：Prompt 构建流程（C# 端）

**修改** `AssetDescriptionService.cs`：

```csharp
public async Task<AssetDescriptionDocument> DescribeAsync(
    ManagedAssetRecord asset,
    string? prompt = null,
    string? systemPrompt = null)
{
    // 1. 检测子类型
    var subtype = _subtypeDetector.DetectSubtype(asset)
                  ?? asset.Subtype  // 用户手动指定的
                  ?? "默认";
    
    // 2. 获取角度配置
    var profile = _angleProfileManager.GetProfile(asset.AssetType, subtype);
    
    // 3. 构建 system prompt（动态生成）
    var dynamicSystemPrompt = BuildDynamicSystemPrompt(asset.AssetType, subtype, profile.Angles);
    
    // 4. 传递给 Python
    var request = new BackendModelGenerateRequest
    {
        AssetFormat = asset.AssetType,
        AssetPath = asset.LocalPath,
        Subtype = subtype,
        Angles = profile.Angles.Select(a => new AngleDefinitionDto(a.Key, a.Label, a.Prompt, a.MaxLength)).ToArray(),
        SystemPrompt = systemPrompt ?? dynamicSystemPrompt,
        Prompt = prompt,
    };
    
    // 5. 调用 Python (Python.NET)
    var response = await _modelClient.GenerateAsync(_backendBaseUrl, request, ct);
    
    // 6. 保存 subtype 到数据库
    // ...
}
```

**`BuildDynamicSystemPrompt` 生成的内容：**

```
你是视频素材结构化描述助手。请根据输入视频、格式信息和绝对路径，输出严格合法的 JSON 对象。

输出要求：
- 只描述当前视频内容本身，不做文件管理、使用建议、版权判断或目录推断。
- 只写视频中能明确看到或听到的内容，不得臆造。
- 只能输出 JSON，不要输出 Markdown、代码块、解释或额外文本。
- JSON 必须包含且只包含以下字段： "场景", "动作", "镜头", "整体"
- 每个字段都是对象，包含 "text" 和 "tags"。
- 每个 text 用中文，不超过对应字段的最大字数。
- tags 是简短中文标签数组。

字段含义：
- "场景"：描述视频中的场景、环境和背景（不超过 100 字）
- "动作"：描述视频中的人物动作或动态变化（不超过 100 字）
- "镜头"：描述镜头类型、运动方式和剪辑节奏（不超过 80 字）
- "整体"：一句话概括视频内容（不超过 120 字）

输出格式示例：
{
  "场景": { "text": "...", "tags": ["..."] },
  "动作": { "text": "...", "tags": ["..."] },
  "镜头": { "text": "...", "tags": ["..."] },
  "整体": { "text": "...", "tags": ["..."] }
}
```

---

### 第四步：Python 后端变更

**修改** `ModelGenerateRequest`（`schemas/model.py`）：
- 新增 `angles: list[AngleDef] | None = None` 字段
- `AngleDef` = `{"key": str, "label": str, "prompt": str, "max_length": int}`

**修改** `ModelService.generate_text()`：
- 如果 `angles` 不为空，用传入的 angles 动态构建 system prompt
- 如果 `angles` 为空（旧版本），回退到 `prompts.yaml` 的静态配置

**修改** `model_service.py` 中 `_call_dashscope` 等：
- 核心逻辑不变——仍然是调 LLM 返回 JSON
- 只是 system prompt 的构建方式变了

Python 端改动很小，基本就是新增一个 `build_system_prompt_from_angles()` 函数。

---

### 第五步：C# 数据层变更

**修改** `SqliteAssetDatabase.cs`：
- `asset_metadata` 表新增列：`subtype TEXT DEFAULT NULL`

**新增** 用户角度配置表（可选，第一版可以不做）：
```sql
CREATE TABLE user_angle_profiles (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    asset_type TEXT NOT NULL,
    subtype TEXT NOT NULL,
    angles_json TEXT NOT NULL,    -- 自定义角度组合
    is_active INTEGER DEFAULT 0,
    created_at TEXT DEFAULT (datetime('now'))
);
```

---

### 第六步：C# 模型层调优

**修改** `StructuredDescriptionHelper.cs`：
- `SortSegments` 移除硬编码优先级（`"全面"→0, "乐器"→1, ...`）
- 改为按 JSON 中的出现顺序保持，或传入配置顺序
- `ExtractSegments` 已经是动态角度的，只需排序调整

**修改** `AssetDescriptionDocument.cs`：
- 新增 `Subtype` 属性

**修改** `BackendApiContracts.cs`：
- `BackendModelGenerateRequest` 新增 `Subtype` + `Angles` 字段
- `AngleDefinitionDto` 新增 record

---

### 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| **C# 新增** | | |
| `Models/AngleProfileConfig.cs` | 新增 | 角度配置数据模型 + 管理器 |
| `Models/AngleDefinitionDto.cs` | 新增 | 传给 Python 的 DTO |
| `Services/SubtypeDetector.cs` | 新增 | 子类型启发式检测 |
| **C# 配置文件** | | |
| `angle_profiles.yaml` | 新增 | 内置角度配置（嵌入资源） |
| **C# 修改** | | |
| `Models/StructuredDescriptionHelper.cs` | 修改 | 移除硬编码优先级 |
| `Models/AssetDescriptionDocument.cs` | 修改 | 新增 Subtype |
| `Services/AssetDescription/AssetDescriptionService.cs` | 修改 | 子类型检测 + 动态 Prompt |
| `Services/BackendApi/BackendApiContracts.cs` | 修改 | 新增 Subtype + Angles |
| `Services/Python/PythonModelService.cs` | 修改 | 传递 Subtype + Angles |
| `Services/Infrastructure/SqliteAssetDatabase.cs` | 修改 | 新增 subtype 列 |
| `Services/AssetSearch/SearchPipelineComponents.cs` | 修改 | 加载 subtype |
| `DependencyInjection/ApplicationModule.cs` | 修改 | 注册新服务 |
| **Python 新增** | | |
| `app/core/angle_prompt_builder.py` | 新增 | 从 angles 列表构建 system prompt |
| **Python 修改** | | |
| `app/schemas/model.py` | 修改 | 新增 AngleDef + angles 字段 |
| `app/application/services/model_service.py` | 修改 | 接收 angles 参数，动态构建 prompt |

---

### 实施顺序

1. **角度配置数据模型**（C#）：`AngleProfileConfig.cs` + `angle_profiles.yaml`
2. **Python 接收角度**：`schemas/model.py` + `angle_prompt_builder.py` + `model_service.py`
3. **C# 传递角度**：`BackendApiContracts.cs` + `PythonModelService.cs` + `AssetDescriptionService.cs`
4. **子类型检测**（C#）：`SubtypeDetector.cs`
5. **数据库**：`SqliteAssetDatabase.cs` 新增 subtype 列
6. **结构化描述助手**：移除硬编码优先级
7. **DI 注册** + 测试

---

### 向后兼容

- 旧请求没有 `angles` → Python 回退到 `prompts.yaml` 的静态配置
- 旧数据 `subtype IS NULL` → 走 `默认` 子类型的角度配置
- `StructuredDescriptionHelper` 解析 JSON 时已支持动态角度名，只需调整排序
- 所有现有描述和向量不受影响