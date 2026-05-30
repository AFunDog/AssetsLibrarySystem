from pydantic import BaseModel, Field


class SearchRequest(BaseModel):
    query: str = Field(..., min_length=1, description="自然语言查询文本")


class SearchResultItem(BaseModel):
    asset_id: str
    name: str
    asset_type: str
    summary: str
    score: float


class SearchResponse(BaseModel):
    query: str
    items: list[SearchResultItem]
    answer: str
    stage: str
