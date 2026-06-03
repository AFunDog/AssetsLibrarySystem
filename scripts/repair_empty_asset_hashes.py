#!/usr/bin/env python3
"""One-time repair for legacy asset rows with empty hash or file size values.

This script backfills `assets.content_hash`, `assets.observed_hash`, and
`assets.file_size` for rows that still have empty hash fields or a zero file
size. It computes the SHA-256 hash and actual file size from the file
referenced by `assets.current_path`.

The script is intentionally simple:
- create a backup first
- only touch rows with empty `content_hash`, empty `observed_hash`, or `file_size = 0`
- skip rows whose files no longer exist
- do not rewrite any other metadata fields
"""

from __future__ import annotations

import argparse
import hashlib
import os
import shutil
import sqlite3
from datetime import datetime, timezone
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description="Repair empty asset hash and file size values in the database.")
    parser.add_argument("--db", dest="db_path", help="Path to asset_descriptions.db")
    parser.add_argument("--dry-run", action="store_true", help="Report changes without writing them.")
    args = parser.parse_args()

    db_path = resolve_db_path(args.db_path)
    if not db_path.exists():
        print(f"Database not found: {db_path}")
        return 1

    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row
        total_rows = connection.execute(
            """
            SELECT COUNT(*)
            FROM assets
            WHERE COALESCE(content_hash, '') = ''
               OR COALESCE(observed_hash, '') = ''
               OR COALESCE(file_size, 0) = 0;
            """
        ).fetchone()[0]

    if total_rows == 0:
        print("No empty hash rows found. Nothing to do.")
        return 0

    backup_path = create_backup(db_path)
    print(f"Backup created: {backup_path}")

    updated_count = 0
    skipped_missing_count = 0
    skipped_error_count = 0

    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row
        rows = connection.execute(
            """
            SELECT asset_uid, asset_name, current_path, content_hash, observed_hash, file_size
            FROM assets
            WHERE COALESCE(content_hash, '') = ''
               OR COALESCE(observed_hash, '') = ''
               OR COALESCE(file_size, 0) = 0
            ORDER BY updated_at ASC, created_at ASC;
            """
        ).fetchall()

        if not args.dry_run:
            connection.execute("PRAGMA foreign_keys = OFF;")

        for row in rows:
            current_path = row["current_path"] or ""
            file_path = Path(current_path)

            if not file_path.exists():
                skipped_missing_count += 1
                print(f"Skip missing file: asset_uid={row['asset_uid']}, path={current_path}")
                continue

            try:
                file_size = file_path.stat().st_size
                hash_value = compute_sha256(file_path)
            except Exception as exc:  # pragma: no cover - defensive logging path
                skipped_error_count += 1
                print(f"Skip error: asset_uid={row['asset_uid']}, path={current_path}, error={exc}")
                continue

            print(
                f"Repair hash: asset_uid={row['asset_uid']}, path={current_path}, "
                f"file_size={file_size}, hash={hash_value}"
            )
            updated_count += 1

            if args.dry_run:
                continue

            connection.execute(
                """
                UPDATE assets
                SET content_hash = $hash_value,
                    observed_hash = $hash_value,
                    file_size = $file_size
                WHERE asset_uid = $asset_uid;
                """,
                {
                    "asset_uid": row["asset_uid"],
                    "hash_value": hash_value,
                    "file_size": file_size,
                },
            )

        if not args.dry_run:
            connection.commit()

    print(
        "Repair completed: "
        f"updated={updated_count}, skipped_missing={skipped_missing_count}, skipped_error={skipped_error_count}"
    )
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
    backup_path = db_path.with_name(f"{db_path.name}.repair-empty-hash.{timestamp}.bak")
    shutil.copy2(db_path, backup_path)
    return backup_path


def compute_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


if __name__ == "__main__":
    raise SystemExit(main())
