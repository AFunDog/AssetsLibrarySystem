from fastapi import APIRouter

from app.application.services.search_service import SearchService
from app.schemas.search import SearchRequest, SearchResponse


router = APIRouter(prefix="/search", tags=["search"])
search_service = SearchService()


@router.post("", response_model=SearchResponse)
def search_assets(payload: SearchRequest) -> SearchResponse:
    return search_service.search(payload)
