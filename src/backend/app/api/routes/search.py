from __future__ import annotations

from fastapi import APIRouter, HTTPException

from app.application.services.search_service import SearchService
from app.schemas.search import (
    SearchIndexRequest,
    SearchIndexResponse,
    SearchExploreRequest,
    SearchExploreResponse,
    SearchModelCloseRequest,
    SearchModelCloseResponse,
    SearchModelStatusResponse,
    SearchReindexResponse,
    SearchWarmupResponse,
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


@router.post("/explore", response_model=SearchExploreResponse)
def explore(payload: SearchExploreRequest) -> SearchExploreResponse:
    try:
        return get_search_service().explore(payload)
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.post("/reindex", response_model=SearchReindexResponse)
def reindex() -> SearchReindexResponse:
    try:
        return get_search_service().rebuild_index()
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.post("/warmup/embedding", response_model=SearchWarmupResponse)
def warmup_embedding() -> SearchWarmupResponse:
    try:
        return get_search_service().warmup_embedding_model()
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.post("/warmup/rerank", response_model=SearchWarmupResponse)
def warmup_rerank() -> SearchWarmupResponse:
    try:
        return get_search_service().warmup_rerank_model()
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.post("/models/close", response_model=SearchModelCloseResponse)
def close_model(payload: SearchModelCloseRequest) -> SearchModelCloseResponse:
    try:
        return get_search_service().close_model(payload)
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.get("/models/status", response_model=SearchModelStatusResponse)
def get_model_status() -> SearchModelStatusResponse:
    try:
        return get_search_service().get_model_status()
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
