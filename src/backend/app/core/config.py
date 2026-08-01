from __future__ import annotations

from functools import lru_cache
from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict

# .env 用绝对路径定位（相对 backend 根，而非进程 cwd）：
# Python 嵌入 C# 进程内时 cwd 是 Avalonia 运行目录，相对路径 ".env" 会找不到，
# 导致 DASHSCOPE_API_KEY 读不到、supports_live_call=False、所有描述请求降级为 mock 占位。
_BACKEND_ROOT = Path(__file__).resolve().parents[2]


class Settings(BaseSettings):
    app_env: str = "dev"
    data_root: str | None = Field(default=None, validation_alias="DATA_ROOT")
    dashscope_api_key: str = Field(default="", validation_alias="DASHSCOPE_API_KEY")
    media_temp_dir: str | None = Field(default=None, validation_alias="ALS_MEDIA_TEMP_DIR")
    enable_media_preprocess: bool = Field(default=True, validation_alias="ALS_ENABLE_MEDIA_PREPROCESS")
    image_max_side: int = Field(default=1600, validation_alias="ALS_IMAGE_MAX_SIDE")
    image_jpeg_quality: int = Field(default=82, validation_alias="ALS_IMAGE_JPEG_QUALITY")
    video_crf: int = Field(default=30, validation_alias="ALS_VIDEO_CRF")
    video_audio_bitrate: str = Field(default="128k", validation_alias="ALS_VIDEO_AUDIO_BITRATE")

    model_config = SettingsConfigDict(
        env_file=_BACKEND_ROOT / ".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    return Settings()


settings = get_settings()
