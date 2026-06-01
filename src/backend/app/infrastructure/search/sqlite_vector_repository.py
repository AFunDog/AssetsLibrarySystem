from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
import sqlite3

import numpy as np


@dataclass(slots=True)
class IndexedAssetVectorRecord:
    doc_id: int
    asset_id: str
    asset_name: str
    asset_format: str
    asset_path: str
    description: str
    source_store_path: str | None
    generated_at: datetime | None
    embedding_model: str
    vector: np.ndarray
    updated_at: datetime


@dataclass(slots=True)
class IndexState:
    document_count: int
    latest_updated_at: str


class SqliteVectorRepository:
    def __init__(self, database_path: str | Path) -> None:
        self.database_path = Path(database_path)
        self.database_path.parent.mkdir(parents=True, exist_ok=True)

    def list_documents(self) -> list[IndexedAssetVectorRecord]:
        if not self._has_source_table():
            return []

        with self._connect() as connection:
            rows = connection.execute(
                """
                SELECT
                    asset_id,
                    asset_name,
                    asset_type,
                    asset_path,
                    description,
                    description_store_path,
                    embedding_model,
                    vector_dim,
                    vector_blob,
                    vectorized_at
                FROM asset_description_vectors
                ORDER BY vectorized_at, asset_id
                """
            ).fetchall()

        records: list[IndexedAssetVectorRecord] = []
        for row in rows:
            updated_at = datetime.fromisoformat(row["vectorized_at"])
            vector = np.frombuffer(row["vector_blob"], dtype=np.float32, count=int(row["vector_dim"])).copy()
            records.append(
                IndexedAssetVectorRecord(
                    doc_id=len(records) + 1,
                    asset_id=str(row["asset_id"]),
                    asset_name=str(row["asset_name"]),
                    asset_format=str(row["asset_type"]),
                    asset_path=str(row["asset_path"]),
                    description=str(row["description"]),
                    source_store_path=row["description_store_path"],
                    generated_at=None,
                    embedding_model=str(row["embedding_model"]),
                    vector=vector,
                    updated_at=updated_at,
                )
            )

        return records

    def get_state(self) -> IndexState:
        if not self._has_source_table():
            return IndexState(document_count=0, latest_updated_at="")

        with self._connect() as connection:
            row = connection.execute(
                """
                SELECT
                    COUNT(*) AS document_count,
                    COALESCE(MAX(vectorized_at), '') AS latest_updated_at
                FROM asset_description_vectors
                """
            ).fetchone()

        return IndexState(
            document_count=int(row["document_count"]),
            latest_updated_at=str(row["latest_updated_at"]),
        )

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database_path)
        connection.row_factory = sqlite3.Row
        return connection

    def _has_source_table(self) -> bool:
        if not self.database_path.exists():
            return False

        with self._connect() as connection:
            row = connection.execute(
                """
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = 'asset_description_vectors'
                LIMIT 1
                """
            ).fetchone()
        return row is not None
