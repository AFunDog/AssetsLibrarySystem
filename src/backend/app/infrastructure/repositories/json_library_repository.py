from __future__ import annotations

import json
from dataclasses import asdict
from pathlib import Path
from threading import RLock

from app.domain.models.library import AssetLibrary


class JsonLibraryRepository:
    """素材库目录仓储。"""

    def __init__(self, storage_path: str | Path) -> None:
        self.storage_path = Path(storage_path)
        self._lock = RLock()
        self.storage_path.parent.mkdir(parents=True, exist_ok=True)

    def list_libraries(self) -> list[AssetLibrary]:
        with self._lock:
            return [self._library_from_dict(item) for item in self._load_raw()]

    def get_library(self, library_id: str) -> AssetLibrary | None:
        with self._lock:
            for item in self._load_raw():
                if str(item.get("id")) == library_id:
                    return self._library_from_dict(item)
        return None

    def upsert_library(self, library: AssetLibrary) -> AssetLibrary:
        with self._lock:
            records = self._load_raw()
            replaced = False
            for index, item in enumerate(records):
                if str(item.get("id")) == library.id:
                    records[index] = asdict(library)
                    replaced = True
                    break
            if not replaced:
                records.append(asdict(library))
            self._write_raw(records)
        return library

    def _load_raw(self) -> list[dict]:
        if not self.storage_path.exists():
            return []
        raw_text = self.storage_path.read_text(encoding="utf-8").strip()
        if not raw_text:
            return []
        data = json.loads(raw_text)
        if not isinstance(data, list):
            raise ValueError(f"素材库文件格式错误: {self.storage_path}")
        return [item for item in data if isinstance(item, dict)]

    def _write_raw(self, records: list[dict]) -> None:
        temp_path = self.storage_path.with_suffix(self.storage_path.suffix + ".tmp")
        temp_path.write_text(
            json.dumps(records, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        temp_path.replace(self.storage_path)

    @staticmethod
    def _library_from_dict(item: dict) -> AssetLibrary:
        return AssetLibrary(
            id=str(item.get("id") or ""),
            name=str(item.get("name") or ""),
            root_path=str(item.get("root_path") or ""),
            created_at=str(item.get("created_at") or ""),
            updated_at=str(item.get("updated_at") or ""),
        )
