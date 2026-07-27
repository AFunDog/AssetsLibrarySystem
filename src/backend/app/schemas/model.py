from __future__ import annotations

from pathlib import Path
from typing import Any
from typing import Literal

from pydantic import BaseModel, Field, field_validator


AssetFormat = Literal["文本", "图片", "视频", "音频"]


class AngleDef(BaseModel):
    """单个角度的定义，由 C# 端传入"""
    key: str = Field(description="角度键名，如 '场景'、'歌词大意'")
    label: str = Field(description="展示标签，如 '场景环境'")
    prompt: str = Field(description="给 LLM 的指导，如 '描述视频中的场景和环境'")
    max_length: int = Field(default=120, description="该字段的最大字数")


class ModelGenerateRequest(BaseModel):
    asset_format: AssetFormat = Field(description="当前素材的格式")
    asset_path: str = Field(min_length=1, description="当前素材文件的绝对路径")
    prompt: str | None = Field(default=None, description="覆盖默认提示词")
    system_prompt: str | None = Field(default=None, description="覆盖默认系统提示词")
    mock_response: bool = Field(default=False, description="强制走占位响应，便于联调")
    subtype: str | None = Field(default=None, description="素材子类型，如 '实拍'、'歌曲'")
    angles: list[AngleDef] | None = Field(default=None, description="角度定义列表，由 C# 端传入")
    # 视频切片配置
    enable_slicing: bool = Field(default=False, description="是否启用视频切片描述")
    slice_threshold: float = Field(default=60.0, description="切片阈值（秒），超过此时长才切片")
    min_scene_len: int = Field(default=15, description="最小场景长度（帧），低于此长度的场景会被合并")
    adaptive_threshold: float = Field(default=3.0, description="场景检测自适应阈值，越高越不敏感")

    @field_validator("asset_path")
    @classmethod
    def validate_asset_path(cls, value: str) -> str:
        if not Path(value).is_absolute():
            raise ValueError("asset_path 必须是绝对路径")
        return value


class ModelGenerateResponse(BaseModel):
    class TokenUsage(BaseModel):
        input_tokens: int = Field(ge=0)
        output_tokens: int = Field(ge=0)
        total_tokens: int = Field(ge=0)
        image_tokens: int | None = Field(default=None, ge=0)
        video_tokens: int | None = Field(default=None, ge=0)
        audio_tokens: int | None = Field(default=None, ge=0)
        input_tokens_details: dict[str, Any] | None = Field(default=None)
        output_tokens_details: dict[str, Any] | None = Field(default=None)
        prompt_tokens_details: dict[str, Any] | None = Field(default=None)

    provider_slot: str
    provider: str
    model: str
    mode: Literal["mock", "live"]
    output_text: str
    system_prompt: str
    token_usage: TokenUsage | None = Field(
        default=None,
        description="本次调用消耗的 token 统计；mock 模式下通常为空。",
    )


class ModelCapabilitiesResponse(BaseModel):
    provider_slot: str
    provider: str
    model: str
    supports_live_call: bool
    description: str
