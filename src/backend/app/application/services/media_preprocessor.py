from __future__ import annotations

import logging
from pathlib import Path
import re
import shutil
import subprocess
import uuid
from typing import Any

logger = logging.getLogger(__name__)


class MediaPreprocessor:
    """负责图片和视频的模型调用前预处理及临时文件生命周期。"""

    def __init__(
        self,
        temp_dir: Path,
        enabled: bool,
        image_max_side: int,
        image_jpeg_quality: int,
        video_crf: int,
        video_audio_bitrate: str,
    ) -> None:
        self.temp_dir = temp_dir
        self.enabled = enabled
        self.image_max_side = image_max_side
        self.image_jpeg_quality = image_jpeg_quality
        self.video_crf = video_crf
        self.video_audio_bitrate = video_audio_bitrate

    def prepare(self, asset_format: str, asset_path: str) -> str:
        source_path = Path(asset_path).resolve()
        if not source_path.exists():
            logger.error("素材不存在: %s", source_path)
            raise FileNotFoundError(f"素材不存在: {source_path}")

        if not self.enabled or asset_format not in {"图片", "视频", "视频剪辑"}:
            logger.debug("预处理跳过: format=%s, enabled=%s", asset_format, self.enabled)
            return str(source_path)

        self.temp_dir.mkdir(parents=True, exist_ok=True)
        target_path = self.build_temp_asset_path(source_path)
        if asset_format == "图片":
            result = self.compress_image(source_path, target_path)
        else:
            result = self.compress_video(source_path, target_path)

        if result == source_path:
            logger.warning("预处理回退到原始文件: format=%s, path=%s", asset_format, source_path)
        else:
            logger.debug("预处理完成: format=%s, size=%s -> %s", asset_format, source_path, result)
        return str(result)

    def cleanup(self, source_path: str, prepared_path: str) -> None:
        source = Path(source_path).resolve()
        prepared = Path(prepared_path).resolve()
        if prepared == source:
            return

        try:
            prepared.relative_to(self.temp_dir.resolve())
        except ValueError:
            return

        self.remove_file_if_exists(prepared)
        logger.debug("临时文件已清理: %s", prepared)

    def build_temp_asset_path(self, source_path: Path, preferred_suffix: str | None = None) -> Path:
        safe_stem = re.sub(r"[^\w\-.]+", "_", source_path.stem).strip("._") or "asset"
        suffix = preferred_suffix or source_path.suffix or ".bin"
        return self.temp_dir / f"{safe_stem}-{uuid.uuid4().hex[:8]}{suffix.lower()}"

    def compress_image(self, source_path: Path, target_path: Path) -> Path:
        try:
            from PIL import Image
        except ImportError:
            logger.warning("Pillow 未安装，跳过图片压缩: %s", source_path)
            return source_path

        try:
            with Image.open(source_path) as image:
                converted = image.copy()
                converted.thumbnail((self.image_max_side, self.image_max_side))
                suffix = target_path.suffix.lower()

                if suffix in {".jpg", ".jpeg"}:
                    if converted.mode not in {"RGB", "L"}:
                        converted = converted.convert("RGB")
                    converted.save(target_path, format="JPEG", quality=self.image_jpeg_quality, optimize=True)
                    logger.debug("图片 JPEG 压缩完成: %s -> %s", source_path.name, target_path)
                    return target_path

                if suffix == ".webp":
                    converted.save(target_path, format="WEBP", quality=self.image_jpeg_quality, method=6)
                    logger.debug("图片 WebP 压缩完成: %s -> %s", source_path.name, target_path)
                    return target_path

                save_kwargs: dict[str, Any] = {"optimize": True}
                if suffix == ".png":
                    save_kwargs["compress_level"] = 9
                converted.save(target_path, **save_kwargs)
                logger.debug("图片压缩完成: %s -> %s", source_path.name, target_path)
                return target_path
        except Exception as exc:
            logger.warning("图片压缩失败，回退到原始文件: %s, error=%s", source_path, exc)
            self.remove_file_if_exists(target_path)
            return source_path

    def compress_video(self, source_path: Path, target_path: Path) -> Path:
        if shutil.which("ffmpeg") is None:
            logger.warning("ffmpeg 未找到，跳过视频压缩: %s", source_path)
            return source_path

        command = [
            "ffmpeg", "-y", "-i", str(source_path),
            "-vf", "scale='min(1280,iw)':-2",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", str(self.video_crf),
            "-c:a", "aac", "-b:a", self.video_audio_bitrate,
            str(target_path),
        ]
        return self.run_ffmpeg_or_fallback(command, source_path, target_path)

    def run_ffmpeg_or_fallback(self, command: list[str], source_path: Path, target_path: Path) -> Path:
        try:
            # 加超时兜底：大视频压缩可能长时间挂起，超时后回退原始文件
            subprocess.run(
                command,
                check=True,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                timeout=300,
            )
            logger.debug("视频压缩完成: %s -> %s", source_path.name, target_path)
        except Exception as exc:
            logger.warning("视频压缩失败，回退到原始文件: %s, error=%s", source_path, exc)
            self.remove_file_if_exists(target_path)
            return source_path

        if not target_path.exists() or target_path.stat().st_size <= 0:
            logger.warning("视频压缩输出为空，回退到原始文件: %s", source_path)
            self.remove_file_if_exists(target_path)
            return source_path
        return target_path

    @staticmethod
    def remove_file_if_exists(path: Path) -> None:
        try:
            path.unlink(missing_ok=True)
        except OSError:
            pass