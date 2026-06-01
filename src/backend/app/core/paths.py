from __future__ import annotations

from pathlib import Path


BACKEND_ROOT = Path(__file__).resolve().parents[2]
REPO_ROOT = Path(__file__).resolve().parents[4]
SHARED_DATA_DIR = REPO_ROOT / "data"


def ensure_shared_data_dir() -> Path:
    SHARED_DATA_DIR.mkdir(parents=True, exist_ok=True)
    return SHARED_DATA_DIR
