from __future__ import annotations

from pathlib import Path
from typing import Protocol

from app.application.services.asset_service import AssetService
from app.domain.models.tagging import TaggingAsset
from app.infrastructure.tagging.asset_describer import (
    DEFAULT_PROMPTS_PATH,
    DEFAULT_PROVIDER_PATH,
    DEFAULT_PROVIDER_SLOT,
    TaggedAsset,
    build_asset_describer,
)
from app.schemas.tagging import TaggingAssetRequest, TaggingAssetResponse


class AssetDescriberProtocol(Protocol):
    async def describe(self, asset: TaggingAsset) -> TaggedAsset:
        """为单个素材生成打标结果。"""


class TaggingService:
    """素材打标与结果缓存用例层。"""

    def __init__(
        self,
        asset_service: AssetService | None = None,
        describer: AssetDescriberProtocol | None = None,
        providers_path: str | Path | None = None,
        prompts_path: str | Path | None = None,
        provider_slot: str = DEFAULT_PROVIDER_SLOT,
    ) -> None:
        self.asset_service = asset_service or AssetService()
        self._describer = describer or build_asset_describer(
            providers_path=providers_path or DEFAULT_PROVIDER_PATH,
            prompts_path=prompts_path or DEFAULT_PROMPTS_PATH,
            provider_slot=provider_slot,
        )

    async def describe_asset(self, payload: TaggingAssetRequest) -> TaggingAssetResponse:
        tagged = await self._describer.describe(
            TaggingAsset(
                asset_id=payload.asset_id,
                asset_type=payload.asset_type,
                source_path=payload.source_path,
                text=payload.text,
                media_mime_type=payload.media_mime_type,
                title=payload.title,
            )
        )

        self.asset_service.cache_tagging_result(
            asset_id=payload.asset_id,
            source_path=payload.source_path,
            asset_type=payload.asset_type,
            provider=tagged.provider,
            model=tagged.model,
            description=tagged.description,
            tags=tagged.tags,
            raw_text=tagged.raw_text,
        )
        return TaggingAssetResponse(
            asset_id=payload.asset_id,
            asset_type=payload.asset_type,
            source_path=payload.source_path,
            provider=tagged.provider,
            model=tagged.model,
            description=tagged.description,
            tags=tagged.tags,
            raw_text=tagged.raw_text,
            stage="cached",
        )
