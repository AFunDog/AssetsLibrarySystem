"""使用 PySceneDetect 检测视频场景边界。

封装 PySceneDetect 的 AdaptiveDetector，返回场景时间范围列表。
"""

from __future__ import annotations

import logging
from dataclasses import dataclass
from pathlib import Path

from scenedetect import AdaptiveDetector, SceneManager, open_video

logger = logging.getLogger(__name__)


@dataclass(slots=True)
class SceneRange:
    """一个场景的时间范围"""

    start_frame: int
    end_frame: int
    start_sec: float
    end_sec: float


class VideoSceneDetector:
    """使用 PySceneDetect AdaptiveDetector 检测视频场景边界。

    Args:
        adaptive_threshold: 自适应检测阈值，越高越不敏感。默认 3.0。
        min_scene_len: 最小场景长度（帧），低于此长度的场景会被 PySceneDetect 合并。默认 15。
        min_seconds: 最小场景时长（秒），低于此长度的相邻场景会被合并，避免片段过短。默认 5.0。
    """

    def __init__(
        self,
        adaptive_threshold: float = 3.0,
        min_scene_len: int = 15,
        min_seconds: float = 5.0,
    ) -> None:
        self._adaptive_threshold = adaptive_threshold
        self._min_scene_len = min_scene_len
        self._min_seconds = min_seconds

    def detect(self, video_path: str | Path) -> list[SceneRange]:
        """检测视频中的场景，返回场景时间范围列表。

        Args:
            video_path: 视频文件路径。

        Returns:
            SceneRange 列表，按时间顺序排列。
            如果未检测到场景边界，返回整个视频作为一个场景。
        """
        video = open_video(str(video_path))
        manager = SceneManager()
        manager.add_detector(
            AdaptiveDetector(
                adaptive_threshold=self._adaptive_threshold,
                min_scene_len=self._min_scene_len,
            )
        )
        manager.detect_scenes(video)
        scene_list = manager.get_scene_list()

        if not scene_list:
            logger.info("未检测到场景边界，将整个视频作为一个场景处理")
            total_frames = video.duration.frame_num
            fps = float(video.frame_rate or 30.0)
            return [
                SceneRange(
                    start_frame=0,
                    end_frame=total_frames,
                    start_sec=0.0,
                    end_sec=total_frames / fps,
                )
            ]

        scenes = [
            SceneRange(
                start_frame=start.frame_num,
                end_frame=end.frame_num,
                start_sec=start.seconds,
                end_sec=end.seconds,
            )
            for start, end in scene_list
        ]

        # 合并过短的相邻场景
        scenes = self._merge_short_scenes(scenes)
        logger.info("场景检测完成: raw=%d, merged=%d", len(scene_list), len(scenes))
        return scenes

    def _merge_short_scenes(self, scenes: list[SceneRange]) -> list[SceneRange]:
        """合并时长低于 min_seconds 的相邻场景"""
        if len(scenes) <= 1:
            return scenes

        merged: list[SceneRange] = []
        for scene in scenes:
            duration = scene.end_sec - scene.start_sec

            if not merged:
                # 第一个场景，直接加入
                merged.append(scene)
                continue

            if duration >= self._min_seconds:
                # 当前场景够长，直接加入
                merged.append(scene)
                continue

            # 当前场景过短，合并到上一个
            last = merged[-1]
            merged[-1] = SceneRange(
                start_frame=last.start_frame,
                end_frame=scene.end_frame,
                start_sec=last.start_sec,
                end_sec=scene.end_sec,
            )

        # 如果合并后第一个场景仍然过短，与第二个场景合并
        if len(merged) >= 2:
            first = merged[0]
            if first.end_sec - first.start_sec < self._min_seconds:
                second = merged[1]
                merged[1] = SceneRange(
                    start_frame=first.start_frame,
                    end_frame=second.end_frame,
                    start_sec=first.start_sec,
                    end_sec=second.end_sec,
                )
                merged.pop(0)

        # 如果合并后最后一个场景仍然过短，与倒数第二个场景合并
        if len(merged) >= 2:
            last = merged[-1]
            if last.end_sec - last.start_sec < self._min_seconds:
                prev = merged[-2]
                merged[-2] = SceneRange(
                    start_frame=prev.start_frame,
                    end_frame=last.end_frame,
                    start_sec=prev.start_sec,
                    end_sec=last.end_sec,
                )
                merged.pop()

        return merged