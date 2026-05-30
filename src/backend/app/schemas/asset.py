from pydantic import BaseModel


class AssetItem(BaseModel):
    id: str
    name: str
    asset_type: str
    path: str
    description: str
    tags: list[str]
    status: str


class AssetListResponse(BaseModel):
    items: list[AssetItem]
    total: int
    stage: str
