from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
import json
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
    tags: list[str]
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

        has_assets_table = self._has_table("assets")
        has_metadata_table = self._has_table("asset_metadata")
        has_description_table = self._has_table("asset_descriptions")

        asset_name_expr = "v.asset_name"
        asset_type_expr = "v.asset_type"
        asset_path_expr = "v.asset_path"
        description_expr = "v.description"
        tags_expr = "'[]'"
        generated_at_expr = "NULL"

        joins: list[str] = []
        if has_assets_table:
            joins.append("LEFT JOIN assets AS a ON a.asset_uid = v.asset_id")
            asset_name_expr = "COALESCE(a.asset_name, v.asset_name)"
            asset_type_expr = "COALESCE(a.asset_type, v.asset_type)"
            asset_path_expr = "COALESCE(a.current_path, v.asset_path)"

        if has_metadata_table:
            joins.append("LEFT JOIN asset_metadata AS m ON m.asset_uid = v.asset_id")
            tags_expr = "COALESCE(m.tags_json, '[]')"

        if has_description_table:
            joins.append("LEFT JOIN asset_descriptions AS d ON d.asset_id = v.asset_id")
            description_expr = "d.description"
            generated_at_expr = "d.generated_at"

        with self._connect() as connection:
            rows = connection.execute(
                f"""
                SELECT
                    v.rowid AS vector_id,
                    asset_id,
                    {asset_name_expr} AS asset_name,
                    {asset_type_expr} AS asset_type,
                    {asset_path_expr} AS asset_path,
                    {description_expr} AS description,
                    {tags_expr} AS tags_json,
                    v.description_store_path,
                    {generated_at_expr} AS generated_at,
                    embedding_model,
                    vector_dim,
                    vector_blob,
                    vectorized_at
                FROM asset_description_vectors
                {' '.join(joins)}
                ORDER BY vectorized_at, asset_id
                """
            ).fetchall()

        records: list[IndexedAssetVectorRecord] = []
        for row in rows:
            updated_at = datetime.fromisoformat(row["vectorized_at"])
            vector = np.frombuffer(row["vector_blob"], dtype=np.float32, count=int(row["vector_dim"])).copy()
            generated_at_value = row["generated_at"]
            try:
                tags = json.loads(row["tags_json"] or "[]")
            except json.JSONDecodeError:
                tags = []
            records.append(
                IndexedAssetVectorRecord(
                    doc_id=int(row["vector_id"]),
                    asset_id=str(row["asset_id"]),
                    asset_name=str(row["asset_name"]),
                    asset_format=str(row["asset_type"]),
                    asset_path=str(row["asset_path"]),
                    description=str(row["description"]),
                    tags=[str(item) for item in tags] if isinstance(tags, list) else [],
                    source_store_path=row["description_store_path"],
                    generated_at=datetime.fromisoformat(generated_at_value) if generated_at_value else None,
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
        return self._has_table("asset_description_vectors")

    def _has_table(self, table_name: str) -> bool:
        if not self.database_path.exists():
            return False

        with self._connect() as connection:
            row = connection.execute(
                """
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = ?
                LIMIT 1
                """,
                (table_name,),
            ).fetchone()
        return row is not None
