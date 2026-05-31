from __future__ import annotations

from pathlib import Path
from typing import Literal

from pydantic import BaseModel, Field, field_validator


AssetFormat = Literal["文本", "图片", "视频", "音频"]


class ModelGenerateRequest(BaseModel):
    asset_format: AssetFormat = Field(description="当前素材的格式")
    asset_path: str = Field(min_length=1, description="当前素材文件的绝对路径")
    prompt: str | None = Field(default=None, description="覆盖默认提示词")
    system_prompt: str | None = Field(default=None, description="覆盖默认系统提示词")
    mock_response: bool = Field(default=False, description="强制走占位响应，便于联调")

    @field_validator("asset_path")
    @classmethod
    def validate_asset_path(cls, value: str) -> str:
        if not Path(value).is_absolute():
            raise ValueError("asset_path 必须是绝对路径")
        return value


class ModelGenerateResponse(BaseModel):
    provider_slot: str
    provider: str
    model: str
    mode: Literal["mock", "live"]
    output_text: str
    system_prompt: str


class ModelCapabilitiesResponse(BaseModel):
    provider_slot: str
    provider: str
    model: str
    supports_live_call: bool
    description: str
