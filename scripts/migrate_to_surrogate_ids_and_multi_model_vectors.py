#!/usr/bin/env python3
"""One-time migration to database libraries and numeric foreign keys."""

from __future__ import annotations

import argparse
import json
import os
import shutil
import sqlite3
from datetime import datetime, timezone
from pathlib import Path


CORE_TABLES = ("assets", "asset_metadata", "asset_descriptions", "asset_description_vectors")

SCHEMA = """
CREATE TABLE libraries_new (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    root_path TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE UNIQUE INDEX ux_libraries_new_root_path ON libraries_new(root_path);

CREATE TABLE assets_new (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    asset_uid TEXT NOT NULL,
    library_id INTEGER NOT NULL REFERENCES libraries_new(id) ON DELETE CASCADE,
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

CREATE TABLE asset_metadata_new (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    asset_id INTEGER NOT NULL REFERENCES assets_new(id) ON DELETE CASCADE,
    tags_json TEXT NOT NULL DEFAULT '[]',
    metadata_status TEXT NOT NULL,
    vector_state TEXT NOT NULL DEFAULT 'pending',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE asset_descriptions_new (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    asset_id INTEGER NOT NULL REFERENCES assets_new(id) ON DELETE CASCADE,
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

CREATE TABLE asset_description_vectors_new (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    asset_id INTEGER NOT NULL REFERENCES assets_new(id) ON DELETE CASCADE,
    angle_type TEXT NOT NULL DEFAULT '全面',
    embedding_model TEXT NOT NULL,
    vector_dim INTEGER NOT NULL,
    vector_blob BLOB NOT NULL,
    vectorized_at TEXT NOT NULL,
    content_hash TEXT NULL
);
"""

INDEXES = """
CREATE UNIQUE INDEX ux_libraries_root_path ON libraries(root_path);
CREATE UNIQUE INDEX ux_assets_asset_uid ON assets(asset_uid);
CREATE INDEX ix_assets_library_id ON assets(library_id);
CREATE INDEX ix_assets_content_hash ON assets(content_hash);
CREATE INDEX ix_assets_current_path ON assets(current_path);
CREATE UNIQUE INDEX ux_asset_metadata_asset_id ON asset_metadata(asset_id);
CREATE UNIQUE INDEX ux_asset_descriptions_asset_id ON asset_descriptions(asset_id);
CREATE UNIQUE INDEX ux_asset_description_vectors_identity
    ON asset_description_vectors(asset_id, angle_type, embedding_model);
CREATE INDEX ix_asset_description_vectors_embedding_model
    ON asset_description_vectors(embedding_model);
"""


def main() -> int:
    parser = argparse.ArgumentParser(description="Migrate libraries and asset relations to numeric IDs.")
    parser.add_argument("--db", dest="db_path", help="Path to asset_descriptions.db")
    parser.add_argument("--libraries-json", help="Path to legacy libraries.json")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    db_path = resolve_db_path(args.db_path)
    if not db_path.exists():
        print(f"Database not found: {db_path}")
        return 1

    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row
        missing = [table for table in CORE_TABLES if not table_exists(connection, table)]
        if missing:
            print(f"Missing required tables: {', '.join(missing)}")
            return 1
        pending = needs_migration(connection)

    print(f"Numeric foreign-key migration scan: pending={pending}")
    if not pending or args.dry_run:
        return 0

    backup_path = create_backup(db_path)
    print(f"Backup created: {backup_path}")
    library_items = read_legacy_libraries(resolve_libraries_path(args.libraries_json, db_path))

    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = OFF;")
        with connection:
            migrate(connection, library_items)
        connection.execute("PRAGMA foreign_keys = ON;")
        violations = connection.execute("PRAGMA foreign_key_check;").fetchall()
        if violations:
            raise RuntimeError(f"Foreign-key check failed: {violations}")

    print("Migration completed.")
    return 0


def needs_migration(connection: sqlite3.Connection) -> bool:
    if not table_exists(connection, "libraries"):
        return True
    if column_type(connection, "assets", "library_id") != "INTEGER":
        return True
    if "asset_uid" in columns(connection, "asset_metadata"):
        return True
    return any(column_type(connection, table, "asset_id") != "INTEGER" for table in CORE_TABLES[1:])


def migrate(connection: sqlite3.Connection, library_items: list[dict[str, str]]) -> None:
    connection.executescript(
        "DROP TABLE IF EXISTS libraries_new; DROP TABLE IF EXISTS assets_new; "
        "DROP TABLE IF EXISTS asset_metadata_new; DROP TABLE IF EXISTS asset_descriptions_new; "
        "DROP TABLE IF EXISTS asset_description_vectors_new;"
    )
    connection.executescript(SCHEMA)
    now = datetime.now(timezone.utc).isoformat()

    legacy_to_numeric: dict[str, int] = {}
    if table_exists(connection, "libraries"):
        for row in connection.execute("SELECT id, name, root_path, created_at, updated_at FROM libraries;"):
            cursor = connection.execute(
                "INSERT INTO libraries_new (name, root_path, created_at, updated_at) VALUES (?, ?, ?, ?);",
                (row["name"], row["root_path"], row["created_at"], row["updated_at"]),
            )
            legacy_to_numeric[str(row["id"])] = int(cursor.lastrowid)

    for item in library_items:
        cursor = connection.execute(
            "INSERT OR IGNORE INTO libraries_new (name, root_path, created_at, updated_at) VALUES (?, ?, ?, ?);",
            (item["name"], item["rootPath"], now, now),
        )
        numeric_id = int(cursor.lastrowid) if cursor.lastrowid else int(
            connection.execute("SELECT id FROM libraries_new WHERE root_path = ?;", (item["rootPath"],)).fetchone()[0]
        )
        legacy_to_numeric[item["id"]] = numeric_id

    for legacy_id in [str(row[0]) for row in connection.execute("SELECT DISTINCT library_id FROM assets;")]:
        if legacy_id not in legacy_to_numeric:
            cursor = connection.execute(
                "INSERT INTO libraries_new (name, root_path, created_at, updated_at) VALUES (?, ?, ?, ?);",
                (f"迁移素材库 {legacy_id}", f"legacy://{legacy_id}", now, now),
            )
            legacy_to_numeric[legacy_id] = int(cursor.lastrowid)

    asset_has_id = "id" in columns(connection, "assets")
    asset_rows = connection.execute("SELECT * FROM assets;").fetchall()
    for row in asset_rows:
        values = (
            row["id"] if asset_has_id else None,
            row["asset_uid"], legacy_to_numeric[str(row["library_id"])], row["asset_name"], row["asset_type"],
            row["current_path"], row["content_hash"], row["observed_hash"], row["file_size"],
            row["modified_time_utc"], row["status"], row["created_at"], row["updated_at"], row["created_by"],
            row["uid_version"],
        )
        connection.execute(
            """INSERT INTO assets_new
            (id, asset_uid, library_id, asset_name, asset_type, current_path, content_hash, observed_hash,
             file_size, modified_time_utc, status, created_at, updated_at, created_by, uid_version)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);""",
            values,
        )

    copy_related(connection, "asset_metadata", "asset_uid" if "asset_uid" in columns(connection, "asset_metadata") else "asset_id",
                 ("tags_json", "metadata_status", "vector_state", "created_at", "updated_at"))
    copy_related(connection, "asset_descriptions", "asset_id",
                 ("asset_name", "asset_type", "asset_path", "description", "backend_endpoint", "mode", "generated_at",
                  "token_usage_json", "prompt", "system_prompt", "content_hash", "metadata_status"))
    copy_related(connection, "asset_description_vectors", "asset_id",
                 ("angle_type", "embedding_model", "vector_dim", "vector_blob", "vectorized_at", "content_hash"))

    for table in (*CORE_TABLES[::-1], "libraries"):
        if table_exists(connection, table):
            connection.execute(f"DROP TABLE {table};")
    for table in ("libraries", *CORE_TABLES):
        connection.execute(f"ALTER TABLE {table}_new RENAME TO {table};")
    connection.executescript(INDEXES)


def copy_related(connection: sqlite3.Connection, table: str, reference_column: str, data_columns: tuple[str, ...]) -> None:
    available = columns(connection, table)
    selected = [column for column in data_columns if column in available]
    defaults = {
        "angle_type": "'全面'", "metadata_status": "'ready'", "vector_state": "'pending'",
        "content_hash": "NULL", "token_usage_json": "NULL", "prompt": "NULL", "system_prompt": "NULL",
    }
    expressions = [f"src.{column}" if column in selected else defaults[column] for column in data_columns]
    connection.execute(
        f"""INSERT OR IGNORE INTO {table}_new (asset_id, {', '.join(data_columns)})
        SELECT a.id, {', '.join(expressions)}
        FROM {table} AS src
        INNER JOIN assets AS old_a
          ON CAST(src.{reference_column} AS TEXT) = old_a.asset_uid
          OR CAST(src.{reference_column} AS TEXT) = CAST(old_a.id AS TEXT)
        INNER JOIN assets_new AS a ON a.asset_uid = old_a.asset_uid;"""
    )


def read_legacy_libraries(path: Path) -> list[dict[str, str]]:
    if not path.exists():
        return []
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    return [item for item in data if item.get("id") and item.get("name") and item.get("rootPath")]


def resolve_db_path(explicit_path: str | None) -> Path:
    if explicit_path:
        return Path(explicit_path).expanduser().resolve()
    data_root = os.environ.get("DATA_ROOT")
    if data_root:
        return Path(data_root).expanduser().resolve() / "asset_descriptions.db"
    return Path(__file__).resolve().parent.parent / "data" / "asset_descriptions.db"


def resolve_libraries_path(explicit_path: str | None, db_path: Path) -> Path:
    return Path(explicit_path).expanduser().resolve() if explicit_path else db_path.with_name("libraries.json")


def create_backup(db_path: Path) -> Path:
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    backup_path = db_path.with_name(f"{db_path.name}.numeric-foreign-keys.{timestamp}.bak")
    shutil.copy2(db_path, backup_path)
    return backup_path


def table_exists(connection: sqlite3.Connection, table: str) -> bool:
    return connection.execute(
        "SELECT 1 FROM sqlite_master WHERE type='table' AND name=?;", (table,)
    ).fetchone() is not None


def columns(connection: sqlite3.Connection, table: str) -> set[str]:
    return {row["name"] for row in connection.execute(f"PRAGMA table_info({table});")}


def column_type(connection: sqlite3.Connection, table: str, column: str) -> str | None:
    for row in connection.execute(f"PRAGMA table_info({table});"):
        if row["name"] == column:
            return str(row["type"]).upper()
    return None


if __name__ == "__main__":
    raise SystemExit(main())
