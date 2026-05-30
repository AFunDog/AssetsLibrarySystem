from pydantic import BaseModel, Field


class TaggingAssetRequest(BaseModel):
    asset_id: str = Field(..., min_length=1)
    asset_type: str = Field(..., min_length=1, description="text / image / video / music")
    source_path: str = Field(..., min_length=1, description="本地文件路径")
    text: str | None = Field(default=None, description="文本素材内容；非文本素材可留空")
    media_mime_type: str | None = Field(default=None, description="媒体 MIME 类型")
    title: str | None = Field(default=None, description="素材标题，可选")


class TaggingAssetResponse(BaseModel):
    asset_id: str
    asset_type: str
    source_path: str
    provider: str
    model: str
    description: str
    tags: list[str]
    raw_text: str
    stage: str
