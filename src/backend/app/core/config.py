from __future__ import annotations

from functools import lru_cache

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    app_name: str = "Assets Library System API"
    app_version: str = "0.1.0"
    api_prefix: str = "/api/v1"

    app_env: str = "dev"
    database_url: str = "sqlite:///app.db"
    log_level: str = "INFO"
    host: str = "127.0.0.1"
    port: int = 8000
    data_root: str | None = Field(default=None, validation_alias="DATA_ROOT")
    dashscope_api_key: str = Field(default="", validation_alias="DASHSCOPE_API_KEY")
    search_cache_dir: str | None = Field(default=None, validation_alias="ALS_SEARCH_CACHE_DIR")
    search_embed_model: str = Field(default="Qwen/Qwen3-Embedding-0.6B", validation_alias="ALS_SEARCH_EMBED_MODEL")
    search_rerank_model: str = Field(default="Qwen/Qwen3-Reranker-0.6B", validation_alias="ALS_SEARCH_RERANK_MODEL")
    media_temp_dir: str | None = Field(default=None, validation_alias="ALS_MEDIA_TEMP_DIR")
    enable_media_preprocess: bool = Field(default=True, validation_alias="ALS_ENABLE_MEDIA_PREPROCESS")
    image_max_side: int = Field(default=1600, validation_alias="ALS_IMAGE_MAX_SIDE")
    image_jpeg_quality: int = Field(default=82, validation_alias="ALS_IMAGE_JPEG_QUALITY")
    video_crf: int = Field(default=30, validation_alias="ALS_VIDEO_CRF")
    video_audio_bitrate: str = Field(default="128k", validation_alias="ALS_VIDEO_AUDIO_BITRATE")

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    return Settings()


settings = get_settings()
