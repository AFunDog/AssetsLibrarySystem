from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
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
class AssetVectorInput:
    asset_id: str
    asset_name: str
    asset_format: str
    asset_path: str
    description: str
    source_store_path: str | None
    generated_at: datetime | None
    embedding_model: str
    vector: np.ndarray


@dataclass(slots=True)
class IndexState:
    document_count: int
    latest_updated_at: str


class SqliteVectorRepository:
    def __init__(self, database_path: str | Path) -> None:
        self.database_path = Path(database_path)
        self.database_path.parent.mkdir(parents=True, exist_ok=True)
        self._initialize()

    def upsert_document(
        self,
        *,
        asset_id: str,
        asset_name: str,
        asset_format: str,
        asset_path: str,
        description: str,
        source_store_path: str | None,
        generated_at: datetime | None,
        embedding_model: str,
        vector: np.ndarray,
    ) -> tuple[int, datetime]:
        serialized_vector = np.asarray(vector, dtype=np.float32).tobytes()
        vector_dim = int(vector.shape[0])
        now = datetime.now(timezone.utc)

        with self._connect() as connection:
            cursor = connection.execute(
                """
                INSERT INTO asset_vectors (
                    asset_id,
                    asset_name,
                    asset_format,
                    asset_path,
                    description,
                    source_store_path,
                    generated_at,
                    embedding_model,
                    vector_dim,
                    vector_blob,
                    updated_at
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ON CONFLICT(asset_id) DO UPDATE SET
                    asset_name = excluded.asset_name,
                    asset_format = excluded.asset_format,
                    asset_path = excluded.asset_path,
                    description = excluded.description,
                    source_store_path = excluded.source_store_path,
                    generated_at = excluded.generated_at,
                    embedding_model = excluded.embedding_model,
                    vector_dim = excluded.vector_dim,
                    vector_blob = excluded.vector_blob,
                    updated_at = excluded.updated_at
                RETURNING doc_id
                """,
                (
                    asset_id,
                    asset_name,
                    asset_format,
                    asset_path,
                    description,
                    source_store_path,
                    generated_at.isoformat() if generated_at is not None else None,
                    embedding_model,
                    vector_dim,
                    sqlite3.Binary(serialized_vector),
                    now.isoformat(),
                ),
            )
            row = cursor.fetchone()
            if row is None:
                raise RuntimeError("素材向量入库失败。")
            return int(row[0]), now

    def replace_documents(self, documents: list[AssetVectorInput]) -> list[IndexedAssetVectorRecord]:
        if not documents:
            raise ValueError("没有可写入的向量数据。")

        records: list[IndexedAssetVectorRecord] = []
        with self._connect() as connection:
            connection.execute("DELETE FROM asset_vectors")
            connection.execute("DELETE FROM sqlite_sequence WHERE name = 'asset_vectors'")

            for document in documents:
                serialized_vector = np.asarray(document.vector, dtype=np.float32).tobytes()
                vector_dim = int(document.vector.shape[0])
                now = datetime.now(timezone.utc)

                cursor = connection.execute(
                    """
                    INSERT INTO asset_vectors (
                        asset_id,
                        asset_name,
                        asset_format,
                        asset_path,
                        description,
                        source_store_path,
                        generated_at,
                        embedding_model,
                        vector_dim,
                        vector_blob,
                        updated_at
                    )
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    RETURNING doc_id
                    """,
                    (
                        document.asset_id,
                        document.asset_name,
                        document.asset_format,
                        document.asset_path,
                        document.description,
                        document.source_store_path,
                        document.generated_at.isoformat() if document.generated_at is not None else None,
                        document.embedding_model,
                        vector_dim,
                        sqlite3.Binary(serialized_vector),
                        now.isoformat(),
                    ),
                )
                row = cursor.fetchone()
                if row is None:
                    raise RuntimeError("素材向量重建失败。")

                records.append(
                    IndexedAssetVectorRecord(
                        doc_id=int(row[0]),
                        asset_id=document.asset_id,
                        asset_name=document.asset_name,
                        asset_format=document.asset_format,
                        asset_path=document.asset_path,
                        description=document.description,
                        source_store_path=document.source_store_path,
                        generated_at=document.generated_at,
                        embedding_model=document.embedding_model,
                        vector=np.asarray(document.vector, dtype=np.float32).copy(),
                        updated_at=now,
                    )
                )

        return records

    def list_documents(self) -> list[IndexedAssetVectorRecord]:
        with self._connect() as connection:
            rows = connection.execute(
                """
                SELECT
                    doc_id,
                    asset_id,
                    asset_name,
                    asset_format,
                    asset_path,
                    description,
                    source_store_path,
                    generated_at,
                    embedding_model,
                    vector_dim,
                    vector_blob,
                    updated_at
                FROM asset_vectors
                ORDER BY doc_id
                """
            ).fetchall()

        records: list[IndexedAssetVectorRecord] = []
        for row in rows:
            generated_at = datetime.fromisoformat(row["generated_at"]) if row["generated_at"] else None
            updated_at = datetime.fromisoformat(row["updated_at"])
            vector = np.frombuffer(row["vector_blob"], dtype=np.float32, count=int(row["vector_dim"])).copy()
            records.append(
                IndexedAssetVectorRecord(
                    doc_id=int(row["doc_id"]),
                    asset_id=str(row["asset_id"]),
                    asset_name=str(row["asset_name"]),
                    asset_format=str(row["asset_format"]),
                    asset_path=str(row["asset_path"]),
                    description=str(row["description"]),
                    source_store_path=row["source_store_path"],
                    generated_at=generated_at,
                    embedding_model=str(row["embedding_model"]),
                    vector=vector,
                    updated_at=updated_at,
                )
            )

        return records

    def get_state(self) -> IndexState:
        with self._connect() as connection:
            row = connection.execute(
                """
                SELECT
                    COUNT(*) AS document_count,
                    COALESCE(MAX(updated_at), '') AS latest_updated_at
                FROM asset_vectors
                """
            ).fetchone()

        return IndexState(
            document_count=int(row["document_count"]),
            latest_updated_at=str(row["latest_updated_at"]),
        )

    def _initialize(self) -> None:
        with self._connect() as connection:
            connection.execute(
                """
                CREATE TABLE IF NOT EXISTS asset_vectors (
                    doc_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    asset_id TEXT NOT NULL UNIQUE,
                    asset_name TEXT NOT NULL,
                    asset_format TEXT NOT NULL,
                    asset_path TEXT NOT NULL,
                    description TEXT NOT NULL,
                    source_store_path TEXT NULL,
                    generated_at TEXT NULL,
                    embedding_model TEXT NOT NULL,
                    vector_dim INTEGER NOT NULL,
                    vector_blob BLOB NOT NULL,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT NOT NULL
                )
                """
            )

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database_path)
        connection.row_factory = sqlite3.Row
        return connection
