from __future__ import annotations

from datetime import datetime
from pathlib import Path
from typing import Literal

from pydantic import BaseModel, Field, field_validator

from app.schemas.model import AssetFormat

EmbeddingDimensions = Literal[2048, 1024, 512]


class SearchIndexRequest(BaseModel):
    provider: Literal["local", "dashscope"] = "local"
    model: str = Field(default="Qwen/Qwen3-Embedding-0.6B", min_length=1)
    embedding_dimensions: EmbeddingDimensions | None = Field(
        default=None,
        description="DashScope embedding output dimensions; ignored by local models",
    )
    asset_id: str = Field(min_length=1, description="Asset id")
    asset_name: str = Field(min_length=1, description="Asset name")
    asset_format: AssetFormat = Field(description="Asset type")
    asset_path: str = Field(min_length=1, description="Absolute asset path")
    description: str = Field(min_length=1, description="Searchable asset description")
    generated_at: datetime | None = Field(default=None, description="Description generation time")

    @field_validator("asset_path")
    @classmethod
    def validate_asset_path(cls, value: str) -> str:
        if not Path(value).is_absolute():
            raise ValueError("asset_path must be absolute")
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
    token_usage: int | None = None


class SearchQueryRequest(BaseModel):
    provider: Literal["local", "dashscope"] = "local"
    model: str = Field(default="Qwen/Qwen3-Reranker-0.6B", min_length=1)
    query: str = Field(min_length=1, description="User query")
    candidates: list["SearchQueryCandidate"] = Field(min_length=1, description="Candidates to rerank")
    final_top_k: int = Field(default=5, ge=1, le=50)

    @field_validator("query")
    @classmethod
    def validate_query(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("query cannot be empty")
        return normalized


class SearchQueryCandidate(BaseModel):
    candidate_id: str | None = None
    asset_id: str
    asset_name: str
    asset_format: AssetFormat
    asset_path: str
    description: str
    tags: list[str] = Field(default_factory=list)
    generated_at: datetime | None = None


class SearchQueryResultItem(SearchQueryCandidate):
    embedding_similarity: float | None = Field(default=None)
    vector_distance: float | None = Field(default=None)
    rerank_score: float
    combined_score: float | None = Field(default=None)


class SearchQueryResponse(BaseModel):
    query: str
    final_top_k: int
    rerank_model: str
    results: list[SearchQueryResultItem]
    token_usage: int | None = None


class SearchWarmupResponse(BaseModel):
    model_kind: Literal["embedding", "rerank"]
    model_name: str
    device: str
    warmed: bool = True


class SearchModelCloseRequest(BaseModel):
    model_kind: Literal["embedding", "rerank"] = Field(description="Model kind to release")


class SearchModelCloseResponse(BaseModel):
    model_kind: Literal["embedding", "rerank"]
    model_name: str
    device: str
    closed: bool = Field(description="Whether a loaded model was released")
    cuda_cache_cleared: bool = Field(description="Whether CUDA cache was cleared")
    remaining_loaded_models: list[Literal["embedding", "rerank"]] = Field(default_factory=list)


class SearchModelStatusResponse(BaseModel):
    embedding_model_name: str
    rerank_model_name: str
    device: str
    loaded_model_kinds: list[Literal["embedding", "rerank"]] = Field(default_factory=list)
    embedding_loaded: bool
    rerank_loaded: bool
    loaded_count: int = Field(ge=0)


SearchQueryRequest.model_rebuild()
SearchQueryResultItem.model_rebuild()
