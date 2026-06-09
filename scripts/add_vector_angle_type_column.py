#!/usr/bin/env python3
"""One-time migration to add angle_type to asset_description_vectors.

This script adds the `angle_type` column and backfills existing rows with the
default value `全面`.
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
    parser = argparse.ArgumentParser(description="Add angle_type to asset_description_vectors and backfill existing rows.")
    parser.add_argument("--db", dest="db_path", help="Path to asset_descriptions.db")
    parser.add_argument("--dry-run", action="store_true", help="Only report whether migration is needed.")
    args = parser.parse_args()

    db_path = resolve_db_path(args.db_path)
    if not db_path.exists():
        print(f"Database not found: {db_path}")
        return 1

    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row
        columns = existing_columns(connection, "asset_description_vectors")
        if not columns:
            print("Missing asset_description_vectors table.")
            return 1

        has_angle_type = "angle_type" in columns
        existing_count = connection.execute("SELECT COUNT(*) FROM asset_description_vectors;").fetchone()[0]
        missing_value_count = 0
        if has_angle_type:
            missing_value_count = connection.execute(
                """
                SELECT COUNT(*)
                FROM asset_description_vectors
                WHERE COALESCE(TRIM(angle_type), '') = '';
                """
            ).fetchone()[0]

    if has_angle_type and missing_value_count == 0:
        print("Database already has angle_type and no empty values. Nothing to do.")
        return 0

    print(
        "Vector angle_type migration scan: "
        f"has_angle_type={has_angle_type}, rows={existing_count}, empty_values={missing_value_count}"
    )
    if args.dry_run:
        return 0

    backup_path = create_backup(db_path)
    print(f"Backup created: {backup_path}")

    with sqlite3.connect(db_path) as connection:
        with connection:
            if not has_angle_type:
                connection.execute(
                    f"ALTER TABLE asset_description_vectors ADD COLUMN angle_type TEXT NOT NULL DEFAULT '{DEFAULT_ANGLE_TYPE}';"
                )

            connection.execute(
                """
                UPDATE asset_description_vectors
                SET angle_type = $angle_type
                WHERE COALESCE(TRIM(angle_type), '') = '';
                """,
                {"angle_type": DEFAULT_ANGLE_TYPE},
            )

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
    backup_path = db_path.with_name(f"{db_path.name}.vector-angle-type.{timestamp}.bak")
    shutil.copy2(db_path, backup_path)
    return backup_path


def existing_columns(connection: sqlite3.Connection, table_name: str) -> set[str]:
    rows = connection.execute(f"PRAGMA table_info({table_name});").fetchall()
    return {row["name"] for row in rows}


if __name__ == "__main__":
    raise SystemExit(main())
