from __future__ import annotations

import numpy as np
from fastapi import APIRouter, File, Form, HTTPException, UploadFile
from pydantic import ValidationError

from app.application.services.search_service import SearchService
from app.infrastructure.search.sqlite_vector_repository import AssetVectorInput
from app.schemas.search import (
    SearchIndexRequest,
    SearchIndexResponse,
    SearchReindexDocument,
    SearchReindexResponse,
    SearchQueryRequest,
    SearchQueryResponse,
)


router = APIRouter(prefix="/search", tags=["search"])
_search_service: SearchService | None = None


def get_search_service() -> SearchService:
    global _search_service
    if _search_service is None:
        _search_service = SearchService()
    return _search_service


@router.post("/index", response_model=SearchIndexResponse)
def index_description(payload: SearchIndexRequest) -> SearchIndexResponse:
    try:
        return get_search_service().vectorize(payload)
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.post("/query", response_model=SearchQueryResponse)
def search(payload: SearchQueryRequest) -> SearchQueryResponse:
    try:
        return get_search_service().rerank(payload)
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.post("/reindex", response_model=SearchReindexResponse)
async def reindex(
    documents: list[str] = Form(..., description="每条向量文档的元数据 JSON"),
    vector_blobs: list[UploadFile] = File(..., description="与元数据顺序一一对应的二进制向量文件"),
) -> SearchReindexResponse:
    try:
        if len(documents) != len(vector_blobs):
            raise ValueError("documents 和 vector_blobs 数量必须一致。")

        prepared_documents: list[AssetVectorInput] = []
        for index, (metadata_json, vector_file) in enumerate(zip(documents, vector_blobs, strict=True), start=1):
            try:
                metadata = SearchReindexDocument.model_validate_json(metadata_json)
            except ValidationError as exc:
                raise ValueError(f"第 {index} 条元数据 JSON 无效：{exc}") from exc
            vector_blob = await vector_file.read()
            if len(vector_blob) == 0:
                raise ValueError(f"第 {index} 条向量文件为空。")
            if len(vector_blob) % 4 != 0:
                raise ValueError(f"第 {index} 条向量文件长度必须是 4 的倍数。")

            prepared_documents.append(
                AssetVectorInput(
                    asset_id=metadata.asset_id,
                    asset_name=metadata.asset_name,
                    asset_format=metadata.asset_format,
                    asset_path=metadata.asset_path,
                    description=metadata.description,
                    source_store_path=metadata.source_store_path,
                    generated_at=metadata.generated_at,
                    embedding_model=metadata.embedding_model,
                    vector=np.frombuffer(vector_blob, dtype=np.float32).copy(),
                )
            )

        return get_search_service().rebuild_index(prepared_documents)
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
