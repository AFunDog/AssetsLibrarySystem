from pydantic import BaseModel, Field


class LibraryCreateRequest(BaseModel):
    id: str = Field(..., min_length=1, description="素材库唯一 ID")
    name: str = Field(..., min_length=1, description="素材库名称")
    root_path: str = Field(..., min_length=1, description="素材库根目录绝对路径")


class LibraryItem(BaseModel):
    id: str
    name: str
    root_path: str
    created_at: str
    updated_at: str


class LibraryListResponse(BaseModel):
    items: list[LibraryItem]
    total: int
    stage: str


class DirectoryEntry(BaseModel):
    name: str
    path: str


class DriveEntry(BaseModel):
    path: str
    label: str


class BrowseResponse(BaseModel):
    current: str
    parent: str | None
    entries: list[DirectoryEntry]
