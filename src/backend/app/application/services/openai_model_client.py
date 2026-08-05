from __future__ import annotations

import base64
import json
import logging
from pathlib import Path
from typing import Any

import httpx

from app.application.services.model_client import ModelClient
from app.core.provider_config import ProviderConfig

logger = logging.getLogger(__name__)

DEFAULT_OPENAI_BASE_URL = "https://api.openai.com/v1"
CHAT_COMPLETIONS_PATH = "/chat/completions"


# 视频多帧提取配置
# 按 DashScope 的 5fps 采样标准，每秒钟提取 5 帧
# 帧数 = fps * 视频时长（秒），无上限，完全对齐原生视频处理


class OpenAIModelClient(ModelClient):
    """OpenAI 兼容 API 客户端。

    支持任意实现 OpenAI Chat Completions 接口的供应商：
    - OpenAI（GPT-4o 等）
    - Ollama（本地模型）
    - vLLM（自托管）
    - LM Studio
    - Groq
    - Together AI
    - 等等

    通过 ``provider_config.base_url`` 配置 API 端点地址。
    """

    def call_generation(
        self,
        provider_config: ProviderConfig,
        model_name: str,
        system_prompt: str,
        user_prompt: str,
        text_content: str,
        response_format: dict[str, Any],
    ) -> Any:
        base_url = provider_config.base_url or DEFAULT_OPENAI_BASE_URL
        url = base_url.rstrip("/") + CHAT_COMPLETIONS_PATH

        messages: list[dict[str, str]] = [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": f"{user_prompt}\n\n素材内容：\n{text_content}".strip()},
        ]

        body: dict[str, Any] = {
            "model": model_name,
            "messages": messages,
            "temperature": provider_config.temperature,
            "max_tokens": provider_config.max_tokens,
        }
        if response_format and response_format.get("type"):
            body["response_format"] = response_format

        logger.info(
            "OpenAI 文本生成: url=%s, model=%s, max_tokens=%s",
            url, model_name, provider_config.max_tokens,
        )
        return self._post(url, provider_config.api_key, body)

    def call_multimodal(
        self,
        provider_config: ProviderConfig,
        model_name: str,
        system_prompt: str,
        multimodal_content: list[dict[str, Any]],
        response_format: dict[str, Any],
        **extra_kwargs: Any,
    ) -> Any:
        base_url = provider_config.base_url or DEFAULT_OPENAI_BASE_URL
        url = base_url.rstrip("/") + CHAT_COMPLETIONS_PATH

        messages: list[dict[str, Any]] = []
        if system_prompt.strip():
            messages.append({"role": "system", "content": system_prompt})

        # 将 DashScope 格式的多模态内容转换为 OpenAI 格式
        openai_content = self._convert_multimodal_content(multimodal_content)
        messages.append({"role": "user", "content": openai_content})

        body: dict[str, Any] = {
            "model": model_name,
            "messages": messages,
            "temperature": provider_config.temperature,
            "max_tokens": provider_config.max_tokens,
        }
        if response_format and response_format.get("type"):
            body["response_format"] = response_format

        # 透传额外参数（extra_body 中除 response_format 外的配置项），避免覆盖已显式设置的键
        body.update(extra_kwargs)

        logger.info(
            "OpenAI 多模态生成: url=%s, model=%s, items=%d",
            url, model_name, len(multimodal_content),
        )
        return self._post(url, provider_config.api_key, body)

    # ── 内部方法 ────────────────────────────────────────────────

    def _post(self, url: str, api_key: str, body: dict[str, Any]) -> dict[str, Any]:
        """发送 POST 请求到 OpenAI 兼容 API 并返回 JSON 响应。"""
        headers = {
            "Authorization": f"Bearer {api_key}",
            "Content-Type": "application/json",
        }
        try:
            response = httpx.post(
                url,
                headers=headers,
                json=body,
                timeout=300.0,  # 大模型调用可能耗时较长
            )
            response.raise_for_status()
            return response.json()
        except httpx.HTTPStatusError as e:
            logger.error(
                "OpenAI API HTTP 错误: status=%d, body=%s",
                e.response.status_code,
                e.response.text[:500],
            )
            raise
        except httpx.TimeoutException:
            logger.error("OpenAI API 请求超时: url=%s", url)
            raise
        except Exception as e:
            logger.error("OpenAI API 请求失败: %s", e)
            raise

    def _convert_multimodal_content(
        self,
        dashscope_content: list[dict[str, Any]],
    ) -> list[dict[str, Any]]:
        """将 DashScope 风格的多模态内容列表转换为 OpenAI 风格。

        DashScope 格式::
            [{"image": "file:///path/to/img.jpg"}, {"text": "描述这个图片"}]

        OpenAI 格式::
            [
                {"type": "image_url", "image_url": {"url": "data:image/jpeg;base64,..."}},
                {"type": "text", "text": "描述这个图片"},
            ]
        """
        openai_items: list[dict[str, Any]] = []
        for item in dashscope_content:
            if not isinstance(item, dict):
                continue

            # 文本
            if "text" in item:
                openai_items.append({"type": "text", "text": str(item["text"])})
                continue

            # 图片
            image_path = item.get("image")
            if image_path:
                local_path = self._strip_file_uri(str(image_path))
                openai_item = self._build_image_item(local_path)
                if openai_item:
                    openai_items.append(openai_item)
                continue

            # 视频 — OpenAI 不支持原生视频，均匀提取多帧作为图片
            video_path = item.get("video")
            if video_path:
                local_path = self._strip_file_uri(str(video_path))
                fps = item.get("fps", 5)  # 来自 _build_media_item 的 fps 参数
                frame_items = self._extract_video_frames(local_path, fps)
                if frame_items:
                    openai_items.extend(frame_items)
                    logger.info(
                        "视频提取多帧: path=%s, frames=%d",
                        local_path, len(frame_items),
                    )
                continue

            # 音频 — OpenAI Chat Completions 不支持原生音频
            audio_path = item.get("audio")
            if audio_path:
                local_path = self._strip_file_uri(str(audio_path))
                logger.warning(
                    "OpenAI Chat Completions 不支持原生音频输入，跳过: %s", local_path,
                )
                continue

        return openai_items

    def _build_image_item(self, file_path: str) -> dict[str, Any] | None:
        """读取图片文件并构建 OpenAI 图片内容项。"""
        path = Path(file_path)
        if not path.exists():
            logger.warning("图片文件不存在: %s", file_path)
            return None

        try:
            data = path.read_bytes()
            mime = self._guess_mime_type(path.suffix)
            b64 = base64.b64encode(data).decode("ascii")
            return {
                "type": "image_url",
                "image_url": {
                    "url": f"data:{mime};base64,{b64}",
                    "detail": "low",  # 降低分辨率减少 token 消耗
                },
            }
        except Exception as e:
            logger.warning("读取图片文件失败: %s, error=%s", file_path, e)
            return None

    def _extract_video_frames(self, video_path: str, fps: int = 5) -> list[dict[str, Any]]:
        """从视频中按 fps 提取多帧作为图片列表。

        按 ``fps`` 参数逐秒提取帧，对齐 DashScope 的 5fps 采样标准。
        帧数 = fps * duration，在时间轴上均匀分布。

        Args:
            video_path: 视频文件路径
            fps: 采样帧率（来自 DashScope 格式的 fps 参数，默认 5fps）

        Returns:
            OpenAI 图片格式的帧列表，每项为 ``{"type": "image_url", ...}``
        """
        import subprocess
        import tempfile

        path = Path(video_path)
        if not path.exists():
            logger.warning("视频文件不存在: %s", video_path)
            return []

        # 获取视频时长
        duration = self._get_video_duration(video_path)
        if duration <= 0:
            logger.warning("无法获取视频时长，回退到提取第一帧: %s", video_path)
            frame = self._extract_video_first_frame(video_path)
            return [frame] if frame else []

        # 按 fps * duration 计算帧数，但硬性上限避免长视频 OOM / 超大请求体
        max_frames = 32
        requested_frames = max(3, int(duration * fps))
        num_frames = min(requested_frames, max_frames)
        safe_duration = max(duration - 0.5, 0.1)  # 避免取到视频末尾边界

        if num_frames <= 1:
            timestamps = [0.0]
        else:
            step = safe_duration / (num_frames - 1)
            timestamps = [i * step for i in range(num_frames)]

        if requested_frames > max_frames:
            logger.warning(
                "视频帧数触达上限: duration=%ss, fps=%d, requested=%d, capped=%d",
                duration,
                fps,
                requested_frames,
                max_frames,
            )
        logger.info(
            "视频帧提取: duration=%ss, fps=%d, frames=%d",
            duration, fps, num_frames,
        )

        # 提取每一帧
        frames: list[dict[str, Any]] = []
        for ts in timestamps:
            frame_path: str | None = None
            try:
                with tempfile.NamedTemporaryFile(suffix=".jpg", delete=False) as tmp:
                    frame_path = tmp.name

                cmd = [
                    "ffmpeg", "-y",
                    "-ss", str(ts),
                    "-i", str(path),
                    "-vframes", "1",
                    "-q:v", "2",
                    str(frame_path),
                ]
                result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
                if result.returncode != 0:
                    logger.warning(
                        "ffmpeg 提取帧失败: ts=%ss, error=%s",
                        ts, result.stderr[:100],
                    )
                    continue

                item = self._build_image_item(frame_path)
                if item:
                    frames.append(item)
            except Exception as e:
                logger.warning("提取视频帧异常: ts=%ss, error=%s", ts, e)
            finally:
                # 成功/失败路径都清理临时帧文件（杀软/句柄占用时忽略删除失败）
                if frame_path is not None:
                    try:
                        Path(frame_path).unlink(missing_ok=True)
                    except OSError:
                        pass

        if not frames:
            # 回退到第一帧
            frame = self._extract_video_first_frame(video_path)
            if frame:
                frames.append(frame)

        logger.info(
            "视频多帧提取完成: duration=%ss, requested=%d, got=%d",
            duration, len(timestamps), len(frames),
        )
        return frames

    def _get_video_duration(self, video_path: str) -> float:
        """用 ffprobe 获取视频时长（秒）。"""
        import subprocess
        cmd = [
            "ffprobe", "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            video_path,
        ]
        try:
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
            if result.returncode == 0 and result.stdout.strip():
                return float(result.stdout.strip())
        except Exception:
            pass
        return 0.0

    def _extract_video_first_frame(self, video_path: str) -> dict[str, Any] | None:
        """尝试用 ffmpeg 提取视频第一帧作为图片（回退方法）。"""
        import subprocess
        import tempfile

        path = Path(video_path)
        if not path.exists():
            return None

        try:
            with tempfile.NamedTemporaryFile(suffix=".jpg", delete=False) as tmp:
                frame_path = tmp.name

            cmd = [
                "ffmpeg", "-y",
                "-ss", "0",
                "-i", str(path),
                "-vframes", "1",
                "-q:v", "2",
                str(frame_path),
            ]
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
            if result.returncode != 0:
                logger.warning("ffmpeg 提取视频帧失败: %s", result.stderr[:200])
                return None

            return self._build_image_item(frame_path)
        except Exception as e:
            logger.warning("提取视频帧异常: %s", e)
            return None
        finally:
            # 成功/失败路径都清理临时帧文件
            if "frame_path" in locals():
                try:
                    Path(frame_path).unlink(missing_ok=True)
                except OSError:
                    pass

    @staticmethod
    def _guess_mime_type(suffix: str) -> str:
        mapping = {
            ".jpg": "image/jpeg",
            ".jpeg": "image/jpeg",
            ".png": "image/png",
            ".gif": "image/gif",
            ".webp": "image/webp",
            ".bmp": "image/bmp",
        }
        return mapping.get(suffix.lower(), "image/jpeg")

    @staticmethod
    def _strip_file_uri(uri: str) -> str:
        """去除 file:// 前缀，返回本地文件系统路径。

        支持格式:
        - file:///C:/path/to/file  ->  C:/path/to/file
        - file:///path/to/file     ->  /path/to/file
        - C:/path/to/file          ->  C:/path/to/file（无前缀时原样返回）
        """
        prefix = "file://"
        if uri.startswith(prefix):
            path = uri[len(prefix):]
            # Windows 上 file:///C:/... 去掉前缀后是 /C:/...，去掉开头的 /
            if path.startswith("/") and len(path) > 2 and path[2] == ":":
                return path.lstrip("/")
            return path
        return uri