"""使用 PySceneDetect 检测视频场景边界。

封装 PySceneDetect 的 AdaptiveDetector，返回场景时间范围列表。
"""

from __future__ import annotations

import logging
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from scenedetect import AdaptiveDetector, SceneManager, open_video

logger = logging.getLogger(__name__)


class SceneDetectionCancelled(Exception):
    """场景检测被取消（进度回调返回 False 时抛出）"""


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
        max_seconds: 最大场景时长（秒），超过此长度的场景会被等分成多段，
            避免出现超长片段（如片尾曲、长对话场景）。None 表示不限制。默认 None。
    """

    def __init__(
        self,
        adaptive_threshold: float = 3.0,
        min_scene_len: int = 15,
        min_seconds: float = 5.0,
        max_seconds: float | None = None,
    ) -> None:
        self._adaptive_threshold = adaptive_threshold
        self._min_scene_len = min_scene_len
        self._min_seconds = min_seconds
        self._max_seconds = max_seconds

    def detect(
        self,
        video_path: str | Path,
        range_start: float | None = None,
        range_end: float | None = None,
        progress_callback: Callable[[int], bool] | None = None,
    ) -> list[SceneRange]:
        """检测视频中的场景，返回场景时间范围列表。

        Args:
            video_path: 视频文件路径。
            range_start: 可选，只保留该时间点之后的场景（秒）。
            range_end: 可选，只保留该时间点之前的场景（秒）。
            progress_callback: 可选，每完成一块检测后回调进度百分比（0-100）。
                回调返回 False 表示请求取消，检测会抛出 SceneDetectionCancelled。

        Returns:
            SceneRange 列表，按时间顺序排列。
            如果未检测到场景边界，返回整个视频作为一个场景。
        """
        video = open_video(str(video_path))
        try:
            manager = SceneManager()
            manager.add_detector(
                AdaptiveDetector(
                    adaptive_threshold=self._adaptive_threshold,
                    min_scene_len=self._min_scene_len,
                )
            )

            # 分块检测以报告进度：每块约 15 秒，块数上限 50（保证至少 2% 的进度粒度）
            # 防御 duration 为 None（异常媒体文件），此时退化为单块全量检测
            if video.duration is None:
                logger.warning("无法读取视频时长，退化为单块全量检测: path=%s", video_path)
                total_seconds = 0.0
            else:
                total_seconds = float(video.duration.seconds)
            blocks = max(1, min(int(total_seconds / 15), 50)) if total_seconds > 0 else 1
            for index in range(blocks):
                end_time = total_seconds * (index + 1) / blocks
                manager.detect_scenes(video, end_time=end_time)
                if progress_callback is not None:
                    percent = int((index + 1) / blocks * 100)
                    if progress_callback(percent) is False:
                        raise SceneDetectionCancelled(
                            f"SceneDetectionCancelled: 场景检测已取消: path={video_path}"
                        )

            scene_list = manager.get_scene_list()

            if not scene_list:
                logger.info("未检测到场景边界，将整个视频作为一个场景处理")
                if video.duration is None:
                    logger.warning("视频时长不可用，无法确定总帧数，跳过场景检测: path=%s", video_path)
                    return []
                total_frames = video.duration.frame_num
                fps = float(video.frame_rate or 30.0)
                single = [
                    SceneRange(
                        start_frame=0,
                        end_frame=total_frames,
                        start_sec=0.0,
                        end_sec=total_frames / fps,
                    )
                ]
                # 同样应用 max_seconds 等分兜底：无边界的长视频（片尾曲/长对话）
                # 恰好是最需要等分的情形，不能绕过
                if self._max_seconds is not None:
                    single = self._split_long_scenes(single)
                return single

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
            # 裁剪到指定时间范围（用于「在特定时间范围内描述」）
            if range_start is not None or range_end is not None:
                scenes = self._clip_to_range(scenes, range_start, range_end)
            # 等分超长场景（max_seconds 兜底，最后执行确保任何路径输出都无超长段）
            if self._max_seconds is not None:
                scenes = self._split_long_scenes(scenes)
            logger.info("场景检测完成: raw=%d, merged=%d", len(scene_list), len(scenes))
            return scenes
        finally:
            close = getattr(video, "close", None)
            if callable(close):
                try:
                    close()
                except Exception:
                    logger.debug("关闭视频资源失败: path=%s", video_path, exc_info=True)

    def _clip_to_range(
        self,
        scenes: list[SceneRange],
        range_start: float | None,
        range_end: float | None,
    ) -> list[SceneRange]:
        """把场景列表裁剪到 [range_start, range_end] 时间范围内（秒）。"""
        clipped: list[SceneRange] = []
        for scene in scenes:
            start = max(scene.start_sec, range_start if range_start is not None else scene.start_sec)
            end = min(scene.end_sec, range_end if range_end is not None else scene.end_sec)
            if end > start:
                clipped.append(
                    SceneRange(
                        start_frame=scene.start_frame,
                        end_frame=scene.end_frame,
                        start_sec=start,
                        end_sec=end,
                    )
                )
        if not clipped and scenes:
            # 范围内没有任何场景边界：把范围本身作为一个片段返回
            start = range_start if range_start is not None else 0.0
            end = range_end if range_end is not None else max((s.end_sec for s in scenes), default=start)
            if end > start:
                clipped.append(SceneRange(0, 0, start, end))
        return clipped

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

    def _split_long_scenes(self, scenes: list[SceneRange]) -> list[SceneRange]:
        """把时长超过 max_seconds 的场景等分成多段，每段时长不超过 max_seconds。

        等分而非按检测阈值切分：不引入新的边界检测（超长场景通常是语义连续
        的长段落，如片尾曲、长对话，检测器无法找到内部边界），等分能保证
        每段长度可控，同时保持时间覆盖完整、无重叠。
        """
        if self._max_seconds is None or self._max_seconds <= 0:
            return scenes

        result: list[SceneRange] = []
        for scene in scenes:
            duration = scene.end_sec - scene.start_sec
            if duration <= self._max_seconds:
                result.append(scene)
                continue

            part_count = int(math.ceil(duration / self._max_seconds))
            part_seconds = duration / part_count
            frame_span = scene.end_frame - scene.start_frame
            for index in range(part_count):
                start_sec = scene.start_sec + index * part_seconds
                end_sec = scene.start_sec + (index + 1) * part_seconds
                start_frame = scene.start_frame + round(frame_span * index / part_count)
                if index == part_count - 1:
                    # 末尾段使用原始结束帧，避免浮点误差导致漏帧
                    end_frame = scene.end_frame
                else:
                    end_frame = scene.start_frame + round(frame_span * (index + 1) / part_count)
                result.append(
                    SceneRange(
                        start_frame=start_frame,
                        end_frame=end_frame,
                        start_sec=start_sec,
                        end_sec=end_sec,
                    )
                )
            logger.info(
                "场景超过 max_seconds=%.1f，等分为 %d 段: %.1f-%.1fs",
                self._max_seconds,
                part_count,
                scene.start_sec,
                scene.end_sec,
            )
        return result