"""VideoSliceDescriber 单元测试"""

from __future__ import annotations

import json
import tempfile
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
        # 模拟 LLM 回调
        self.mock_llm = MagicMock()
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

    def test_describe_sliced_with_two_scenes(self):
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
        self.assertIn("测试场景描述", result["整体"]["text"])

    def test_describe_sliced_single_scene(self):
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

        result = describer.describe_sliced(
            "test.mp4",
            "视频",
            [{"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120}],
            "你是描述助手",
            "请描述",
        )

        self.assertIn("整体", result)
        self.assertIn("segments", result)
        self.assertEqual(len(result["segments"]), 1)

    def test_describe_sliced_llm_failure_uses_empty(self):
        """LLM 调用失败时，使用空描述"""
        failing_llm = MagicMock(side_effect=RuntimeError("LLM failed"))
        describer = VideoSliceDescriber(
            failing_llm,
            slice_threshold=10.0,
        )
        describer._scene_detector.detect = MagicMock(
            return_value=[
                SceneRange(0, 300, 0.0, 10.0),
            ]
        )

        result = describer.describe_sliced(
            "test.mp4",
            "视频",
            [{"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120}],
            "你是描述助手",
            "请描述",
        )

        self.assertIn("整体", result)
        self.assertEqual(result["整体"]["text"], "")

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


if __name__ == "__main__":
    unittest.main()