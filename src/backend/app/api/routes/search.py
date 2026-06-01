from fastapi import APIRouter, HTTPException

from app.application.services.search_service import SearchService
from app.schemas.search import (
    SearchIndexRequest,
    SearchIndexResponse,
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
