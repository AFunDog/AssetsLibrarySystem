# 视频切片描述实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 对长视频（≥60s）进行场景检测切片，每个片段独立描述，最后合成整体摘要

**Architecture:**
- Python 端：PySceneDetect 检测场景 → ffmpeg 提取片段 → 每个片段调 LLM → 合成整体摘要
- C# 端：传递切片配置参数（开关、阈值），子类型角度配置已就绪
- 输出 JSON：`{整体, segments: [{start_time, end_time, 角度1, 角度2, ...}]}`

**Tech Stack:** PySceneDetect 0.7.1, ffmpeg, DashScope multimodal API, Python.NET

**前置条件：** 子类型与角度配置系统已就绪（已提交的 angle_profiles.yaml + AngleProfileManager）

---

## 文件结构

### Python 后端

| 文件 | 操作 | 职责 |
|------|------|------|
| `app/application/services/video_scene_detector.py` | 新建 | PySceneDetect 封装，返回场景列表 |
| `app/application/services/video_slice_describer.py` | 新建 | 编排切片+描述+合成流程 |
| `app/schemas/model.py` | 修改 | 新增切片相关字段 |
| `app/application/services/model_service.py` | 修改 | 处理切片视频的 generate_text |
| `app/core/angle_prompt_builder.py` | 修改 | 新增合成摘要的 prompt builder |
| `pyproject.toml` | 修改 | 添加 scenedetect 依赖 |
| `tests/test_video_scene_detector.py` | 新建 | 场景检测单元测试 |
| `tests/test_video_slice_describer.py` | 新建 | 切片描述器单元测试 |

### C# 端

| 文件 | 操作 | 职责 |
|------|------|------|
| `Models/AngleProfileConfig.cs` | 修改 | 新增 `AngleProfile.VideoSlicingConfig` 配置 |
| `Services/BackendApi/BackendApiContracts.cs` | 修改 | 新增切片字段到请求 |
| `Services/Python/PythonModelService.cs` | 修改 | 传递切片字段 |
| `Services/AssetDescription/AssetDescriptionService.cs` | 修改 | 读取子类型切片配置并传递 |
| `Tests/AngleProfileManagerTests.cs` | 修改 | 新增切片配置测试 |

---

### Task 1: Python - VideoSceneDetector 模块

**Files:**
- Create: `src/backend/app/application/services/video_scene_detector.py`
- Test: `src/backend/tests/test_video_scene_detector.py`

- [ ] **Step 1: 安装 PySceneDetect**

```bash
cd /d/GitRepository/AssetsLibrarySystem/src/backend
.venv/Scripts/python.exe -m pip install scenedetect -q
```

Expected: 安装成功，无错误

- [ ] **Step 2: 创建 VideoSceneDetector**

```python
# src/backend/app/application/services/video_scene_detector.py
from __future__ import annotations

import logging
from dataclasses import dataclass
from pathlib import Path

from scenedetect import SceneManager, open_video, AdaptiveDetector

logger = logging.getLogger(__name__)


@dataclass(slots=True)
class SceneRange:
    """一个场景的时间范围"""
    start_frame: int
    end_frame: int
    start_sec: float
    end_sec: float


class VideoSceneDetector:
    """使用 PySceneDetect 检测视频场景边界"""

    def __init__(
        self,
        adaptive_threshold: float = 3.0,
        min_scene_len: int = 15,
    ) -> None:
        self._adaptive_threshold = adaptive_threshold
        self._min_scene_len = min_scene_len

    def detect(self, video_path: str | Path) -> list[SceneRange]:
        """检测视频中的场景，返回场景时间范围列表"""
        video = open_video(str(video_path))
        manager = SceneManager()
        manager.add_detector(AdaptiveDetector(
            adaptive_threshold=self._adaptive_threshold,
            min_scene_len=self._min_scene_len,
        ))
        manager.detect_scenes(video)
        scene_list = manager.get_scene_list()

        if not scene_list:
            logger.info("未检测到场景边界，将整个视频作为一个场景处理")
            total_frames = video.duration.get_frames()
            fps = video.frame_rate
            return [SceneRange(
                start_frame=0,
                end_frame=total_frames,
                start_sec=0.0,
                end_sec=total_frames / fps if fps else 0.0,
            )]

        return [
            SceneRange(
                start_frame=start.get_frames(),
                end_frame=end.get_frames(),
                start_sec=start.get_seconds(),
                end_sec=end.get_seconds(),
            )
            for start, end in scene_list
        ]
```

- [ ] **Step 3: 写测试文件**

```python
# src/backend/tests/test_video_scene_detector.py
from __future__ import annotations

import unittest
from pathlib import Path

from app.application.services.video_scene_detector import (
    VideoSceneDetector,
    SceneRange,
)


class VideoSceneDetectorTestCase(unittest.TestCase):
    """VideoSceneDetector 单元测试
    使用短视频文件测试场景检测功能。
    """

    def setUp(self):
        self.detector = VideoSceneDetector(
            adaptive_threshold=3.0,
            min_scene_len=15,
        )
        # 使用项目中的测试视频
        self.test_video = self._find_test_video()

    def _find_test_video(self) -> str:
        """查找测试用的短视频文件"""
        # 从仓库根目录查找
        current = Path(__file__).resolve().parents[3]
        candidates = [
            current / "__assets__" / "demo_video1.mp4",
            current / "assets" / "test.mp4",
            current / "tests" / "fixtures" / "test_video.mp4",
        ]
        for c in candidates:
            if c.exists():
                return str(c)
        # 如果没有测试视频，创建一个
        return self._create_test_video()

    def _create_test_video(self) -> str:
        """用 ffmpeg 生成一个简单的测试视频"""
        import subprocess
        import tempfile
        path = Path(tempfile.gettempdir()) / "test_video_scene_detect.mp4"
        if path.exists():
            return str(path)
        # 生成 5 秒视频，包含不同颜色场景
        cmd = [
            "ffmpeg", "-y",
            "-f", "lavfi", "-i", "color=c=red:s=320x240:d=1",
            "-f", "lavfi", "-i", "color=c=green:s=320x240:d=1",
            "-f", "lavfi", "-i", "color=c=blue:s=320x240:d=1",
            "-f", "lavfi", "-i", "color=c=white:s=320x240:d=1",
            "-f", "lavfi", "-i", "color=c=black:s=320x240:d=1",
            "-filter_complex",
            "[0:v][1:v][2:v][3:v][4:v]concat=n=5:v=1:a=0",
            "-t", "5",
            str(path),
        ]
        subprocess.run(cmd, capture_output=True)
        return str(path) if path.exists() else ""

    def test_detect_returns_scene_ranges(self):
        if not self.test_video:
            self.skipTest("无法创建测试视频")
        scenes = self.detector.detect(self.test_video)
        self.assertIsInstance(scenes, list)
        self.assertGreater(len(scenes), 0)
        for scene in scenes:
            self.assertIsInstance(scene, SceneRange)
            self.assertGreaterEqual(scene.start_frame, 0)
            self.assertGreater(scene.end_frame, scene.start_frame)
            self.assertGreaterEqual(scene.end_sec, scene.start_sec)

    def test_detect_scene_range_has_seconds(self):
        if not self.test_video:
            self.skipTest("无法创建测试视频")
        scenes = self.detector.detect(self.test_video)
        for scene in scenes:
            self.assertGreaterEqual(scene.start_sec, 0.0)
            self.assertGreater(scene.end_sec, scene.start_sec)

    def test_detect_returns_empty_for_missing_file(self):
        with self.assertRaises(Exception):
            self.detector.detect("/nonexistent/video.mp4")


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 4: 运行测试**

```bash
cd /d/GitRepository/AssetsLibrarySystem/src/backend
.venv/Scripts/python.exe -m pytest tests/test_video_scene_detector.py -v
```

Expected: 测试通过或跳过（无测试视频时）

- [ ] **Step 5: Commit**

```bash
cd /d/GitRepository/AssetsLibrarySystem
git add src/backend/app/application/services/video_scene_detector.py
git add src/backend/tests/test_video_scene_detector.py
git commit -m "feat(video): 添加 VideoSceneDetector 场景检测模块"
```

---

### Task 2: Python - VideoSliceDescriber 模块

**Files:**
- Create: `src/backend/app/application/services/video_slice_describer.py`
- Test: `src/backend/tests/test_video_slice_describer.py`

- [ ] **Step 1: 创建 VideoSliceDescriber**

```python
# src/backend/app/application/services/video_slice_describer.py
from __future__ import annotations

import json
import logging
import subprocess
import tempfile
from pathlib import Path
from typing import Any

from app.application.services.video_scene_detector import (
    VideoSceneDetector,
    SceneRange,
)

logger = logging.getLogger(__name__)

# 切片阈值：视频时长超过此值才启用切片（秒）
DEFAULT_SLICE_THRESHOLD_SECONDS = 60.0


def ffmpeg_extract_segment(
    video_path: str,
    start_sec: float,
    end_sec: float,
    output_path: str,
) -> str:
    """用 ffmpeg 快速提取视频片段（-c copy 不重编码）"""
    duration = end_sec - start_sec
    cmd = [
        "ffmpeg", "-y",
        "-ss", str(start_sec),
        "-i", video_path,
        "-t", str(duration),
        "-c", "copy",
        "-avoid_negative_ts", "make_zero",
        str(output_path),
    ]
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        logger.warning("ffmpeg 提取片段失败: %s, stderr=%s", result.returncode, result.stderr[:200])
        raise RuntimeError(f"ffmpeg 提取片段失败: {result.stderr}")
    return output_path


def get_video_duration(video_path: str) -> float:
    """用 ffprobe 获取视频时长（秒）"""
    cmd = [
        "ffprobe", "-v", "error",
        "-show_entries", "format=duration",
        "-of", "default=noprint_wrappers=1:nokey=1",
        video_path,
    ]
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0 or not result.stdout.strip():
        return 0.0
    try:
        return float(result.stdout.strip())
    except ValueError:
        return 0.0


class VideoSliceDescriber:
    """视频切片描述器
    两阶段流程：
    1. 检测场景 → 提取每个片段 → 调 LLM 描述
    2. 从片段描述合成整体摘要
    """

    def __init__(
        self,
        model_service: Any,  # ModelService 实例
        scene_detector: VideoSceneDetector | None = None,
        slice_threshold: float = DEFAULT_SLICE_THRESHOLD_SECONDS,
        temp_dir: str | Path | None = None,
    ):
        self._model_service = model_service
        self._scene_detector = scene_detector or VideoSceneDetector()
        self._slice_threshold = slice_threshold
        self._temp_dir = Path(temp_dir) if temp_dir else Path(tempfile.gettempdir())

    def should_slice(self, video_path: str) -> bool:
        """判断是否需要切片"""
        duration = get_video_duration(video_path)
        return duration >= self._slice_threshold

    def describe_sliced(
        self,
        video_path: str,
        asset_format: str,
        angles: list[dict[str, Any]],
        system_prompt: str,
        prompt: str,
    ) -> dict[str, Any]:
        """切片描述主流程"""
        # 1. 检测场景
        scenes = self._scene_detector.detect(video_path)
        logger.info("视频场景检测完成: scenes=%d", len(scenes))

        # 2. 描述每个片段
        segment_descriptions = []
        for i, scene in enumerate(scenes):
            seg_desc = self._describe_segment(
                video_path, scene, i, asset_format, angles, system_prompt, prompt,
            )
            segment_descriptions.append(seg_desc)

        # 3. 合成整体摘要
        overall = self._synthesize_overall(segment_descriptions, angles)

        return {
            "整体": overall,
            "segments": segment_descriptions,
        }

    def _describe_segment(
        self,
        video_path: str,
        scene: SceneRange,
        index: int,
        asset_format: str,
        angles: list[dict[str, Any]],
        system_prompt: str,
        prompt: str,
    ) -> dict[str, Any]:
        """描述单个片段"""
        # 提取片段
        segment_path = str(self._temp_dir / f"seg_{index}_{Path(video_path).stem}.mp4")
        try:
            ffmpeg_extract_segment(video_path, scene.start_sec, scene.end_sec, segment_path)
        except RuntimeError:
            # ffmpeg 失败时用原视频（回退到整个视频）
            logger.warning("片段提取失败，使用原视频: seg=%d", index)
            segment_path = video_path

        # 构建带时间戳的 prompt
        time_prompt = f"[时间范围: {scene.start_sec:.1f}s - {scene.end_sec:.1f}s] "
        if prompt:
            time_prompt += prompt

        # 调 LLM 描述
        try:
            result = self._model_service._call_dashscope(
                # 需要传入合适的 provider context
                # 此处简化：使用 ModelService 的公共方法
                self._model_service._resolve_provider_context_for_asset_format(asset_format),
                system_prompt,
                time_prompt,
                asset_format,
                segment_path,
                self._model_service._resolve_model_name(
                    self._model_service._resolve_provider_context_for_asset_format(asset_format).model,
                    asset_format,
                ),
            )
            raw_text = result[0]  # (output_text, token_usage)
            cleaned = self._model_service._clean_llm_output(raw_text)
            parsed = json.loads(cleaned) if cleaned.startswith("{") else {"整体": {"text": cleaned, "tags": []}}
        except Exception as e:
            logger.error("片段描述失败: seg=%d, error=%s", index, e)
            parsed = {"整体": {"text": "", "tags": []}}

        # 清理临时文件
        if segment_path != video_path:
            Path(segment_path).unlink(missing_ok=True)

        return {
            "start_time": scene.start_sec,
            "end_time": scene.end_sec,
            **parsed,
        }

    def _synthesize_overall(
        self,
        segment_descriptions: list[dict[str, Any]],
        angles: list[dict[str, Any]],
    ) -> dict[str, Any]:
        """从片段描述合成整体摘要"""
        # 简单拼接所有片段的 整体 文本
        all_texts = []
        all_tags: list[str] = []
        for seg in segment_descriptions:
            overall = seg.get("整体", {})
            if isinstance(overall, dict):
                text = overall.get("text", "")
                if text:
                    all_texts.append(text)
                tags = overall.get("tags", [])
                if isinstance(tags, list):
                    all_tags.extend(tags)

        if not all_texts:
            return {"text": "", "tags": []}

        # 合并去重标签
        seen = set()
        unique_tags = []
        for tag in all_tags:
            if tag not in seen:
                seen.add(tag)
                unique_tags.append(tag)

        scene_count = len(segment_descriptions)
        summary = f"视频包含{scene_count}个场景：{'；'.join(all_texts)}"

        return {
            "text": summary[:500],  # 限制长度
            "tags": unique_tags[:20],
        }
```

- [ ] **Step 2: 写测试**

```python
# src/backend/tests/test_video_slice_describer.py
from __future__ import annotations

import json
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

from app.application.services.video_slice_describer import (
    VideoSliceDescriber,
    get_video_duration,
    ffmpeg_extract_segment,
)
from app.application.services.video_scene_detector import SceneRange


class VideoSliceDescriberTestCase(unittest.TestCase):
    def setUp(self):
        self.mock_model_service = MagicMock()
        self.mock_model_service._resolve_provider_context_for_asset_format.return_value.model = "qwen3.5-flash"
        self.mock_model_service._resolve_model_name.return_value = "qwen3.5-flash"
        self.mock_model_service._clean_llm_output.return_value = '{"整体":{"text":"测试场景","tags":["测试"]}}'
        self.mock_model_service._call_dashscope.return_value = (
            '{"整体":{"text":"测试场景","tags":["测试"]}}',
            None,
        )

    def test_should_slice_short_video_returns_false(self):
        with patch("app.application.services.video_slice_describer.get_video_duration", return_value=30.0):
            describer = VideoSliceDescriber(self.mock_model_service, slice_threshold=60.0)
            self.assertFalse(describer.should_slice("test.mp4"))

    def test_should_slice_long_video_returns_true(self):
        with patch("app.application.services.video_slice_describer.get_video_duration", return_value=120.0):
            describer = VideoSliceDescriber(self.mock_model_service, slice_threshold=60.0)
            self.assertTrue(describer.should_slice("test.mp4"))

    def test_ffmpeg_extract_segment_raises_on_missing_file(self):
        with self.assertRaises(RuntimeError):
            ffmpeg_extract_segment("/nonexistent.mp4", 0, 10, "/tmp/out.mp4")

    def test_get_video_duration_returns_zero_for_missing(self):
        duration = get_video_duration("/nonexistent.mp4")
        self.assertEqual(duration, 0.0)

    def test_describe_sliced_with_two_scenes(self):
        describer = VideoSliceDescriber(
            self.mock_model_service,
            slice_threshold=10.0,  # 低阈值触发切片
        )
        # mock detect 返回两个场景
        describer._scene_detector.detect = MagicMock(return_value=[
            SceneRange(0, 300, 0.0, 10.0),
            SceneRange(300, 600, 10.0, 20.0),
        ])

        result = describer.describe_sliced(
            "test.mp4",
            "视频",
            [{"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120}],
            "你是描述助手",
            "请描述",
        )

        self.assertIn("整体", result)
        self.assertIn("segments", result)
        self.assertEqual(len(result["segments"]), 2)
        self.assertEqual(result["segments"][0]["start_time"], 0.0)
        self.assertEqual(result["segments"][1]["start_time"], 10.0)

    @patch("app.application.services.video_slice_describer.get_video_duration", return_value=30.0)
    def test_should_slice_custom_threshold(self, mock_duration):
        describer = VideoSliceDescriber(self.mock_model_service, slice_threshold=20.0)
        self.assertTrue(describer.should_slice("test.mp4"))


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 3: 运行测试**

```bash
cd /d/GitRepository/AssetsLibrarySystem/src/backend
.venv/Scripts/python.exe -m pytest tests/test_video_slice_describer.py -v
```

Expected: 通过

- [ ] **Step 4: Commit**

```bash
cd /d/GitRepository/AssetsLibrarySystem
git add src/backend/app/application/services/video_slice_describer.py
git add src/backend/tests/test_video_slice_describer.py
git commit -m "feat(video): 添加 VideoSliceDescriber 切片描述模块"
```

---

### Task 3: Python - 修改 schema + model_service + angle_prompt_builder

**Files:**
- Modify: `src/backend/app/schemas/model.py`
- Modify: `src/backend/app/application/services/model_service.py`
- Modify: `src/backend/app/core/angle_prompt_builder.py`
- Modify: `src/backend/pyproject.toml`

- [ ] **Step 1: 修改 schema - 新增切片字段**

```python
# 在 ModelGenerateRequest 中新增字段
class ModelGenerateRequest(BaseModel):
    # ... 已有字段不变 ...
    subtype: str | None = Field(default=None, description="素材子类型")
    angles: list[AngleDef] | None = Field(default=None, description="角度定义列表")
    # 新增切片字段
    enable_slicing: bool = Field(default=False, description="是否启用视频切片")
    slice_threshold: float = Field(default=60.0, description="切片阈值（秒）")
    min_scene_len: int = Field(default=15, description="最小场景长度（帧）")
    adaptive_threshold: float = Field(default=3.0, description="场景检测自适应阈值")
```

- [ ] **Step 2: 修改 angle_prompt_builder - 新增合成摘要 prompt**

```python
# 在 angle_prompt_builder.py 末尾添加

def build_summary_prompt(
    asset_format: str,
    angles: list[dict[str, Any]],
    segment_descriptions: list[dict[str, Any]],
) -> str:
    """从所有片段的描述文本合成整体摘要"""
    asset_label = _ASSET_TYPE_LABELS.get(asset_format, f"{asset_format}素材")

    lines = [
        f"你是{asset_label}综合摘要助手。",
        "以下是一段视频的多个场景描述，请根据这些描述生成整体摘要。",
        "",
        "输出要求：",
        "- 综合所有场景，概括视频的整体内容和风格。",
        "- 只能输出 JSON，不要输出 Markdown、代码块或解释。",
        f'- JSON 必须包含且只包含以下字段： {", ".join(f'"{a["key"]}"' for a in angles)}',
        '- 每个字段是对象，包含 "text" 和 "tags"。',
        "- 每个 text 用中文，不超过 200 个中文字符。",
        "",
        "场景描述如下：",
    ]

    for i, seg in enumerate(segment_descriptions):
        seg_texts = []
        for angle in angles:
            key = angle["key"]
            if key in seg:
                text = seg[key].get("text", "") if isinstance(seg[key], dict) else ""
                if text:
                    seg_texts.append(f"{key}: {text}")
        if seg_texts:
            lines.append(f"场景{i+1} ({seg.get('start_time', 0):.1f}s-{seg.get('end_time', 0):.1f}s)：{'；'.join(seg_texts)}")

    lines.extend([
        "",
        "请输出 JSON：",
        "{",
    ])
    for i, a in enumerate(angles):
        comma = "," if i < len(angles) - 1 else ""
        lines.append(f'  "{a["key"]}": {{ "text": "...", "tags": ["..."] }}{comma}')
    lines.append("}")

    return "\n".join(lines)
```

- [ ] **Step 3: 修改 model_service.py - 处理切片视频**

```python
# 在 generate_text 方法中，处理视频切片逻辑

# 在文件头部新增导入
from app.application.services.video_slice_describer import VideoSliceDescriber
from app.core.angle_prompt_builder import build_summary_prompt

# 在 generate_text 方法中，处理视频类型 + 切片逻辑
# 在 mock 检查之后，live 调用之前：

# 视频切片处理
if payload.asset_format == "视频" and payload.enable_slicing and payload.angles:
    slice_describer = VideoSliceDescriber(
        model_service=self,
        slice_threshold=payload.slice_threshold,
        min_scene_len=payload.min_scene_len,
        adaptive_threshold=payload.adaptive_threshold,
        temp_dir=self._temp_dir,
    )

    if slice_describer.should_slice(payload.asset_path):
        logger.info("视频启用切片描述: %s", payload.asset_path)
        # 构建系统 prompt（使用角度定义）
        sys_prompt = self._resolve_system_prompt(payload.system_prompt, "")
        if not sys_prompt or sys_prompt == DEFAULT_SYSTEM_PROMPT:
            sys_prompt = build_system_prompt_from_angles(
                payload.asset_format,
                [a.model_dump() for a in payload.angles],
            )
        prompt = payload.prompt.strip() if payload.prompt else ""

        # 执行切片描述
        sliced_result = slice_describer.describe_sliced(
            video_path=payload.asset_path,
            asset_format=payload.asset_format,
            angles=[a.model_dump() for a in payload.angles],
            system_prompt=sys_prompt,
            prompt=prompt,
        )

        output_text = json.dumps(sliced_result, ensure_ascii=False)
        # 清理输出
        output_text = self._clean_llm_output(output_text)

        provider_context = self._resolve_provider_context_for_asset_format(payload.asset_format)
        return ModelGenerateResponse(
            provider_slot=DEFAULT_PROVIDER_SLOT,
            provider=provider_context.provider,
            model=provider_context.model,
            mode="live",
            output_text=output_text,
            system_prompt=sys_prompt,
            token_usage=None,
        )
```

- [ ] **Step 4: 修改 pyproject.toml 添加 scenedetect**

```toml
dependencies = [
  # ... 已有依赖 ...
  "scenedetect>=0.7.1",
]
```

- [ ] **Step 5: 运行所有 Python 测试**

```bash
cd /d/GitRepository/AssetsLibrarySystem/src/backend
.venv/Scripts/python.exe -m pytest -x -q
```

Expected: 全部通过（现有 45 个 + 新增）

- [ ] **Step 6: Commit**

```bash
cd /d/GitRepository/AssetsLibrarySystem
git add src/backend/app/schemas/model.py
git add src/backend/app/application/services/model_service.py
git add src/backend/app/core/angle_prompt_builder.py
git add src/backend/pyproject.toml
git commit -m "feat(video): 修改 model_service 支持视频切片描述"
```

---

### Task 4: C# - 传递切片配置到 Python

**Files:**
- Modify: `src/avalonia/.../Models/AngleProfileConfig.cs`
- Modify: `src/avalonia/.../Services/BackendApi/BackendApiContracts.cs`
- Modify: `src/avalonia/.../Services/Python/PythonModelService.cs`
- Modify: `src/avalonia/.../Services/AssetDescription/AssetDescriptionService.cs`

- [ ] **Step 1: AngleProfileConfig 新增切片配置**

在 `AngleProfileConfig.cs` 中新增 `VideoSlicingConfig` 和 `SubtypeProfile` 新增属性：

```csharp
public sealed record VideoSlicingConfig(
    bool Enabled = true,
    double SliceThresholdSeconds = 60.0,
    int MinSceneLength = 15,
    double AdaptiveThreshold = 3.0);

// 修改 SubtypeProfile 新增 Slicing 属性
public sealed record SubtypeProfile(
    string AssetType,
    string Subtype,
    string Label,
    IReadOnlyList<AngleDefinition> Angles,
    VideoSlicingConfig? Slicing = null);
```

- [ ] **Step 2: 修改 BackendApiContracts**

```csharp
public sealed record BackendModelGenerateRequest(
    string AssetFormat,
    string AssetPath,
    string? Prompt,
    string? SystemPrompt,
    bool MockResponse,
    string? Subtype = null,
    AngleDefinitionDto[]? Angles = null,
    bool EnableSlicing = false,
    double SliceThreshold = 60.0,
    int MinSceneLen = 15,
    double AdaptiveThreshold = 3.0);
```

- [ ] **Step 3: 修改 PythonModelService**

```csharp
// 在 BuildGenerateRequest 方法中新增切片字段
kw["enable_slicing"] = new PyInt(request.EnableSlicing ? 1 : 0);
kw["slice_threshold"] = new PyFloat(request.SliceThreshold);
kw["min_scene_len"] = new PyInt(request.MinSceneLen);
kw["adaptive_threshold"] = new PyFloat(request.AdaptiveThreshold);
```

- [ ] **Step 4: 修改 AssetDescriptionService**

```csharp
// 在 DescribeAsync 方法中，构建请求时加入切片配置
var profile = AngleProfileManager.GetProfile(asset.AssetType, subtype);

// 确定切片配置
var slicing = profile.Slicing;
bool enableSlicing = slicing?.Enabled == true;
double sliceThreshold = slicing?.SliceThresholdSeconds ?? 60.0;

// 构建请求时传入
var request = new BackendModelGenerateRequest(
    // ... 已有字段 ...
    Subtype: subtype,
    Angles: angleDtos,
    EnableSlicing: enableSlicing,
    SliceThreshold: sliceThreshold,
    MinSceneLen: 15,
    AdaptiveThreshold: 3.0);
```

- [ ] **Step 5: 构建并运行 C# 测试**

```bash
cd /d/GitRepository/AssetsLibrarySystem/src/avalonia
dotnet build -c Debug
dotnet test --no-build -c Debug
```

Expected: 0 错误，全部测试通过

- [ ] **Step 6: Commit**

```bash
cd /d/GitRepository/AssetsLibrarySystem
git add src/avalonia/...
git commit -m "feat(video): C# 端传递视频切片配置到 Python"
```

---

### Task 5: 安装依赖 + 最终验证

- [ ] **Step 1: 安装 scenedetect 到 .venv**

```bash
cd /d/GitRepository/AssetsLibrarySystem/src/backend
.venv/Scripts/python.exe -m pip install scenedetect -q
```

- [ ] **Step 2: 运行所有 Python 测试**

```bash
.venv/Scripts/python.exe -m pytest -x -q
```

Expected: 全部通过

- [ ] **Step 3: 运行所有 C# 测试**

```bash
cd /d/GitRepository/AssetsLibrarySystem/src/avalonia
dotnet test -c Debug
```

Expected: 全部通过

- [ ] **Step 4: 运行桌面应用验证启动**

```bash
cd /d/GitRepository/AssetsLibrarySystem/src/avalonia/AssetsLibrarySystem.Avalonia
timeout 12 dotnet run --no-build -c Debug
```

Expected: Python 引擎初始化正常，应用启动成功

- [ ] **Step 5: 最终提交**

```bash
cd /d/GitRepository/AssetsLibrarySystem
git add -A
git commit -m "feat: 视频切片描述功能完整实现"
```