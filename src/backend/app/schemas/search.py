from __future__ import annotations

from datetime import datetime
from pathlib import Path
from typing import Literal

from pydantic import BaseModel, Field, field_validator

from app.schemas.model import AssetFormat


class SearchIndexRequest(BaseModel):
    asset_id: str = Field(min_length=1, description="素材唯一标识")
    asset_name: str = Field(min_length=1, description="素材名称")
    asset_format: AssetFormat = Field(description="素材类型")
    asset_path: str = Field(min_length=1, description="素材绝对路径")
    description: str = Field(min_length=1, description="可检索的素材描述")
    source_store_path: str | None = Field(default=None, description="来源描述存储路径")
    generated_at: datetime | None = Field(default=None, description="描述生成时间")

    @field_validator("asset_path")
    @classmethod
    def validate_asset_path(cls, value: str) -> str:
        if not Path(value).is_absolute():
            raise ValueError("asset_path 必须是绝对路径")
        return value


class SearchIndexResponse(BaseModel):
    asset_id: str
    asset_name: str
    asset_format: AssetFormat
    asset_path: str
    description: str
    vector: list[float]
    vector_dim: int = Field(ge=1)
    embedding_model: str


class SearchQueryRequest(BaseModel):
    query: str = Field(min_length=1, description="用户查询文本")
    candidates: list["SearchQueryCandidate"] = Field(min_length=1, description="待重排序候选集")
    final_top_k: int = Field(default=5, ge=1, le=50)

    @field_validator("query")
    @classmethod
    def validate_query(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("query 不能为空")
        return normalized


class SearchQueryCandidate(BaseModel):
    asset_id: str
    asset_name: str
    asset_format: AssetFormat
    asset_path: str
    description: str
    source_store_path: str | None = None
    generated_at: datetime | None = None


class SearchQueryResultItem(SearchQueryCandidate):
    embedding_similarity: float
    rerank_score: float


class SearchQueryResponse(BaseModel):
    query: str
    final_top_k: int
    rerank_model: str
    results: list[SearchQueryResultItem]


class SearchExploreRequest(BaseModel):
    query: str = Field(min_length=1, description="用户查询文本")
    candidate_top_k: int = Field(default=20, ge=1, le=200, description="向量召回候选数量")
    final_top_k: int = Field(default=5, ge=1, le=50, description="最终返回数量")
    asset_format: AssetFormat | None = Field(default=None, description="可选：按素材类型过滤")

    @field_validator("query")
    @classmethod
    def validate_query(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("query 不能为空")
        return normalized


class SearchExploreResponse(BaseModel):
    query: str
    candidate_top_k: int
    final_top_k: int
    asset_format: AssetFormat | None
    embedding_model: str
    rerank_model: str
    results: list[SearchQueryResultItem]


class SearchReindexResponse(BaseModel):
    document_count: int = Field(ge=1)
    vector_dim: int = Field(ge=1)
    database_path: str
    index_path: str
    metadata_path: str
    embedding_models: list[str]


class SearchWarmupResponse(BaseModel):
    model_kind: Literal["embedding", "rerank"]
    model_name: str
    device: str
    warmed: bool = True


SearchQueryRequest.model_rebuild()
SearchQueryResultItem.model_rebuild()
SearchExploreRequest.model_rebuild()
SearchExploreResponse.model_rebuild()
