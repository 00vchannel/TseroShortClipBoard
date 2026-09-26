"""Compare a legacy JSON snapshot with a migrated v2 SQLite database.

Prints counts and equality only; never prints clipboard content.
"""

import argparse
import json
import sqlite3
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("legacy_json", type=Path)
    parser.add_argument("database", type=Path)
    args = parser.parse_args()

    source = json.loads(args.legacy_json.read_text(encoding="utf-8"))
    source_snippets = [s for s in source["snippets"] if s["category"] != "Copied"]
    source_history = [s["content"] for s in source["snippets"] if s["category"] == "Copied"]
    source_categories = [c for c in source["categories"] if c not in ("All", "Copied")]

    connection = sqlite3.connect(args.database.as_uri() + "?mode=ro", uri=True)
    try:
        integrity = connection.execute("PRAGMA integrity_check").fetchone()[0]
        categories = [row[0] for row in connection.execute(
            "SELECT name FROM categories ORDER BY sort_order"
        )]
        snippets = list(connection.execute(
            "SELECT s.emoji,s.title,c.name,s.content FROM snippets s "
            "LEFT JOIN categories c ON c.id=s.category_id "
            "WHERE s.deleted_at IS NULL ORDER BY s.sort_order,s.id"
        ))
        history = [row[0] for row in connection.execute(
            "SELECT content FROM history ORDER BY sort_order"
        )]
    finally:
        connection.close()

    expected_snippets = [
        (s["emoji"], s["title"], None if s["category"] == "All" else s["category"], s["content"])
        for s in source_snippets
    ]
    print(f"integrity={integrity}")
    print(f"categories={len(categories)} equal={categories == source_categories}")
    print(f"snippets={len(snippets)} equal={snippets == expected_snippets}")
    print(f"history={len(history)} equal={history == source_history}")
    if not (integrity == "ok" and categories == source_categories
            and snippets == expected_snippets and history == source_history):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
