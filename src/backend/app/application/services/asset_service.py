from __future__ import annotations

from pathlib import Path

from app.domain.models.asset import Asset
from app.domain.models.library import AssetLibrary
from app.domain.models.tag_record import AssetTagRecord
from app.infrastructure.repositories.json_library_repository import JsonLibraryRepository
from app.infrastructure.repositories.json_tag_repository import JsonTagRepository
from app.schemas.asset import AssetItem, AssetListResponse, AssetTaggingInfo
from app.schemas.library import LibraryCreateRequest, LibraryItem, LibraryListResponse

TEXT_SUFFIXES = {".txt", ".md", ".json", ".yaml", ".yml", ".csv"}
IMAGE_SUFFIXES = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"}
VIDEO_SUFFIXES = {".mp4", ".mov", ".webm", ".mkv", ".avi", ".m4v"}
MUSIC_SUFFIXES = {".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac"}


class AssetService:
    """素材库目录管理与素材扫描用例层。"""

    def __init__(
        self,
        library_repository: JsonLibraryRepository | None = None,
        tag_repository: JsonTagRepository | None = None,
        library_store_path: str | Path | None = None,
        tag_store_path: str | Path | None = None,
    ) -> None:
        backend_root = Path(__file__).resolve().parents[3]
        self.library_repository = library_repository or JsonLibraryRepository(
            library_store_path or backend_root / "data" / "libraries.json"
        )
        self.tag_repository = tag_repository or JsonTagRepository(
            tag_store_path or backend_root / "data" / "asset_tags.json"
        )

    def create_library(self, payload: LibraryCreateRequest) -> LibraryItem:
        root_path = Path(payload.root_path).expanduser().resolve()
        if not root_path.exists() or not root_path.is_dir():
            raise ValueError(f"素材库目录不存在或不是目录: {root_path}")

        existing = self.library_repository.get_library(payload.id)
        if existing is not None:
            raise ValueError(f"素材库已存在: {payload.id}")

        library = AssetLibrary(
            id=payload.id,
            name=payload.name,
            root_path=str(root_path),
        )
        self.library_repository.upsert_library(library)
        return self._to_library_item(library)

    def list_libraries(self) -> LibraryListResponse:
        libraries = [self._to_library_item(item) for item in self.library_repository.list_libraries()]
        return LibraryListResponse(items=libraries, total=len(libraries), stage="persistent-json")

    def list_assets(self, library_id: str | None = None) -> AssetListResponse:
        libraries = self.library_repository.list_libraries()
        if library_id:
            libraries = [item for item in libraries if item.id == library_id]

        tag_map = {record.asset_id: record for record in self.tag_repository.list_records()}
        assets: list[Asset] = []
        for library in libraries:
            assets.extend(self._scan_library(library, tag_map))

        assets.sort(key=lambda item: (item.library_name.lower(), item.relative_path.lower()))
        return AssetListResponse(
            items=[self._to_asset_item(item) for item in assets],
            total=len(assets),
            stage="filesystem-scan",
        )

    def cache_tagging_result(
        self,
        *,
        asset_id: str,
        source_path: str,
        asset_type: str,
        provider: str,
        model: str,
        description: str,
        tags: list[str],
        raw_text: str,
    ) -> AssetTagRecord:
        record = AssetTagRecord(
            asset_id=asset_id,
            source_path=source_path,
            asset_type=asset_type,
            provider=provider,
            model=model,
            description=description,
            tags=list(tags),
            raw_text=raw_text,
        )
        return self.tag_repository.upsert_record(record)

    def _scan_library(
        self,
        library: AssetLibrary,
        tag_map: dict[str, AssetTagRecord],
    ) -> list[Asset]:
        root_path = Path(library.root_path)
        if not root_path.exists():
            return []

        discovered: list[Asset] = []
        for path in sorted(item for item in root_path.rglob("*") if item.is_file()):
            asset_type = self._detect_asset_type(path)
            if asset_type is None:
                continue

            relative_path = path.relative_to(root_path).as_posix()
            asset_id = f"{library.id}:{relative_path}"
            tag_record = tag_map.get(asset_id)

            discovered.append(
                Asset(
                    id=asset_id,
                    library_id=library.id,
                    library_name=library.name,
                    name=path.name,
                    asset_type=asset_type,
                    path=str(path),
                    relative_path=relative_path,
                    description=tag_record.description if tag_record else "",
                    tags=list(tag_record.tags) if tag_record else [],
                    status="tagged" if tag_record else "discovered",
                    tagging_provider=tag_record.provider if tag_record else None,
                    tagging_model=tag_record.model if tag_record else None,
                    tagging_description=tag_record.description if tag_record else None,
                    tagging_raw_text=tag_record.raw_text if tag_record else None,
                    tagged_at=tag_record.tagged_at if tag_record else None,
                )
            )

        return discovered

    @staticmethod
    def _detect_asset_type(path: Path) -> str | None:
        suffix = path.suffix.lower()
        if suffix in TEXT_SUFFIXES:
            return "text"
        if suffix in IMAGE_SUFFIXES:
            return "image"
        if suffix in VIDEO_SUFFIXES:
            return "video"
        if suffix in MUSIC_SUFFIXES:
            return "music"
        return None

    @staticmethod
    def _to_library_item(library: AssetLibrary) -> LibraryItem:
        return LibraryItem(
            id=library.id,
            name=library.name,
            root_path=library.root_path,
            created_at=library.created_at,
            updated_at=library.updated_at,
        )

    @staticmethod
    def _to_asset_item(asset: Asset) -> AssetItem:
        return AssetItem(
            id=asset.id,
            library_id=asset.library_id,
            library_name=asset.library_name,
            name=asset.name,
            asset_type=asset.asset_type,
            path=asset.path,
            relative_path=asset.relative_path,
            description=asset.description,
            tags=list(asset.tags),
            status=asset.status,
            tagging=AssetTaggingInfo(
                provider=asset.tagging_provider,
                model=asset.tagging_model,
                description=asset.tagging_description,
                tags=list(asset.tags),
                raw_text=asset.tagging_raw_text,
                tagged_at=asset.tagged_at,
            )
            if asset.tagged_at
            else None,
        )
