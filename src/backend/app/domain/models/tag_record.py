from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone


@dataclass(slots=True)
class AssetTagRecord:
    asset_id: str
    source_path: str
    asset_type: str
    provider: str
    model: str
    description: str
    tags: list[str]
    raw_text: str
    tagged_at: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    updated_at: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
