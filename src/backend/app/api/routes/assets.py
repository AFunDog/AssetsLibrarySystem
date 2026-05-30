from fastapi import APIRouter

from app.application.services.asset_service import AssetService
from app.schemas.asset import AssetListResponse


router = APIRouter(prefix="/assets", tags=["assets"])
asset_service = AssetService()


@router.get("", response_model=AssetListResponse)
def list_assets() -> AssetListResponse:
    return asset_service.list_assets()
