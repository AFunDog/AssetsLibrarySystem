from __future__ import annotations

from dataclasses import dataclass, field


@dataclass(slots=True)
class TaggingAsset:
    asset_id: str
    asset_type: str
    source_path: str | None = None
    text: str | None = None
    media_mime_type: str | None = None
    title: str | None = None
    description: str | None = None
    tags: list[str] = field(default_factory=list)
