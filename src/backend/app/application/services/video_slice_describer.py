"""视频切片描述器。

两阶段流程：
1. 检测场景 → 提取每个片段 → 调 LLM 描述每个片段
2. 从片段描述合成整体摘要

C# 端负责子类型检测和角度配置，Python 端只负责切片和 LLM 调用。
"""

from __future__ import annotations

import json
import logging
import subprocess
import tempfile
from collections.abc import Awaitable
from pathlib import Path
from typing import Any, Callable

from app.application.services.video_scene_detector import (
    SceneRange,
    VideoSceneDetector,
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
    """用 ffmpeg 快速提取视频片段（-c copy 不重编码）。"""
    duration = end_sec - start_sec
    if duration <= 0:
        raise ValueError(f"片段时长必须大于 0: {duration}")
    cmd = [
        "ffmpeg",
        "-y",
        "-ss",
        str(start_sec),
        "-i",
        video_path,
        "-t",
        str(duration),
        "-c",
        "copy",
        "-avoid_negative_ts",
        "make_zero",
        str(output_path),
    ]
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        logger.warning(
            "ffmpeg 提取片段失败: returncode=%d, stderr=%s",
            result.returncode,
            result.stderr[:300],
        )
        raise RuntimeError(f"ffmpeg 提取片段失败: {result.stderr[:200]}")
    return output_path


def get_video_duration(video_path: str) -> float:
    """用 ffprobe 获取视频时长（秒）。"""
    cmd = [
        "ffprobe",
        "-v",
        "error",
        "-show_entries",
        "format=duration",
        "-of",
        "default=noprint_wrappers=1:nokey=1",
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
    """视频切片描述器。

    两阶段流程：
    1. 检测场景 → 提取片段 → 调 LLM 描述每个片段
    2. 从片段描述合成整体摘要

    Args:
        call_llm: 调用 LLM 的异步回调函数，接收 (system_prompt, prompt, asset_format, asset_path)
                  返回 (output_text, token_usage) 的协程。
        scene_detector: 场景检测器实例。
        slice_threshold: 切片阈值（秒），超过此值时长的视频才启用切片。
        temp_dir: 临时文件目录。
    """

    def __init__(
        self,
        call_llm: Callable[..., Awaitable[tuple[str, Any]]],
        scene_detector: VideoSceneDetector | None = None,
        slice_threshold: float = DEFAULT_SLICE_THRESHOLD_SECONDS,
        temp_dir: str | Path | None = None,
    ) -> None:
        self._call_llm = call_llm
        self._scene_detector = scene_detector or VideoSceneDetector()
        self._slice_threshold = slice_threshold
        self._temp_dir = Path(temp_dir) if temp_dir else Path(tempfile.gettempdir())

    def should_slice(self, video_path: str) -> bool:
        """判断视频是否需要切片。"""
        duration = get_video_duration(video_path)
        return duration >= self._slice_threshold

    async def describe_sliced(
        self,
        video_path: str,
        asset_format: str,
        angles: list[dict[str, Any]],
        system_prompt: str,
        prompt: str,
    ) -> dict[str, Any]:
        """切片描述主流程。

        Args:
            video_path: 视频文件路径。
            asset_format: 素材格式（如 "视频"）。
            angles: 角度定义列表。
            system_prompt: 系统提示词。
            prompt: 用户提示词。

        Returns:
            {"整体": {...}, "segments": [{...}, ...]}
        """
        # 1. 检测场景
        scenes = self._scene_detector.detect(video_path)
        logger.info("视频场景检测完成: scenes=%d, path=%s", len(scenes), video_path)

        if len(scenes) <= 1:
            logger.info("视频只有一个场景，无需切片，直接描述")
            return await self._describe_single(video_path, asset_format, angles, system_prompt, prompt)

        # 2. 描述每个片段
        segment_descriptions = []
        for i, scene in enumerate(scenes):
            seg_desc = await self._describe_segment(
                video_path=video_path,
                scene=scene,
                index=i,
                asset_format=asset_format,
                angles=angles,
                system_prompt=system_prompt,
                prompt=prompt,
            )
            segment_descriptions.append(seg_desc)

        # 3. 合成整体摘要
        overall = self._synthesize_overall(segment_descriptions, angles)

        return {
            "整体": overall,
            "segments": segment_descriptions,
        }

    async def _describe_single(
        self,
        video_path: str,
        asset_format: str,
        angles: list[dict[str, Any]],
        system_prompt: str,
        prompt: str,
    ) -> dict[str, Any]:
        """描述整个视频（不分片）。"""
        try:
            raw_text, _ = await self._call_llm(system_prompt, prompt, asset_format, video_path)
            cleaned = _clean_llm_output(raw_text)
            parsed = json.loads(cleaned) if cleaned.startswith("{") else {"整体": {"text": cleaned, "tags": []}}
        except Exception as e:
            logger.error("视频描述失败: %s", e)
            parsed = {"整体": {"text": "", "tags": []}}

        return {
            "整体": parsed.get("整体", {"text": "", "tags": []}),
            "segments": [
                {
                    "start_time": 0.0,
                    "end_time": 0.0,
                    **parsed,
                }
            ],
        }

    async def _describe_segment(
        self,
        video_path: str,
        scene: SceneRange,
        index: int,
        asset_format: str,
        angles: list[dict[str, Any]],
        system_prompt: str,
        prompt: str,
    ) -> dict[str, Any]:
        """描述单个视频片段。"""
        # 提取片段
        segment_path = str(
            self._temp_dir / f"seg_{index}_{Path(video_path).stem}.mp4"
        )
        try:
            ffmpeg_extract_segment(video_path, scene.start_sec, scene.end_sec, segment_path)
        except (RuntimeError, ValueError) as e:
            logger.warning("片段提取失败，使用原视频: seg=%d, error=%s", index, e)
            segment_path = video_path

        # 构建带时间戳的 prompt
        time_prompt = (
            f"[时间范围: {scene.start_sec:.1f}s - {scene.end_sec:.1f}s] "
            f"{prompt}".strip()
        )

        # 调 LLM 描述
        try:
            raw_text, _ = await self._call_llm(system_prompt, time_prompt, asset_format, segment_path)
            cleaned = _clean_llm_output(raw_text)
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
        """从片段描述合成整体摘要。"""
        # 收集所有片段的 "整体" 文本和标签
        all_texts: list[str] = []
        all_tags: list[str] = []

        for seg in segment_descriptions:
            overall = seg.get("整体")
            if isinstance(overall, dict):
                text = overall.get("text", "")
                if text:
                    all_texts.append(text)
                tags = overall.get("tags")
                if isinstance(tags, list):
                    all_tags.extend(str(t) for t in tags if t)

        if not all_texts:
            return {"text": "", "tags": []}

        # 标签去重（保持顺序）
        seen: set[str] = set()
        unique_tags: list[str] = []
        for tag in all_tags:
            if tag not in seen:
                seen.add(tag)
                unique_tags.append(tag)

        scene_count = len(segment_descriptions)
        summary = f"视频包含{scene_count}个场景：{'；'.join(all_texts)}"

        return {
            "text": summary[:500],
            "tags": unique_tags[:20],
        }


def _clean_llm_output(text: str) -> str:
    """清理 LLM 输出中可能混入的 Markdown 代码块标记。"""
    cleaned = text.strip()
    fence_patterns = ("```json", "```")
    for prefix in fence_patterns:
        if cleaned.startswith(prefix):
            cleaned = cleaned[len(prefix):].lstrip()
            break
    if cleaned.endswith("```"):
        cleaned = cleaned[:-3].rstrip()
    return cleaned.strip()