from __future__ import annotations

from fastapi import APIRouter, Depends, HTTPException

from app.application.services.search_service import SearchService
from app.core.dependencies import get_search_service
from app.schemas.search import (
    SearchIndexRequest,
    SearchIndexResponse,
    SearchQueryRequest,
    SearchQueryResponse,
)


router = APIRouter(prefix="/search", tags=["search"])


@router.post("/index", response_model=SearchIndexResponse)
def index_description(
    payload: SearchIndexRequest,
    search_service: SearchService = Depends(get_search_service),
) -> SearchIndexResponse:
    try:
        return search_service.vectorize(payload)
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.post("/query", response_model=SearchQueryResponse)
def search(
    payload: SearchQueryRequest,
    search_service: SearchService = Depends(get_search_service),
) -> SearchQueryResponse:
    try:
        return search_service.rerank(payload)
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc