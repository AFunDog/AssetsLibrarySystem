from pydantic import BaseModel, Field


class AssetTaggingInfo(BaseModel):
    provider: str | None = None
    model: str | None = None
    description: str | None = None
    tags: list[str] = Field(default_factory=list)
    raw_text: str | None = None
    tagged_at: str | None = None


class AssetItem(BaseModel):
    id: str
    library_id: str
    library_name: str
    name: str
    asset_type: str
    path: str
    relative_path: str
    description: str
    tags: list[str]
    status: str
    tagging: AssetTaggingInfo | None = None


class AssetListResponse(BaseModel):
    items: list[AssetItem]
    total: int
    stage: str
