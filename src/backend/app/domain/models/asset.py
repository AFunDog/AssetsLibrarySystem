from __future__ import annotations

from dataclasses import dataclass, field


@dataclass(slots=True)
class Asset:
    id: str
    library_id: str
    library_name: str
    name: str
    asset_type: str
    path: str
    relative_path: str
    description: str = ""
    tags: list[str] = field(default_factory=list)
    status: str = "discovered"
    tagging_provider: str | None = None
    tagging_model: str | None = None
    tagging_description: str | None = None
    tagging_raw_text: str | None = None
    tagged_at: str | None = None
