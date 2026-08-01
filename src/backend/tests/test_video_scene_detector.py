"""VideoSceneDetector 单元测试"""

from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path

from app.application.services.video_scene_detector import (
    SceneDetectionCancelled,
    SceneRange,
    VideoSceneDetector,
)


class VideoSceneDetectorTestCase(unittest.TestCase):
    """VideoSceneDetector 单元测试"""

    def setUp(self):
        self.detector = VideoSceneDetector(
            adaptive_threshold=3.0,
            min_scene_len=15,
        )
        self.test_video = self._create_test_video()

    def _create_test_video(self) -> str:
        """用 ffmpeg 生成一个包含多场景的测试视频"""
        path = Path(tempfile.gettempdir()) / "test_video_scene_detect.mp4"
        if path.exists():
            return str(path)

        # 生成 5 个不同颜色的 1 秒片段，拼接成 5 秒视频
        cmd = [
            "ffmpeg",
            "-y",
            "-f",
            "lavfi",
            "-i",
            "color=c=red:s=320x240:d=1",
            "-f",
            "lavfi",
            "-i",
            "color=c=green:s=320x240:d=1",
            "-f",
            "lavfi",
            "-i",
            "color=c=blue:s=320x240:d=1",
            "-f",
            "lavfi",
            "-i",
            "color=c=white:s=320x240:d=1",
            "-f",
            "lavfi",
            "-i",
            "color=c=black:s=320x240:d=1",
            "-filter_complex",
            "[0:v][1:v][2:v][3:v][4:v]concat=n=5:v=1:a=0",
            "-t",
            "5",
            str(path),
        ]
        result = subprocess.run(cmd, capture_output=True, text=True)
        if result.returncode != 0:
            self.fail(f"创建测试视频失败: {result.stderr}")
        return str(path)

    def test_detect_returns_scene_ranges(self):
        scenes = self.detector.detect(self.test_video)
        self.assertIsInstance(scenes, list)
        self.assertGreater(len(scenes), 0)
        for scene in scenes:
            self.assertIsInstance(scene, SceneRange)
            self.assertGreaterEqual(scene.start_frame, 0)
            self.assertGreater(scene.end_frame, scene.start_frame)
            self.assertGreaterEqual(scene.end_sec, scene.start_sec)

    def test_detect_scene_has_seconds(self):
        scenes = self.detector.detect(self.test_video)
        for scene in scenes:
            self.assertGreaterEqual(scene.start_sec, 0.0)
            self.assertGreater(scene.end_sec, scene.start_sec)

    def test_detect_scene_has_frame_numbers(self):
        scenes = self.detector.detect(self.test_video)
        for scene in scenes:
            self.assertIsInstance(scene.start_frame, int)
            self.assertIsInstance(scene.end_frame, int)
            self.assertGreater(scene.end_frame, scene.start_frame)

    def test_detect_raises_on_missing_file(self):
        with self.assertRaises(Exception):
            self.detector.detect("/nonexistent/video.mp4")

    def test_detect_reports_progress_and_matches_full_detect(self):
        progress: list[int] = []
        scenes = self.detector.detect(self.test_video, progress_callback=lambda p: progress.append(p) or True)

        # 进度递增且最终为 100
        self.assertGreater(len(progress), 0)
        self.assertEqual(progress, sorted(progress))
        self.assertEqual(progress[-1], 100)

        # 分块检测结果与一次性检测一致
        full = self.detector.detect(self.test_video)
        self.assertEqual([(s.start_sec, s.end_sec) for s in scenes],
                         [(s.start_sec, s.end_sec) for s in full])

    def test_detect_cancelled_when_callback_returns_false(self):
        calls = {"count": 0}

        def cancel_first(percent: int) -> bool:
            calls["count"] += 1
            # 第一次回调即返回 False 请求取消（5 秒测试视频只有 1 块）
            return calls["count"] != 1

        with self.assertRaises(SceneDetectionCancelled):
            self.detector.detect(self.test_video, progress_callback=cancel_first)

    def test_scene_range_dataclass(self):
        scene = SceneRange(start_frame=0, end_frame=300, start_sec=0.0, end_sec=10.0)
        self.assertEqual(scene.start_frame, 0)
        self.assertEqual(scene.end_frame, 300)
        self.assertEqual(scene.start_sec, 0.0)
        self.assertEqual(scene.end_sec, 10.0)


if __name__ == "__main__":
    unittest.main()