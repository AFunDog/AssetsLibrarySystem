import string
import sys
from pathlib import Path

from fastapi import APIRouter, HTTPException

from app.application.services.library_service import LibraryService
from app.schemas.library import (
    BrowseResponse,
    DirectoryEntry,
    DriveEntry,
    LibraryCreateRequest,
    LibraryItem,
    LibraryListResponse,
)


router = APIRouter(prefix="/libraries", tags=["libraries"])
library_service = LibraryService()


@router.get("", response_model=LibraryListResponse)
def list_libraries() -> LibraryListResponse:
    return library_service.list_libraries()


@router.post("", response_model=LibraryItem, status_code=201)
def create_library(payload: LibraryCreateRequest) -> LibraryItem:
    try:
        return library_service.create_library(payload)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.get("/drives")
def list_drives() -> list[DriveEntry]:
    """列出本机可用盘符（仅 Windows 有效，其他系统返回根目录）。"""
    drives: list[DriveEntry] = []
    if sys.platform == "win32":
        for letter in string.ascii_uppercase:
            drive = f"{letter}:\\"
            if Path(drive).exists():
                drives.append(DriveEntry(path=drive, label=drive))
    else:
        drives.append(DriveEntry(path="/", label="/"))
    return drives


@router.get("/browse")
def browse_directory(path: str = "") -> BrowseResponse:
    """列出指定路径下的子目录，用于前端文件夹选择器。"""
    if not path:
        # 返回盘符列表作为根
        drives: list[DirectoryEntry] = []
        if sys.platform == "win32":
            for letter in string.ascii_uppercase:
                drive = f"{letter}:\\"
                if Path(drive).exists():
                    drives.append(DirectoryEntry(name=f"{letter}:", path=drive))
        else:
            drives.append(DirectoryEntry(name="/", path="/"))
        return BrowseResponse(current="", parent=None, entries=drives)

    target = Path(path).expanduser().resolve()
    if not target.exists():
        raise HTTPException(status_code=404, detail=f"路径不存在: {target}")
    if not target.is_dir():
        raise HTTPException(status_code=400, detail=f"不是目录: {target}")

    entries: list[DirectoryEntry] = []
    try:
        for item in sorted(target.iterdir()):
            if item.is_dir() and not item.name.startswith("."):
                entries.append(DirectoryEntry(name=item.name, path=str(item)))
    except PermissionError:
        raise HTTPException(status_code=403, detail=f"无权限访问: {target}")

    parent = str(target.parent) if target.parent != target else None
    return BrowseResponse(current=str(target), parent=parent, entries=entries)
