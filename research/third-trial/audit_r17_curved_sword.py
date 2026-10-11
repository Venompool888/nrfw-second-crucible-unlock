"""Read-only source audit for the room-seven curved-sword correction.

Uses the existing authored Unity bundle reader; writes only this task's reports.
No game code is loaded or executed.
"""
import importlib.util
import json
import sqlite3
from pathlib import Path

TASK = Path(__file__).resolve().parents[2]
PROJECT = TASK.parents[1]
READER = Path(__file__).with_name("audit_weapon_prefabs.py")


def main():
    spec = importlib.util.spec_from_file_location("read_only_weapon_audit", READER)
    audit = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(audit)
    audit.ROOT = TASK
    audit.NAMES = ("deadTideCurvedSwordViewVariant",)
    audit.WEAPON_NAMES = ("deadTideCurvedSwordData",)
    audit.main()
    qdb = PROJECT / "research/quantum-runtime/quantum-full.sqlite"
    conn = sqlite3.connect(f"file:{qdb.as_posix()}?mode=ro", uri=True)
    data = conn.execute("select asset_guid, raw_json from records where internal_name=?",
                        ("deadTideCurvedSwordData",)).fetchall()
    if len(data) != 1 or int(data[0][0]) != 4091733801493987324:
        raise ValueError("Expected shipped curved-sword source is absent or ambiguous")
    pending = [4091733801493987324]
    found, missing = {}, set()
    while pending:
        guid = pending.pop()
        if guid in found or guid in missing:
            continue
        row = conn.execute("select internal_name, wrapper_type, raw_json from records where asset_guid=?",
                           (str(guid),)).fetchone()
        if row is None:
            missing.add(guid)
            continue
        name, kind, raw = row
        tree = json.loads(raw)
        found[guid] = {"name": name, "type": kind}
        def refs(node):
            if isinstance(node, dict):
                inner = node.get("Id")
                if isinstance(inner, dict) and isinstance(inner.get("Value"), int):
                    yield inner["Value"]
                for value in node.values():
                    yield from refs(value)
            elif isinstance(node, list):
                for value in node:
                    yield from refs(value)
        pending.extend(value for value in refs(tree) if value != 0)
    conn.close()
    result = {"weapon_guid": 4091733801493987324,
              "source_database_sha256": audit.digest(qdb),
              "closure": found, "missing": sorted(missing),
              "source_reader_sha256": audit.digest(READER),
              "game_dlls_executed": False, "game_started": False}
    out = TASK / "research/third-trial/r17-curved-sword-closure.json"
    out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf8")
    if missing:
        raise ValueError(f"Curved-sword source closure is incomplete: {missing}")
    print(f"PASS: room-seven curved-sword root, action views and {len(found)} explicit source assets")


if __name__ == "__main__":
    main()
