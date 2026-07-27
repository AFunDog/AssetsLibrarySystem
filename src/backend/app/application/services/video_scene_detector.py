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
        min_scene_len: 最小场景长度（帧），低于此长度的场景会被合并。默认 15。
    """

    def __init__(
        self,
        adaptive_threshold: float = 3.0,
        min_scene_len: int = 15,
    ) -> None:
        self._adaptive_threshold = adaptive_threshold
        self._min_scene_len = min_scene_len

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

        return [
            SceneRange(
                start_frame=start.frame_num,
                end_frame=end.frame_num,
                start_sec=start.seconds,
                end_sec=end.seconds,
            )
            for start, end in scene_list
        ]