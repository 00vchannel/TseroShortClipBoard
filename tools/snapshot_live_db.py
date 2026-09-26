"""Create a consistent SQLite backup and content-free verification manifest."""

import argparse
import hashlib
import json
import sqlite3
from datetime import datetime, timezone
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    if not args.source.is_file() or args.destination.exists():
        raise SystemExit("Source missing or destination already exists")

    args.destination.mkdir(parents=True)
    target_path = args.destination / "clipboard.db"
    source = sqlite3.connect(args.source.as_uri() + "?mode=ro", uri=True)
    target = sqlite3.connect(target_path)
    try:
        source.backup(target)
        integrity = target.execute("PRAGMA integrity_check").fetchone()[0]
        if integrity != "ok":
            raise RuntimeError("Backup integrity check failed")
        tables = [row[0] for row in target.execute(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"
        )]
        counts = {}
        table_sha256 = {}
        for table in tables:
            quoted = '"' + table.replace('"', '""') + '"'
            rows = target.execute(f"SELECT * FROM {quoted} ORDER BY rowid").fetchall()
            counts[table] = len(rows)
            table_sha256[table] = hashlib.sha256(
                json.dumps(rows, ensure_ascii=False, default=str, separators=(",", ":")).encode("utf-8")
            ).hexdigest()
        manifest = {
            "created_at": datetime.now(timezone.utc).isoformat(),
            "integrity": integrity,
            "counts": counts,
            "table_sha256": table_sha256,
            "sha256": hashlib.sha256(target_path.read_bytes()).hexdigest(),
        }
        (args.destination / "manifest.json").write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8"
        )
        print(json.dumps({"destination": str(args.destination), "integrity": integrity, "counts": counts}, ensure_ascii=False))
    finally:
        target.close()
        source.close()


if __name__ == "__main__":
    main()
