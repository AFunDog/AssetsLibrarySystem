from fastapi import APIRouter, HTTPException

from app.application.services.library_service import LibraryService
from app.schemas.library import LibraryCreateRequest, LibraryItem, LibraryListResponse


router = APIRouter(prefix="/libraries", tags=["libraries"])
library_service = LibraryService()


@router.get("", response_model=LibraryListResponse)
def list_libraries() -> LibraryListResponse:
    return library_service.list_libraries()


@router.post("", response_model=LibraryItem, status_code=201)
def create_library(payload: LibraryCreateRequest) -> LibraryItem:
    try:
        return library_service.create_library(payload)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
