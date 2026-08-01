"""视频帧提取：用 ffmpeg 按时间点提取单帧，供桌面端片段列表缩略图使用。

与 openai_model_client 中的帧提取不同，这里直接返回 JPEG 字节（base64），
由 C# 端通过 Python.NET 进程内调用，不经过 HTTP。
"""

from __future__ import annotations

import base64
import logging
import subprocess
from pathlib import Path

logger = logging.getLogger(__name__)


def extract_frame(video_path: str, timestamp: float) -> str | None:
    """提取视频指定时间点的帧，返回 JPEG 的 base64 字符串。

    Args:
        video_path: 视频文件本地路径
        timestamp: 时间点（秒）

    Returns:
        JPEG 图片的 base64 字符串；失败时返回 None。
    """
    path = Path(video_path)
    if not path.exists():
        logger.warning("视频文件不存在: %s", video_path)
        return None

    ts = max(0.0, float(timestamp))
    try:
        cmd = [
            "ffmpeg", "-y",
            "-ss", f"{ts:.3f}",
            "-i", str(path),
            "-vframes", "1",
            "-q:v", "3",
            "-vf", "scale=320:-2",
            "-f", "mjpeg",
            "-",
        ]
        result = subprocess.run(cmd, capture_output=True, timeout=30)
        if result.returncode != 0 or not result.stdout:
            logger.warning(
                "ffmpeg 提取视频帧失败: path=%s, ts=%s, stderr=%s",
                video_path, ts, result.stderr[:200],
            )
            return None

        return base64.b64encode(result.stdout).decode("ascii")
    except Exception as e:  # noqa: BLE001
        logger.warning("提取视频帧异常: path=%s, ts=%s, error=%s", video_path, ts, e)
        return None
