from fastapi import APIRouter

from app.application.services.tagging_service import TaggingService
from app.schemas.tagging import TaggingAssetRequest, TaggingAssetResponse


router = APIRouter(prefix="/tagging", tags=["tagging"])
tagging_service = TaggingService()


@router.post("/describe", response_model=TaggingAssetResponse)
async def describe_asset(payload: TaggingAssetRequest) -> TaggingAssetResponse:
    return await tagging_service.describe_asset(payload)
