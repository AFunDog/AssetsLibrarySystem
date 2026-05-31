from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, Field


class ModelMessage(BaseModel):
    role: Literal["system", "user", "assistant"] = "user"
    content: str = Field(..., min_length=1)


class ModelGenerateRequest(BaseModel):
    provider_slot: str = Field(default="llm_gateway", min_length=1)
    prompt: str = Field(..., min_length=1, description="本次请求的主提示词")
    system_prompt: str | None = Field(default=None, description="覆盖默认系统提示词")
    messages: list[ModelMessage] = Field(default_factory=list, description="可选历史消息")
    mock_response: bool = Field(default=False, description="强制走占位响应，便于联调")


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
