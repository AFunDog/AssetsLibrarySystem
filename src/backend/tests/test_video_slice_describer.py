"""VideoSliceDescriber 单元测试"""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch

from app.application.services.video_slice_describer import (
    VideoSliceDescriber,
    get_video_duration,
    ffmpeg_extract_segment,
)
from app.application.services.video_scene_detector import SceneRange


class VideoSliceDescriberTestCase(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        # 模拟异步 LLM 回调
        self.mock_llm = AsyncMock()
        self.mock_llm.return_value = (
            '{"整体":{"text":"测试场景描述","tags":["测试"]}}',
            None,
        )
        self.temp_dir = Path(tempfile.gettempdir())

    def test_should_slice_short_video_returns_false(self):
        with patch(
            "app.application.services.video_slice_describer.get_video_duration",
            return_value=30.0,
        ):
            describer = VideoSliceDescriber(
                self.mock_llm,
                slice_threshold=60.0,
            )
            self.assertFalse(describer.should_slice("test.mp4"))

    def test_should_slice_long_video_returns_true(self):
        with patch(
            "app.application.services.video_slice_describer.get_video_duration",
            return_value=120.0,
        ):
            describer = VideoSliceDescriber(
                self.mock_llm,
                slice_threshold=60.0,
            )
            self.assertTrue(describer.should_slice("test.mp4"))

    def test_should_slice_exact_threshold(self):
        with patch(
            "app.application.services.video_slice_describer.get_video_duration",
            return_value=60.0,
        ):
            describer = VideoSliceDescriber(
                self.mock_llm,
                slice_threshold=60.0,
            )
            self.assertTrue(describer.should_slice("test.mp4"))

    def test_ffmpeg_extract_segment_raises_on_missing_file(self):
        with self.assertRaises(RuntimeError):
            ffmpeg_extract_segment("/nonexistent.mp4", 0, 10, "/tmp/out.mp4")

    def test_ffmpeg_extract_segment_raises_on_zero_duration(self):
        with self.assertRaises(ValueError):
            ffmpeg_extract_segment("test.mp4", 5, 5, "/tmp/out.mp4")

    def test_get_video_duration_returns_zero_for_missing(self):
        duration = get_video_duration("/nonexistent.mp4")
        self.assertEqual(duration, 0.0)

    async def test_describe_sliced_with_two_scenes(self):
        describer = VideoSliceDescriber(
            self.mock_llm,
            slice_threshold=10.0,
        )
        # Mock detect 返回两个场景
        describer._scene_detector.detect = MagicMock(
            return_value=[
                SceneRange(0, 300, 0.0, 10.0),
                SceneRange(300, 600, 10.0, 20.0),
            ]
        )
        # Mock get_video_duration
        with patch(
            "app.application.services.video_slice_describer.get_video_duration",
            return_value=30.0,
        ):
            result = await describer.describe_sliced(
                "test.mp4",
                "视频",
                [{"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120}],
                "你是描述助手",
                "请描述",
            )

        self.assertIn("整体", result)
        self.assertIn("segments", result)
        self.assertEqual(len(result["segments"]), 2)
        # 由于重叠，第一段起点 0.0（裁剪到边界），第二段起点 9.5（10-0.5）
        self.assertEqual(result["segments"][0]["start_time"], 0.0)
        self.assertEqual(result["segments"][1]["start_time"], 9.5)
        self.assertIn("测试场景描述", result["整体"]["text"])

    async def test_describe_sliced_single_scene(self):
        """只有一个场景时，不走切片逻辑"""
        describer = VideoSliceDescriber(
            self.mock_llm,
            slice_threshold=10.0,
        )
        describer._scene_detector.detect = MagicMock(
            return_value=[
                SceneRange(0, 900, 0.0, 30.0),
            ]
        )

        result = await describer.describe_sliced(
            "test.mp4",
            "视频",
            [{"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120}],
            "你是描述助手",
            "请描述",
        )

        self.assertIn("整体", result)
        self.assertIn("segments", result)
        self.assertEqual(len(result["segments"]), 1)

    async def test_describe_sliced_llm_failure_raises(self):
        """LLM 调用失败时，向上抛出 RuntimeError 而非返回空描述"""
        failing_llm = AsyncMock(side_effect=RuntimeError("LLM failed"))
        describer = VideoSliceDescriber(
            failing_llm,
            slice_threshold=10.0,
        )
        describer._scene_detector.detect = MagicMock(
            return_value=[
                SceneRange(0, 300, 0.0, 10.0),
            ]
        )

        with self.assertRaises(RuntimeError):
            await describer.describe_sliced(
                "test.mp4",
                "视频",
                [{"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120}],
                "你是描述助手",
                "请描述",
            )

    def test_synthesize_overall_merges_tags(self):
        """合成整体摘要时，标签去重合并"""
        describer = VideoSliceDescriber(self.mock_llm)
        segments = [
            {
                "start_time": 0.0,
                "end_time": 10.0,
                "整体": {"text": "场景A", "tags": ["城市", "白天"]},
            },
            {
                "start_time": 10.0,
                "end_time": 20.0,
                "整体": {"text": "场景B", "tags": ["城市", "夜晚"]},
            },
        ]

        result = describer._synthesize_overall(segments, [])

        self.assertIn("场景A", result["text"])
        self.assertIn("场景B", result["text"])
        self.assertIn("城市", result["tags"])
        self.assertIn("白天", result["tags"])
        self.assertIn("夜晚", result["tags"])
        # 城市 只出现一次
        self.assertEqual(
            [t for t in result["tags"] if t == "城市"], ["城市"]
        )

    def test_synthesize_overall_empty_segments(self):
        """没有片段时返回空"""
        describer = VideoSliceDescriber(self.mock_llm)
        result = describer._synthesize_overall([], [])
        self.assertEqual(result["text"], "")
        self.assertEqual(result["tags"], [])

    def test_synthesize_overall_truncates_long_text(self):
        """超长文本截断"""
        describer = VideoSliceDescriber(self.mock_llm)
        long_text = "场景" * 300  # 600 chars
        segments = [
            {
                "start_time": 0.0,
                "end_time": 10.0,
                "整体": {"text": long_text, "tags": []},
            }
        ]

        result = describer._synthesize_overall(segments, [])
        self.assertLessEqual(len(result["text"]), 500)

    def test_build_skeleton_creates_empty_angle_fields(self):
        """build_skeleton：按场景时间点生成骨架，角度字段为空"""
        scenes = [
            SceneRange(0, 300, 0.0, 10.0),
            SceneRange(300, 600, 10.0, 20.0),
        ]
        angles = [
            {"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120},
            {"key": "场景", "label": "场景环境", "prompt": "描述场景", "max_length": 100},
        ]

        skeleton = VideoSliceDescriber.build_skeleton(scenes, angles)

        self.assertEqual(skeleton["整体"], {"text": "", "tags": []})
        self.assertEqual(len(skeleton["segments"]), 2)
        self.assertEqual(skeleton["segments"][0]["start_time"], 0.0)
        self.assertEqual(skeleton["segments"][0]["end_time"], 10.0)
        self.assertEqual(skeleton["segments"][1]["start_time"], 10.0)
        self.assertEqual(skeleton["segments"][1]["end_time"], 20.0)
        # 每个片段都含角度占位
        self.assertEqual(skeleton["segments"][0]["整体"], {"text": "", "tags": []})
        self.assertEqual(skeleton["segments"][0]["场景"], {"text": "", "tags": []})

    async def test_describe_sliced_with_external_scenes_skips_detect(self):
        """外部时间点：跳过场景检测，按给定时间点描述（单片段也不退化整体）"""
        describer = VideoSliceDescriber(
            self.mock_llm,
            slice_threshold=10.0,
        )
        describer._scene_detector.detect = MagicMock(
            side_effect=AssertionError("不应触发场景检测")
        )

        with patch(
            "app.application.services.video_slice_describer.get_video_duration",
            return_value=30.0,
        ):
            result = await describer.describe_sliced(
                "test.mp4",
                "视频剪辑",
                [{"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120}],
                "你是描述助手",
                "请描述",
                external_scenes=[SceneRange(0, 0, 5.0, 8.0)],
            )

        # 单片段也走片段描述路径（不退化 _describe_single）
        self.assertIn("整体", result)
        self.assertEqual(len(result["segments"]), 1)
        self.assertEqual(result["segments"][0]["start_time"], 4.5)  # 5.0 - 0.5 重叠
        self.assertEqual(result["segments"][0]["end_time"], 8.5)  # 8.0 + 0.5 重叠


if __name__ == "__main__":
    unittest.main()