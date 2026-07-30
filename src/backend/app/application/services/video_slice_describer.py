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
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=300)
    if result.returncode != 0:
        logger.warning(
            "ffmpeg 提取片段失败: returncode=%d, stderr=%s",
            result.returncode,
            result.stderr[:300],
        )
        raise RuntimeError(f"ffmpeg 提取片段失败: {result.stderr[:200]}")
    return output_path


def get_video_duration(video_path: str) -> float:
    """用 ffprobe 获取视频时长（秒）。失败返回 0.0（调用方需自行兜底）。"""
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
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
    if result.returncode != 0 or not result.stdout.strip():
        logger.warning(
            "ffprobe 获取时长失败: path=%s, returncode=%s",
            video_path,
            result.returncode,
        )
        return 0.0
    try:
        return float(result.stdout.strip())
    except ValueError:
        logger.warning("ffprobe 时长解析失败: path=%s, raw=%r", video_path, result.stdout)
        return 0.0


class VideoSliceDescriber:
    """视频切片描述器。

    两阶段流程：
    1. 检测场景 → 提取片段 → 调 LLM 描述每个片段
    2. 从片段描述合成整体摘要

    Args:
        call_llm: 调用 LLM 的异步回调函数，用于描述视频片段。
        summarize_fn: 可选，用于总结累积摘要的异步回调。
        scene_detector: 场景检测器实例。
        slice_threshold: 切片阈值（秒），超过此值时长的视频才启用切片。
        min_seconds: 最小场景时长（秒），相邻过短场景会被合并。默认 5.0。
        overlap_seconds: 相邻切片的重叠秒数，前后各延伸此值，避免切点遗漏关键内容。默认 0.5。
        temp_dir: 临时文件目录。
    """

    def __init__(
        self,
        call_llm: Callable[..., Awaitable[tuple[str, Any]]],
        summarize_fn: Callable[[str], Awaitable[str]] | None = None,
        scene_detector: VideoSceneDetector | None = None,
        slice_threshold: float = DEFAULT_SLICE_THRESHOLD_SECONDS,
        min_seconds: float = 5.0,
        overlap_seconds: float = 0.5,
        temp_dir: str | Path | None = None,
    ) -> None:
        self._call_llm = call_llm
        self._summarize_fn = summarize_fn
        self._scene_detector = scene_detector or VideoSceneDetector(min_seconds=min_seconds)
        self._slice_threshold = slice_threshold
        self._overlap_seconds = overlap_seconds
        self._temp_dir = Path(temp_dir) if temp_dir else Path(tempfile.gettempdir())

    def should_slice(self, video_path: str) -> bool:
        """判断视频是否需要切片。时长未知（0）时不切片，避免错误裁剪。"""
        duration = get_video_duration(video_path)
        if duration <= 0:
            logger.warning("视频时长未知，跳过切片: path=%s", video_path)
            return False
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

        # 获取视频总时长，用于边界裁剪；ffprobe 失败时用场景最大 end_sec 兜底
        video_duration = get_video_duration(video_path)
        if video_duration <= 0:
            video_duration = max((s.end_sec for s in scenes), default=0.0)
            logger.warning(
                "ffprobe 时长不可用，使用场景最大 end_sec=%.2f 作为边界",
                video_duration,
            )
        if video_duration <= 0:
            raise RuntimeError(f"无法确定视频时长，拒绝切片描述: {video_path}")

        # 2. 描述每个片段（带重叠 + 上下文传递）
        segment_descriptions = []
        previous_context = ""
        cumulative_summary = ""
        for i, scene in enumerate(scenes):
            # 应用重叠：起点向前延伸，终点向后延伸，不超出视频边界
            overlap = self._overlap_seconds
            overlap_start = max(0.0, scene.start_sec - overlap)
            overlap_end = min(video_duration, scene.end_sec + overlap)
            if overlap_end <= overlap_start:
                overlap_start = scene.start_sec
                overlap_end = max(scene.end_sec, scene.start_sec + 0.1)
            overlapped = SceneRange(
                start_frame=scene.start_frame,
                end_frame=scene.end_frame,
                start_sec=overlap_start,
                end_sec=overlap_end,
            )
            seg_desc = await self._describe_segment(
                video_path=video_path,
                scene=overlapped,
                index=i,
                asset_format=asset_format,
                angles=angles,
                system_prompt=system_prompt,
                prompt=prompt,
                previous_context=previous_context,
                cumulative_summary=cumulative_summary,
            )
            # 提取当前片段描述，更新累积摘要和上一片段上下文
            seg_texts = []
            for angle in angles:
                key = angle["key"]
                if key in seg_desc:
                    value = seg_desc[key]
                    if isinstance(value, dict):
                        text = value.get("text", "")
                        if text:
                            seg_texts.append(f"{key}: {text}")

            seg_brief = "；".join(seg_texts) if seg_texts else ""
            if seg_brief:
                if self._summarize_fn:
                    # 用 LLM 总结累积摘要
                    new_entry = f"片段{i+1}({scene.start_sec:.1f}s-{scene.end_sec:.1f}s)：{seg_brief[:200]}"
                    input_text = f"{cumulative_summary}\n{new_entry}" if cumulative_summary else new_entry
                    try:
                        cumulative_summary = await self._summarize_fn(input_text)
                    except Exception as e:
                        logger.warning("摘要总结失败，使用拼接方案: %s", e)
                        cumulative_summary = self._append_to_summary(cumulative_summary, new_entry, i, scene)
                else:
                    # 无 summarize_fn，使用拼接截断方案
                    new_entry = f"片段{i+1}({scene.start_sec:.1f}s-{scene.end_sec:.1f}s)：{seg_brief[:200]}"
                    cumulative_summary = self._append_to_summary(cumulative_summary, new_entry, i, scene)

                previous_context = f"上一片段 ({scene.start_sec:.1f}s-{scene.end_sec:.1f}s)：{seg_brief[:300]}"
            else:
                previous_context = ""

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
        """描述整个视频（不分片）。LLM/JSON 失败向上抛出，避免假成功写入空描述。"""
        try:
            raw_text, _ = await self._call_llm(system_prompt, prompt, asset_format, video_path)
            cleaned = _clean_llm_output(raw_text)
            parsed = json.loads(cleaned) if cleaned.startswith("{") else {"整体": {"text": cleaned, "tags": []}}
            parsed = _normalize_angle_fields(parsed)
        except Exception as e:
            logger.error("视频描述失败: %s", e)
            raise RuntimeError(f"视频描述失败: {e}") from e

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
        previous_context: str = "",
        cumulative_summary: str = "",
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

        # 构建带时间戳和上下文的 prompt
        parts = [f"[时间范围: {scene.start_sec:.1f}s - {scene.end_sec:.1f}s]"]
        if cumulative_summary:
            parts.append(f"[历史摘要: {cumulative_summary[:500]}]")
        if previous_context:
            parts.append(f"[{previous_context[:300]}]")
        parts.append("[注意：当前画面是最高优先级证据，请基于当前片段内容进行描述，不要为了与前文一致而忽略当前画面。]")
        # 格式示例（强制模型输出 {"text": ..., "tags": [...]} 格式）
        example = (
            '【输出格式要求】\n'
            '每个字段必须严格使用以下结构，不得使用扁平字符串：\n'
            '{\n'
            '  "整体": {"text": "视频展示了雨天的城市街景…", "tags": ["城市", "雨天", "街景"]},\n'
            '  "场景": {"text": "阴雨天的街道…", "tags": ["街道", "雨天", "建筑"]},\n'
            '  "人物": {"text": "多名行人撑着雨伞…", "tags": ["行人", "雨伞", "学生"]},\n'
            '  "动作": {"text": "行人过马路…", "tags": ["过马路", "行走"]},\n'
            '  "情感": {"text": "宁静略带忧郁…", "tags": ["宁静", "忧郁"]},\n'
            '  "镜头": {"text": "从模糊散景逐渐聚焦…", "tags": ["散景", "聚焦", "特写"]},\n'
            '  "关键画面": {"text": "红灯变绿灯…", "tags": ["红绿灯", "斑马线"]},\n'
            '  "时间线": {"text": "0s-3s画面…", "tags": ["时间线", "顺序"]}\n'
            '}'
        )
        parts.append(example)
        if prompt:
            parts.append(prompt)
        time_prompt = "\n".join(parts)

        # 调 LLM 描述；失败向上抛出，避免静默空描述被当成 live 成功
        try:
            raw_text, _ = await self._call_llm(system_prompt, time_prompt, asset_format, segment_path)
            cleaned = _clean_llm_output(raw_text)
            parsed = json.loads(cleaned) if cleaned.startswith("{") else {"整体": {"text": cleaned, "tags": []}}
            parsed = _normalize_angle_fields(parsed)
        except Exception as e:
            logger.error("片段描述失败: seg=%d, error=%s", index, e)
            if segment_path != video_path:
                Path(segment_path).unlink(missing_ok=True)
            raise RuntimeError(f"片段描述失败 (seg={index}): {e}") from e

        # 清理临时文件
        if segment_path != video_path:
            Path(segment_path).unlink(missing_ok=True)

        return {
            "start_time": scene.start_sec,
            "end_time": scene.end_sec,
            **parsed,
        }

    @staticmethod
    def _append_to_summary(
        current_summary: str,
        new_entry: str,
        index: int,
        scene: SceneRange,
    ) -> str:
        """拼接累积摘要，超过 800 字时截断保留最近内容"""
        result = f"{current_summary}\n{new_entry}" if current_summary else new_entry
        if len(result) > 800:
            cutoff = result.rfind("\n", len(result) - 600)
            result = result[cutoff + 1:] if cutoff > 0 else result[-600:]
        return result

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


def _normalize_angle_fields(parsed: dict) -> dict:
    """确保每个字段使用 {"text": ..., "tags": [...]} 格式，若模型输出扁平字符串则自动包装。"""
    result = {}
    for key, value in parsed.items():
        if isinstance(value, dict):
            # 已经是对象格式，确保有 text 和 tags 字段
            result[key] = {
                "text": value.get("text", ""),
                "tags": value.get("tags", []),
            }
        elif isinstance(value, str):
            # 扁平字符串，自动包装
            result[key] = {"text": value, "tags": []}
        else:
            # 其他类型（null、list 等），置空
            result[key] = {"text": "", "tags": []}
    return result


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