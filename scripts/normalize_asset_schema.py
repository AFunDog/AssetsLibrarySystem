#!/usr/bin/env python3
"""One-time normalization for the asset database schema.

This script removes duplicated text copies from:
- asset_metadata.description_text
- asset_description_vectors.asset_name / asset_type / asset_path / description
- asset_locations

The authoritative description remains in asset_descriptions, and the
authoritative asset identity remains in assets.
"""

from __future__ import annotations

import argparse
import os
import shutil
import sqlite3
from datetime import datetime, timezone
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description="Normalize duplicated asset database fields.")
    parser.add_argument("--db", dest="db_path", help="Path to asset_descriptions.db")
    args = parser.parse_args()

    db_path = resolve_db_path(args.db_path)
    if not db_path.exists():
        print(f"Database not found: {db_path}")
        return 1

    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row
        columns = get_table_columns(connection)
        if is_already_normalized(columns):
            print("Database is already normalized. Nothing to do.")
            return 0

    backup_path = create_backup(db_path)
    print(f"Backup created: {backup_path}")

    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = OFF;")

        with connection:
            rebuild_asset_metadata(connection)
            rebuild_asset_description_vectors(connection)
            drop_asset_locations(connection)

    print("Normalization completed.")
    return 0


def resolve_db_path(explicit_path: str | None) -> Path:
    if explicit_path:
        return Path(explicit_path).expanduser().resolve()

    data_root = os.environ.get("DATA_ROOT")
    if data_root:
        return Path(data_root).expanduser().resolve() / "asset_descriptions.db"

    return Path(__file__).resolve().parent.parent / "data" / "asset_descriptions.db"


def create_backup(db_path: Path) -> Path:
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    backup_path = db_path.with_name(f"{db_path.name}.normalize.{timestamp}.bak")
    shutil.copy2(db_path, backup_path)
    return backup_path


def get_table_columns(connection: sqlite3.Connection) -> dict[str, set[str]]:
    table_names = [
        "asset_metadata",
        "asset_description_vectors",
        "asset_descriptions",
        "assets",
        "asset_locations",
    ]
    result: dict[str, set[str]] = {}
    for table_name in table_names:
        rows = connection.execute(f"PRAGMA table_info({table_name});").fetchall()
        result[table_name] = {row["name"] for row in rows}
    return result


def is_already_normalized(columns: dict[str, set[str]]) -> bool:
    metadata_columns = columns.get("asset_metadata", set())
    vector_columns = columns.get("asset_description_vectors", set())
    return (
        "description_text" not in metadata_columns
        and "description" not in vector_columns
        and "asset_name" not in vector_columns
        and "asset_type" not in vector_columns
        and "asset_path" not in vector_columns
        and "asset_locations" not in columns
    )


def rebuild_asset_metadata(connection: sqlite3.Connection) -> None:
    columns = existing_columns(connection, "asset_metadata")
    if not columns:
        raise RuntimeError("Missing asset_metadata table.")

    if "description_text" not in columns:
        return

    connection.execute("DROP TABLE IF EXISTS asset_metadata_new;")
    connection.execute(
        """
        CREATE TABLE asset_metadata_new (
            asset_uid TEXT PRIMARY KEY,
            tags_json TEXT NOT NULL DEFAULT '[]',
            metadata_status TEXT NOT NULL,
            vector_state TEXT NOT NULL DEFAULT 'pending',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );
        """
    )
    connection.execute(
        """
        INSERT INTO asset_metadata_new (
            asset_uid,
            tags_json,
            metadata_status,
            vector_state,
            created_at,
            updated_at
        )
        SELECT
            asset_uid,
            COALESCE(tags_json, '[]'),
            COALESCE(metadata_status, 'described'),
            COALESCE(vector_state, 'pending'),
            created_at,
            updated_at
        FROM asset_metadata;
        """
    )
    connection.execute("DROP TABLE asset_metadata;")
    connection.execute("ALTER TABLE asset_metadata_new RENAME TO asset_metadata;")


def rebuild_asset_description_vectors(connection: sqlite3.Connection) -> None:
    columns = existing_columns(connection, "asset_description_vectors")
    if not columns:
        raise RuntimeError("Missing asset_description_vectors table.")

    if "description" not in columns and "asset_name" not in columns and "asset_type" not in columns and "asset_path" not in columns:
        return

    content_hash_expr = "content_hash" if "content_hash" in columns else "NULL"

    connection.execute("DROP TABLE IF EXISTS asset_description_vectors_new;")
    connection.execute(
        """
        CREATE TABLE asset_description_vectors_new (
            asset_id TEXT PRIMARY KEY,
            description_store_path TEXT NOT NULL,
            embedding_model TEXT NOT NULL,
            vector_dim INTEGER NOT NULL,
            vector_blob BLOB NOT NULL,
            vectorized_at TEXT NOT NULL,
            content_hash TEXT NULL
        );
        """
    )
    connection.execute(
        f"""
        INSERT INTO asset_description_vectors_new (
            asset_id,
            description_store_path,
            embedding_model,
            vector_dim,
            vector_blob,
            vectorized_at,
            content_hash
        )
        SELECT
            asset_id,
            description_store_path,
            embedding_model,
            vector_dim,
            vector_blob,
            vectorized_at,
            {content_hash_expr}
        FROM asset_description_vectors;
        """
    )
    connection.execute("DROP TABLE asset_description_vectors;")
    connection.execute("ALTER TABLE asset_description_vectors_new RENAME TO asset_description_vectors;")


def drop_asset_locations(connection: sqlite3.Connection) -> None:
    connection.execute("DROP TABLE IF EXISTS asset_locations;")


def existing_columns(connection: sqlite3.Connection, table_name: str) -> set[str]:
    rows = connection.execute(f"PRAGMA table_info({table_name});").fetchall()
    return {row["name"] for row in rows}


if __name__ == "__main__":
    raise SystemExit(main())
