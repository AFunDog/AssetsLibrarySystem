#!/usr/bin/env python3
"""One-time migration for multi-angle asset_description_vectors.

This script rebuilds `asset_description_vectors` so the table uses a composite
primary key `(asset_id, angle_type)` instead of the legacy single-column
primary key `asset_id`.
"""

from __future__ import annotations

import argparse
import os
import shutil
import sqlite3
from datetime import datetime, timezone
from pathlib import Path


DEFAULT_ANGLE_TYPE = "全面"


def main() -> int:
    parser = argparse.ArgumentParser(description="Rebuild asset_description_vectors for multi-angle vectors.")
    parser.add_argument("--db", dest="db_path", help="Path to asset_descriptions.db")
    parser.add_argument("--dry-run", action="store_true", help="Only report whether migration is needed.")
    args = parser.parse_args()

    db_path = resolve_db_path(args.db_path)
    if not db_path.exists():
        print(f"Database not found: {db_path}")
        return 1

    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row
        if not table_exists(connection, "asset_description_vectors"):
            print("Missing asset_description_vectors table.")
            return 1

        columns = existing_columns(connection, "asset_description_vectors")
        pk_columns = primary_key_columns(connection, "asset_description_vectors")
        row_count = connection.execute("SELECT COUNT(*) FROM asset_description_vectors;").fetchone()[0]

    needs_migration = pk_columns != ["asset_id", "angle_type"]
    print(
        "Multi-angle vector migration scan: "
        f"rows={row_count}, pk_columns={pk_columns}, has_angle_type={'angle_type' in columns}, needs_migration={needs_migration}"
    )
    if not needs_migration:
        print("Table already uses composite primary key (asset_id, angle_type). Nothing to do.")
        return 0

    if args.dry_run:
        return 0

    backup_path = create_backup(db_path)
    print(f"Backup created: {backup_path}")

    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row
        with connection:
            recreate_table(connection)

    print("Migration completed.")
    return 0


def recreate_table(connection: sqlite3.Connection) -> None:
    has_angle_type = "angle_type" in existing_columns(connection, "asset_description_vectors")
    angle_expr = "COALESCE(NULLIF(TRIM(angle_type), ''), '全面')" if has_angle_type else f"'{DEFAULT_ANGLE_TYPE}'"

    connection.execute(
        """
        CREATE TABLE asset_description_vectors_new (
            asset_id TEXT NOT NULL,
            angle_type TEXT NOT NULL DEFAULT '全面',
            embedding_model TEXT NOT NULL,
            vector_dim INTEGER NOT NULL,
            vector_blob BLOB NOT NULL,
            vectorized_at TEXT NOT NULL,
            content_hash TEXT NULL,
            PRIMARY KEY (asset_id, angle_type)
        );
        """
    )

    connection.execute(
        f"""
        INSERT INTO asset_description_vectors_new (
            asset_id,
            angle_type,
            embedding_model,
            vector_dim,
            vector_blob,
            vectorized_at,
            content_hash
        )
        SELECT
            asset_id,
            {angle_expr} AS angle_type,
            embedding_model,
            vector_dim,
            vector_blob,
            vectorized_at,
            content_hash
        FROM asset_description_vectors;
        """
    )

    connection.execute("DROP TABLE asset_description_vectors;")
    connection.execute("ALTER TABLE asset_description_vectors_new RENAME TO asset_description_vectors;")


def resolve_db_path(explicit_path: str | None) -> Path:
    if explicit_path:
        return Path(explicit_path).expanduser().resolve()

    data_root = os.environ.get("DATA_ROOT")
    if data_root:
        return Path(data_root).expanduser().resolve() / "asset_descriptions.db"

    return Path(__file__).resolve().parent.parent / "data" / "asset_descriptions.db"


def create_backup(db_path: Path) -> Path:
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    backup_path = db_path.with_name(f"{db_path.name}.multi-angle-vectors.{timestamp}.bak")
    shutil.copy2(db_path, backup_path)
    return backup_path


def table_exists(connection: sqlite3.Connection, table_name: str) -> bool:
    row = connection.execute(
        """
        SELECT 1
        FROM sqlite_master
        WHERE type = 'table'
          AND name = ?;
        """,
        (table_name,),
    ).fetchone()
    return row is not None


def existing_columns(connection: sqlite3.Connection, table_name: str) -> set[str]:
    rows = connection.execute(f"PRAGMA table_info({table_name});").fetchall()
    return {row["name"] for row in rows}


def primary_key_columns(connection: sqlite3.Connection, table_name: str) -> list[str]:
    rows = connection.execute(f"PRAGMA table_info({table_name});").fetchall()
    pk_items = sorted(
        ((int(row["pk"]), str(row["name"])) for row in rows if int(row["pk"]) > 0),
        key=lambda item: item[0],
    )
    return [name for _, name in pk_items]


if __name__ == "__main__":
    raise SystemExit(main())
