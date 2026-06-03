from fastapi import APIRouter, Depends, HTTPException

from app.application.services.model_service import DEFAULT_PROVIDER_SLOT, ModelService
from app.core.dependencies import get_model_service
from app.schemas.model import (
    ModelCapabilitiesResponse,
    ModelGenerateRequest,
    ModelGenerateResponse,
)


router = APIRouter(prefix="/model", tags=["model"])


@router.get("/capabilities", response_model=ModelCapabilitiesResponse)
def get_capabilities(
    provider_slot: str = DEFAULT_PROVIDER_SLOT,
    model_service: ModelService = Depends(get_model_service),
) -> ModelCapabilitiesResponse:
    try:
        return model_service.get_capabilities(provider_slot)
    except (FileNotFoundError, KeyError, ValueError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.post("/generate", response_model=ModelGenerateResponse)
async def generate_text(
    payload: ModelGenerateRequest,
    model_service: ModelService = Depends(get_model_service),
) -> ModelGenerateResponse:
    try:
        return await model_service.generate_text(payload)
    except (FileNotFoundError, KeyError, ValueError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except RuntimeError as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc
