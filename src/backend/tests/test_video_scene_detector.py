"""VideoSceneDetector 单元测试"""

from __future__ import annotations

import shutil
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
        if shutil.which("ffmpeg") is None:
            self.skipTest("ffmpeg 不可用，跳过依赖真实视频的检测测试")
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

    def test_split_long_scenes_never_exceeds_max(self):
        detector = VideoSceneDetector(max_seconds=40.0)
        scenes = [
            SceneRange(0, 1000, 0.0, 100.0),   # 100s → 3 段
            SceneRange(1000, 1400, 100.0, 140.0),  # 40s → 不切
            SceneRange(1400, 1500, 140.0, 150.0),  # 10s → 不切
            SceneRange(1500, 2500, 150.0, 250.0),  # 100s → 3 段
        ]
        result = detector._split_long_scenes(scenes)

        # 覆盖完整且连续
        self.assertEqual(result[0].start_sec, 0.0)
        for prev, cur in zip(result, result[1:]):
            self.assertAlmostEqual(prev.end_sec, cur.start_sec)
        self.assertEqual(result[-1].end_sec, 250.0)

        # 每段不超过 max_seconds
        for scene in result:
            self.assertLessEqual(scene.end_sec - scene.start_sec, 40.0 + 1e-9)

        # 段数 = ceil(100/40)*2 + 2 个不切段 = 3 + 2 + 3 = 8
        self.assertEqual(len(result), 8)

        # 首段 start_frame 与原场景一致，末段 end_frame 与原场景一致
        self.assertEqual(result[0].start_frame, 0)
        self.assertEqual(result[-1].end_frame, 2500)
        # 时间区间与原始区间一一对应（不重叠、不漏）
        self.assertEqual(
            [(s.start_sec, s.end_sec) for s in result],
            [(0.0, 100 / 3), (100 / 3, 200 / 3), (200 / 3, 100.0),
             (100.0, 140.0), (140.0, 150.0),
             (150.0, 150 + 100 / 3), (150 + 100 / 3, 150 + 200 / 3), (150 + 200 / 3, 250.0)],
        )

    def test_split_long_scenes_disabled_by_default(self):
        detector = VideoSceneDetector()
        scenes = [SceneRange(0, 3000, 0.0, 100.0)]
        self.assertEqual(detector._split_long_scenes(scenes), scenes)

    def test_detect_applies_max_seconds(self):
        detector = VideoSceneDetector(
            adaptive_threshold=3.0,
            min_scene_len=15,
            min_seconds=1.0,
            max_seconds=2.0,
        )
        scenes = detector.detect(self.test_video)
        self.assertGreater(len(scenes), 0)
        for scene in scenes:
            self.assertLessEqual(scene.end_sec - scene.start_sec, 2.0 + 1e-6)

    def test_split_long_scenes_applied_to_single_scene_video(self):
        """未检测到场景边界（整视频单场景）时同样应用 max_seconds 等分。"""
        # 5 秒测试视频 + max_seconds=2 → 应等分为 3 段（ceil(5/2)=3）
        detector = VideoSceneDetector(
            adaptive_threshold=99.0,  # 阈值极高 → 检测不到边界
            min_scene_len=15,
            min_seconds=1.0,
            max_seconds=2.0,
        )
        scenes = detector.detect(self.test_video)
        self.assertGreaterEqual(len(scenes), 3)
        for scene in scenes:
            self.assertLessEqual(scene.end_sec - scene.start_sec, 2.0 + 1e-6)
        # 覆盖完整
        self.assertAlmostEqual(scenes[0].start_sec, 0.0)
        self.assertAlmostEqual(scenes[-1].end_sec, 5.0)


if __name__ == "__main__":
    unittest.main()