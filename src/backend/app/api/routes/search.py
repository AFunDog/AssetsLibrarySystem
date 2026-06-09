from __future__ import annotations

from fastapi import APIRouter, Depends, HTTPException

from app.application.services.search_service import SearchService
from app.core.dependencies import get_search_service
from app.schemas.search import (
    SearchIndexRequest,
    SearchIndexResponse,
    SearchModelCloseRequest,
    SearchModelCloseResponse,
    SearchModelStatusResponse,
    SearchWarmupResponse,
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

@router.post("/warmup/embedding", response_model=SearchWarmupResponse)
def warmup_embedding(search_service: SearchService = Depends(get_search_service)) -> SearchWarmupResponse:
    try:
        return search_service.warmup_embedding_model()
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.post("/warmup/rerank", response_model=SearchWarmupResponse)
def warmup_rerank(search_service: SearchService = Depends(get_search_service)) -> SearchWarmupResponse:
    try:
        return search_service.warmup_rerank_model()
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.post("/models/close", response_model=SearchModelCloseResponse)
def close_model(
    payload: SearchModelCloseRequest,
    search_service: SearchService = Depends(get_search_service),
) -> SearchModelCloseResponse:
    try:
        return search_service.close_model(payload)
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.get("/models/status", response_model=SearchModelStatusResponse)
def get_model_status(search_service: SearchService = Depends(get_search_service)) -> SearchModelStatusResponse:
    try:
        return search_service.get_model_status()
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
