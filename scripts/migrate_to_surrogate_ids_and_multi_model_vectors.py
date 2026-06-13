#!/usr/bin/env python3
"""One-time migration to surrogate primary keys and multi-model vectors."""

from __future__ import annotations

import argparse
import os
import shutil
import sqlite3
from datetime import datetime, timezone
from pathlib import Path


TABLES = {
    "assets": """
        CREATE TABLE assets_new (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            asset_uid TEXT NOT NULL,
            library_id TEXT NOT NULL,
            asset_name TEXT NOT NULL,
            asset_type TEXT NOT NULL,
            current_path TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            observed_hash TEXT NOT NULL,
            file_size INTEGER NOT NULL,
            modified_time_utc TEXT NOT NULL,
            status TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            created_by TEXT NOT NULL,
            uid_version INTEGER NOT NULL DEFAULT 1
        );
    """,
    "asset_metadata": """
        CREATE TABLE asset_metadata_new (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            asset_uid TEXT NOT NULL,
            tags_json TEXT NOT NULL DEFAULT '[]',
            metadata_status TEXT NOT NULL,
            vector_state TEXT NOT NULL DEFAULT 'pending',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );
    """,
    "asset_descriptions": """
        CREATE TABLE asset_descriptions_new (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            asset_id TEXT NOT NULL,
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
    """,
    "asset_description_vectors": """
        CREATE TABLE asset_description_vectors_new (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            asset_id TEXT NOT NULL,
            angle_type TEXT NOT NULL DEFAULT '全面',
            embedding_model TEXT NOT NULL,
            vector_dim INTEGER NOT NULL,
            vector_blob BLOB NOT NULL,
            vectorized_at TEXT NOT NULL,
            content_hash TEXT NULL
        );
    """,
}

INDEXES = """
CREATE UNIQUE INDEX ux_assets_asset_uid ON assets(asset_uid);
CREATE INDEX ix_assets_content_hash ON assets(content_hash);
CREATE INDEX ix_assets_current_path ON assets(current_path);
CREATE UNIQUE INDEX ux_asset_metadata_asset_uid ON asset_metadata(asset_uid);
CREATE UNIQUE INDEX ux_asset_descriptions_asset_id ON asset_descriptions(asset_id);
CREATE UNIQUE INDEX ux_asset_description_vectors_identity
    ON asset_description_vectors(asset_id, angle_type, embedding_model);
CREATE INDEX ix_asset_description_vectors_embedding_model
    ON asset_description_vectors(embedding_model);
"""


def main() -> int:
    parser = argparse.ArgumentParser(description="Migrate SQLite tables to surrogate IDs and multi-model vectors.")
    parser.add_argument("--db", dest="db_path", help="Path to asset_descriptions.db")
    parser.add_argument("--dry-run", action="store_true", help="Report migration status without changing the database.")
    args = parser.parse_args()

    db_path = resolve_db_path(args.db_path)
    if not db_path.exists():
        print(f"Database not found: {db_path}")
        return 1

    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row
        missing = [table for table in TABLES if not table_exists(connection, table)]
        if missing:
            print(f"Missing required tables: {', '.join(missing)}")
            return 1
        pending = [table for table in TABLES if primary_key_columns(connection, table) != ["id"]]

    print(f"Surrogate ID migration scan: pending_tables={pending}")
    if not pending or args.dry_run:
        return 0

    backup_path = create_backup(db_path)
    print(f"Backup created: {backup_path}")
    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = OFF;")
        with connection:
            for table in TABLES:
                rebuild_table(connection, table)
            connection.executescript(INDEXES)
        connection.execute("PRAGMA foreign_keys = ON;")

    print("Migration completed.")
    return 0


def rebuild_table(connection: sqlite3.Connection, table: str) -> None:
    old_columns = [row["name"] for row in connection.execute(f"PRAGMA table_info({table});")]
    copy_columns = [column for column in old_columns if column != "id"]
    connection.execute(f"DROP TABLE IF EXISTS {table}_new;")
    connection.execute(TABLES[table])
    columns = ", ".join(copy_columns)
    connection.execute(f"INSERT INTO {table}_new ({columns}) SELECT {columns} FROM {table};")
    connection.execute(f"DROP TABLE {table};")
    connection.execute(f"ALTER TABLE {table}_new RENAME TO {table};")


def resolve_db_path(explicit_path: str | None) -> Path:
    if explicit_path:
        return Path(explicit_path).expanduser().resolve()
    data_root = os.environ.get("DATA_ROOT")
    if data_root:
        return Path(data_root).expanduser().resolve() / "asset_descriptions.db"
    return Path(__file__).resolve().parent.parent / "data" / "asset_descriptions.db"


def create_backup(db_path: Path) -> Path:
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    backup_path = db_path.with_name(f"{db_path.name}.surrogate-ids-multi-model.{timestamp}.bak")
    shutil.copy2(db_path, backup_path)
    return backup_path


def table_exists(connection: sqlite3.Connection, table: str) -> bool:
    return connection.execute(
        "SELECT 1 FROM sqlite_master WHERE type='table' AND name=?;", (table,)
    ).fetchone() is not None


def primary_key_columns(connection: sqlite3.Connection, table: str) -> list[str]:
    rows = connection.execute(f"PRAGMA table_info({table});").fetchall()
    return [row["name"] for row in sorted(rows, key=lambda row: int(row["pk"])) if int(row["pk"]) > 0]


if __name__ == "__main__":
    raise SystemExit(main())
