#!/usr/bin/env python3
"""One-time migration to remove legacy description store path columns.

This script physically removes these redundant columns from existing SQLite
databases:
- asset_descriptions.store_path
- asset_description_vectors.description_store_path

It keeps all live data by rebuilding the two tables and copying only the
authoritative columns that are still used by the application.
"""

from __future__ import annotations

import argparse
import os
import shutil
import sqlite3
from datetime import datetime, timezone
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description="Remove legacy description store path columns from asset_descriptions.db.")
    parser.add_argument("--db", dest="db_path", help="Path to asset_descriptions.db")
    parser.add_argument("--dry-run", action="store_true", help="Only report whether migration is needed.")
    args = parser.parse_args()

    db_path = resolve_db_path(args.db_path)
    if not db_path.exists():
        print(f"Database not found: {db_path}")
        return 1

    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row
        description_columns = existing_columns(connection, "asset_descriptions")
        vector_columns = existing_columns(connection, "asset_description_vectors")

    needs_description_rebuild = "store_path" in description_columns
    needs_vector_rebuild = "description_store_path" in vector_columns
    if not needs_description_rebuild and not needs_vector_rebuild:
        print("Database already matches current schema. Nothing to do.")
        return 0

    print(
        "Migration required: "
        f"asset_descriptions.store_path={needs_description_rebuild}, "
        f"asset_description_vectors.description_store_path={needs_vector_rebuild}"
    )
    if args.dry_run:
        return 0

    backup_path = create_backup(db_path)
    print(f"Backup created: {backup_path}")

    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = OFF;")

        with connection:
            if needs_description_rebuild:
                rebuild_asset_descriptions(connection)
            if needs_vector_rebuild:
                rebuild_asset_description_vectors(connection)

    print("Migration completed.")
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
    backup_path = db_path.with_name(f"{db_path.name}.remove-store-paths.{timestamp}.bak")
    shutil.copy2(db_path, backup_path)
    return backup_path


def existing_columns(connection: sqlite3.Connection, table_name: str) -> set[str]:
    rows = connection.execute(f"PRAGMA table_info({table_name});").fetchall()
    return {row["name"] for row in rows}


def rebuild_asset_descriptions(connection: sqlite3.Connection) -> None:
    columns = existing_columns(connection, "asset_descriptions")
    if not columns:
        raise RuntimeError("Missing asset_descriptions table.")

    content_hash_expr = "content_hash" if "content_hash" in columns else "NULL"
    metadata_status_expr = "metadata_status" if "metadata_status" in columns else "'ready'"

    connection.execute("DROP TABLE IF EXISTS asset_descriptions_new;")
    connection.execute(
        """
        CREATE TABLE asset_descriptions_new (
            asset_id TEXT PRIMARY KEY,
            asset_name TEXT NOT NULL,
            asset_type TEXT NOT NULL,
            asset_path TEXT NOT NULL,
            description TEXT NOT NULL,
            backend_endpoint TEXT NOT NULL,
            mode TEXT NOT NULL,
            generated_at TEXT NOT NULL,
            token_usage_json TEXT NULL,
            prompt TEXT NULL,
            system_prompt TEXT NULL,
            content_hash TEXT NULL,
            metadata_status TEXT NOT NULL DEFAULT 'ready'
        );
        """
    )
    connection.execute(
        f"""
        INSERT INTO asset_descriptions_new (
            asset_id,
            asset_name,
            asset_type,
            asset_path,
            description,
            backend_endpoint,
            mode,
            generated_at,
            token_usage_json,
            prompt,
            system_prompt,
            content_hash,
            metadata_status
        )
        SELECT
            asset_id,
            asset_name,
            asset_type,
            asset_path,
            description,
            backend_endpoint,
            mode,
            generated_at,
            token_usage_json,
            prompt,
            system_prompt,
            {content_hash_expr},
            {metadata_status_expr}
        FROM asset_descriptions;
        """
    )
    connection.execute("DROP TABLE asset_descriptions;")
    connection.execute("ALTER TABLE asset_descriptions_new RENAME TO asset_descriptions;")


def rebuild_asset_description_vectors(connection: sqlite3.Connection) -> None:
    columns = existing_columns(connection, "asset_description_vectors")
    if not columns:
        raise RuntimeError("Missing asset_description_vectors table.")

    content_hash_expr = "content_hash" if "content_hash" in columns else "NULL"

    connection.execute("DROP TABLE IF EXISTS asset_description_vectors_new;")
    connection.execute(
        """
        CREATE TABLE asset_description_vectors_new (
            asset_id TEXT PRIMARY KEY,
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
            embedding_model,
            vector_dim,
            vector_blob,
            vectorized_at,
            content_hash
        )
        SELECT
            asset_id,
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


if __name__ == "__main__":
    raise SystemExit(main())
