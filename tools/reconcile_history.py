"""Recover history lost to the pre-fix sort-order gap without replacing live rows.

Run against a stopped app. The pre-repair snapshot is an immutable guard: all
tables must still match it before --apply may change the target database.
Clipboard contents are never printed.
"""

import argparse
import datetime as dt
import sqlite3
import uuid
from pathlib import Path


TABLES = ("categories", "drafts", "history", "meta", "settings", "snippets")


def rows(connection, table):
    return connection.execute(f"SELECT * FROM {table} ORDER BY rowid").fetchall()


def history(connection):
    return connection.execute(
        "SELECT id,content,copied_at,sort_order FROM history ORDER BY sort_order,id"
    ).fetchall()


def plan(current, previous):
    current_contents = {row[1] for row in current}
    missing = []
    for row in previous:
        if row[1] not in current_contents:
            missing.append(row)
            current_contents.add(row[1])
    slots = max(0, 50 - len(current))
    selected = missing[:slots]
    combined = list(current) + [
        (str(uuid.uuid4()), item[1], item[2], 1000 + i)
        for i, item in enumerate(selected)
    ]
    combined.sort(key=lambda row: dt.datetime.fromisoformat(row[2]), reverse=True)
    return missing, selected, combined


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--snapshot", type=Path, required=True)
    parser.add_argument("--previous", type=Path, required=True)
    parser.add_argument("--target", type=Path, required=True)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with sqlite3.connect(f"file:{args.snapshot}?mode=ro", uri=True) as snapshot, \
         sqlite3.connect(f"file:{args.previous}?mode=ro", uri=True) as previous:
        with sqlite3.connect(args.target) as target:
            if args.apply:
                target.execute("BEGIN IMMEDIATE")
            for table in TABLES:
                if rows(target, table) != rows(snapshot, table):
                    raise RuntimeError(f"target changed since backup: {table}")
            current = history(target)
            older = history(previous)
            missing, selected, combined = plan(current, older)
            print(
                f"before={len(current)} previous={len(older)} "
                f"missing_distinct={len(missing)} recover={len(selected)} "
                f"after={len(combined)}"
            )
            if not args.apply:
                return
            for identifier, content, copied_at, _ in combined:
                if identifier not in {row[0] for row in current}:
                    target.execute(
                        "INSERT INTO history(id,content,copied_at,sort_order) VALUES(?,?,?,?)",
                        (identifier, content, copied_at, 1000),
                    )
            for index, row in enumerate(combined):
                target.execute(
                    "UPDATE history SET sort_order=? WHERE id=?", (index, row[0])
                )
            if target.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
                raise RuntimeError("database integrity check failed")
            if len(history(target)) != len(combined):
                raise RuntimeError("recovered history count mismatch")
            for table in TABLES:
                if table != "history" and rows(target, table) != rows(snapshot, table):
                    raise RuntimeError(f"unexpected change: {table}")
            target.commit()
            print("applied=ok")


if __name__ == "__main__":
    main()
