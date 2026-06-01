from __future__ import annotations

from functools import lru_cache
import sys
from pathlib import Path

from app.core.config import get_settings


BACKEND_ROOT = Path(__file__).resolve().parents[2]
REPO_ROOT = Path(__file__).resolve().parents[4]


@lru_cache(maxsize=1)
def resolve_data_root() -> Path:
    settings = get_settings()

    if settings.data_root and settings.data_root.strip():
        return Path(settings.data_root).expanduser().resolve()

    if settings.app_env.strip().lower() in {"dev", "development", "debug"}:
        return REPO_ROOT / "data"

    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent / "data"

    return BACKEND_ROOT / "data"


def ensure_shared_data_dir() -> Path:
    data_root = resolve_data_root()
    data_root.mkdir(parents=True, exist_ok=True)
    return data_root
