from __future__ import annotations

import json
from dataclasses import asdict
from pathlib import Path
from threading import RLock

from app.domain.models.tag_record import AssetTagRecord


class JsonTagRepository:
    """按文件路径缓存打标结果。"""

    def __init__(self, storage_path: str | Path) -> None:
        self.storage_path = Path(storage_path)
        self._lock = RLock()
        self.storage_path.parent.mkdir(parents=True, exist_ok=True)

    def list_records(self) -> list[AssetTagRecord]:
        with self._lock:
            return [self._record_from_dict(item) for item in self._load_raw()]

    def get_record(self, asset_id: str) -> AssetTagRecord | None:
        with self._lock:
            for item in self._load_raw():
                if str(item.get("asset_id")) == asset_id:
                    return self._record_from_dict(item)
        return None

    def upsert_record(self, record: AssetTagRecord) -> AssetTagRecord:
        with self._lock:
            records = self._load_raw()
            replaced = False
            for index, item in enumerate(records):
                if str(item.get("asset_id")) == record.asset_id:
                    records[index] = asdict(record)
                    replaced = True
                    break
            if not replaced:
                records.append(asdict(record))
            self._write_raw(records)
        return record

    def _load_raw(self) -> list[dict]:
        if not self.storage_path.exists():
            return []
        raw_text = self.storage_path.read_text(encoding="utf-8").strip()
        if not raw_text:
            return []
        data = json.loads(raw_text)
        if not isinstance(data, list):
            raise ValueError(f"打标缓存文件格式错误: {self.storage_path}")
        return [item for item in data if isinstance(item, dict)]

    def _write_raw(self, records: list[dict]) -> None:
        temp_path = self.storage_path.with_suffix(self.storage_path.suffix + ".tmp")
        temp_path.write_text(
            json.dumps(records, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        temp_path.replace(self.storage_path)

    @staticmethod
    def _record_from_dict(item: dict) -> AssetTagRecord:
        return AssetTagRecord(
            asset_id=str(item.get("asset_id") or ""),
            source_path=str(item.get("source_path") or ""),
            asset_type=str(item.get("asset_type") or ""),
            provider=str(item.get("provider") or ""),
            model=str(item.get("model") or ""),
            description=str(item.get("description") or ""),
            tags=list(item.get("tags") or []),
            raw_text=str(item.get("raw_text") or ""),
            tagged_at=str(item.get("tagged_at") or ""),
            updated_at=str(item.get("updated_at") or ""),
        )
