from app.infrastructure.repositories.asset_repository import InMemoryAssetRepository
from app.schemas.asset import AssetItem, AssetListResponse


class AssetService:
    """素材管理用例层占位实现。"""

    def __init__(self) -> None:
        self.repository = InMemoryAssetRepository()

    def list_assets(self) -> AssetListResponse:
        assets = [
            AssetItem(
                id=item.id,
                name=item.name,
                asset_type=item.asset_type,
                path=item.path,
                description=item.description,
                tags=item.tags,
                status=item.status,
            )
            for item in self.repository.list_assets()
        ]
        return AssetListResponse(items=assets, total=len(assets), stage="skeleton")
