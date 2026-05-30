from dataclasses import dataclass, field


@dataclass(slots=True)
class Asset:
    id: str
    name: str
    asset_type: str
    path: str
    description: str = ""
    tags: list[str] = field(default_factory=list)
    status: str = "draft"
