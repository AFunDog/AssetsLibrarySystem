from __future__ import annotations

from pathlib import Path

from app.domain.models.tagging import TaggingAsset
from app.infrastructure.tagging.asset_describer import build_asset_describer
from app.schemas.tagging import TaggingAssetRequest, TaggingAssetResponse


class TaggingService:
    """素材打标用例层。"""

    def __init__(
        self,
        providers_path: str | Path | None = None,
        prompts_path: str | Path | None = None,
        provider_slot: str = "asset_describer",
    ) -> None:
        backend_root = Path(__file__).resolve().parents[3]
        self._describer = build_asset_describer(
            providers_path=providers_path or (backend_root / "configs/providers.yaml"),
            prompts_path=prompts_path or (backend_root / "configs/prompts.yaml"),
            provider_slot=provider_slot,
        )

    async def describe_asset(self, payload: TaggingAssetRequest) -> TaggingAssetResponse:
        asset = TaggingAsset(
            asset_id=payload.asset_id,
            asset_type=payload.asset_type,
            source_path=payload.source_path,
            text=payload.text,
            media_mime_type=payload.media_mime_type,
            title=payload.title,
        )
        tagged = await self._describer.describe(asset)
        return TaggingAssetResponse(
            asset_id=tagged.asset_id,
            asset_type=tagged.asset_type,
            provider=tagged.provider,
            model=tagged.model,
            description=tagged.description,
            tags=tagged.tags,
            raw_text=tagged.raw_text,
            stage="dashscope",
        )
