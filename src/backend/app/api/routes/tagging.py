from fastapi import APIRouter, HTTPException

from app.application.services.tagging_service import TaggingService
from app.schemas.tagging import TaggingAssetRequest, TaggingAssetResponse


router = APIRouter(prefix="/tagging", tags=["tagging"])
tagging_service = TaggingService()


@router.post("/describe", response_model=TaggingAssetResponse)
async def describe_asset(payload: TaggingAssetRequest) -> TaggingAssetResponse:
    try:
        return await tagging_service.describe_asset(payload)
    except (FileNotFoundError, ValueError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
