from __future__ import annotations

import asyncio
import tempfile
import unittest
from pathlib import Path

from app.application.services.asset_service import AssetService
from app.application.services.tagging_service import TaggingService
from app.domain.models.tagging import TaggingAsset
from app.infrastructure.tagging.asset_describer import TaggedAsset
from app.schemas.library import LibraryCreateRequest
from app.schemas.tagging import TaggingAssetRequest


class FakeDescriber:
    async def describe(self, asset: TaggingAsset) -> TaggedAsset:
        return TaggedAsset(
            asset_id=asset.asset_id,
            asset_type=asset.asset_type,
            provider="fake-provider",
            model="fake-model",
            description="角色表情紧张，动作较克制，适合轻微受惊场景",
            tags=["紧张", "克制", "轻微受惊"],
            raw_text='{"description":"角色表情紧张，动作较克制，适合轻微受惊场景","tags":["紧张","克制","轻微受惊"]}',
        )


class AssetFlowTestCase(unittest.TestCase):
    def test_library_registration_and_filesystem_scan(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            library_dir = root / "library"
            library_dir.mkdir()
            (library_dir / "a.txt").write_text("hello", encoding="utf-8")
            (library_dir / "b.png").write_bytes(b"fake")
            (library_dir / "ignore.bin").write_bytes(b"nope")

            service = AssetService(
                library_store_path=root / "libraries.json",
                tag_store_path=root / "asset_tags.json",
            )
            created = service.create_library(
                LibraryCreateRequest(
                    id="demo-library",
                    name="演示素材库",
                    root_path=str(library_dir),
                )
            )

            self.assertEqual(created.id, "demo-library")
            libraries = service.list_libraries()
            self.assertEqual(libraries.total, 1)

            assets = service.list_assets()
            self.assertEqual(assets.total, 2)
            self.assertEqual({item.relative_path for item in assets.items}, {"a.txt", "b.png"})

    def test_tagging_result_is_cached_and_reflected_in_scanned_assets(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            library_dir = root / "library"
            library_dir.mkdir()
            target_file = library_dir / "pose.txt"
            target_file.write_text("角色站立，神情紧张", encoding="utf-8")

            asset_service = AssetService(
                library_store_path=root / "libraries.json",
                tag_store_path=root / "asset_tags.json",
            )
            asset_service.create_library(
                LibraryCreateRequest(
                    id="demo-library",
                    name="演示素材库",
                    root_path=str(library_dir),
                )
            )

            scanned_before = asset_service.list_assets().items
            self.assertEqual(len(scanned_before), 1)
            self.assertEqual(scanned_before[0].status, "discovered")

            tagging_service = TaggingService(
                asset_service=asset_service,
                describer=FakeDescriber(),
            )
            response = asyncio.run(
                tagging_service.describe_asset(
                    TaggingAssetRequest(
                        asset_id=scanned_before[0].id,
                        asset_type="text",
                        source_path=str(target_file),
                        text="角色站立，神情紧张",
                        title="pose.txt",
                    )
                )
            )

            self.assertEqual(response.provider, "fake-provider")
            self.assertEqual(response.tags, ["紧张", "克制", "轻微受惊"])

            scanned_after = asset_service.list_assets().items
            self.assertEqual(scanned_after[0].status, "tagged")
            self.assertEqual(scanned_after[0].description, "角色表情紧张，动作较克制，适合轻微受惊场景")
            self.assertEqual(scanned_after[0].tags, ["紧张", "克制", "轻微受惊"])
            self.assertIsNotNone(scanned_after[0].tagging)
            self.assertEqual(scanned_after[0].tagging.provider, "fake-provider")


if __name__ == "__main__":
    unittest.main()
