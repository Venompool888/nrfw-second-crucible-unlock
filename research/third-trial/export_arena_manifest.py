"""Map shipped normal Arena floors to real ordinary dynamic-spawner Nuggets.

Reads only the already-extracted QDB SQLite and world blob index. No target DLL
is loaded or executed. Writes only inside this task's artifact directory.
"""
import json
import sqlite3
import sys
from collections import Counter, defaultdict
from pathlib import Path

TASK_ROOT = Path(__file__).resolve().parents[2]
ROOT = TASK_ROOT.parents[1]
HERE = TASK_ROOT / "artifacts/first-trial-spawning"
HERE.mkdir(parents=True, exist_ok=True)
DB = ROOT / "research/quantum-runtime/quantum-full.sqlite"
BLOBS = ROOT / "research/enemy-db/output/world/blob-summary.json"
SPAWNERS = ROOT / "research/enemy-db/output/world/dynamic-spawners.json"
HEADERS = ROOT / "research/crucible-deep/payload/all-qdata-headers.json"
ENUMS = ROOT / "research/il2cpp-metadata/output/enum-map-fields.json"
QDB = Path("D:/SteamLibrary/steamapps/common/NoRestForTheWicked/NoRestForTheWicked_Data/StreamingAssets/quantumDatabase.bin")
FORBIDDEN = {"NpcInstanceData", "EnemyWaveSpawnerStaticInstanceData",
             "BossGateStaticInstanceData", "DirectorZone"}
sys.path.insert(0, str(ROOT / "research/quantum-runtime"))
from decode_qdb import decompress_ext99
from moon_msgpack import Reader


def value(v):
    return str(v.get("Value", 0)) if isinstance(v, dict) else "0"


def main():
    conn = sqlite3.connect(f"file:{DB.as_posix()}?mode=ro", uri=True)
    records = {str(g): {"record_index": i, "type": t, "name": n,
                        "data": json.loads(d)}
               for i, g, t, n, d in conn.execute(
                   "select record_index,asset_guid,wrapper_type,internal_name,raw_json from records")}
    world = next(r["data"] for r in records.values() if r["name"] == "worldlut")
    graph = world["StreamingGraph"]
    graph_by_guid = {value(r["Id"]): i for i, r in enumerate(graph)}
    payloads_by_node = defaultdict(list)
    for p in world["QuantumPayloads"]:
        payloads_by_node[value(p["StreamingNodeId"])].append(p)
    blobs_by_guid = {r["guid"]: r for r in json.loads(BLOBS.read_text(encoding="utf8"))["blobs"]}
    enum_rows = json.loads(ENUMS.read_text(encoding="utf8"))
    enum_fields = {r["field"] for r in enum_rows if r["owner"].startswith("Quantum.")}
    nested_fields = {r["field"] for r in enum_rows if r.get("value_is_enum_map")}
    headers = json.loads(HEADERS.read_text(encoding="utf8"))
    qmeta = {str(v["Guid"]["Value"]): v for h in headers
             for _, v in h["header"]["Metadata"]["$map"]}
    decoded_spawners = {}

    def spawners_for_blob(blob):
        key = blob["blob_key"]
        if key in decoded_spawners:
            return decoded_spawners[key]
        row = conn.execute("select source_offset,byte_length from blobs where blob_key=?", (key,)).fetchone()
        with QDB.open("rb") as source:
            source.seek(row[0])
            raw = decompress_ext99(source.read(row[1]))
        rd = Reader(raw, enum_fields, nested_fields)
        value_ = rd.read()
        assert rd.pos == len(raw) and value(value_[1]["Guid"]) == blob["guid"]
        types = {e["Value"]: t for e, t in qmeta[blob["guid"]]["PureContents"]}
        rows = []
        for e, v in value_[1]["Items"]["$map"]:
            if types.get(e["Value"]) != "DynamicSpawnerData":
                continue
            d = v[1]
            pos = {axis: round(d["Position"][axis]["RawValue"] / 65536, 6)
                   for axis in ("X", "Y", "Z")}
            tags = d["SpawnerTags"]
            rows.append({"guid": d["Guid"], "debug_name": d["DebugName"],
                         "position": pos,
                         "all_tags": [t["Tag"] for t in tags["AllTags"]],
                         "any_tags": [t["Tag"] for t in tags["AnyTags"]],
                         "none_tags": [t["Tag"] for t in tags["NoneTags"]],
                         "difficulty": d["Difficulty"],
                         "miss_chance": round(d["MissChance"]["RawValue"] / 65536, 6),
                         "respawn_frequency": d["RespawnFrequency"],
                         "patrol_route_id": d["PurePatrolRouteId"],
                         "spawn_conditions": d["SpawnConditions"],
                         "blob_key": key, "blob_guid": blob["guid"], "scene": blob["scene"]})
        assert len(rows) == blob["entity_types"]["DynamicSpawnerData"]
        decoded_spawners[key] = rows
        return rows

    def child_of(index, parent_index):
        while index >= 0:
            if index == parent_index:
                return True
            index = graph[index]["ParentIndex"]
        return False

    arenas = []
    for floor_guid, floor in records.items():
        if floor["type"] != "InfiniteDungeonFloorDataAsset" or floor["data"]["FloorType"] != 2:
            continue
        chunks = {value(p["StreamingNodeId"])
                  for p in world["QuantumPayloads"]
                  if any(value(g) == floor_guid for g in p["Payload"])
                  and records.get(value(p["StreamingNodeId"]), {}).get("type") == "WorldChunkDataAsset"}
        if len(chunks) != 1:
            arenas.append({"floor_guid": floor_guid, "floor_name": floor["name"],
                           "error": "floor-to-chunk count " + str(len(chunks))})
            continue
        chunk_guid = next(iter(chunks))
        chunk_index = graph_by_guid[chunk_guid]
        base_blobs = [blobs_by_guid[value(g)]
                      for row in payloads_by_node[chunk_guid]
                      if value(row["ContentSetId"]) == chunk_guid
                      for g in row["Payload"] if value(g) in blobs_by_guid]
        base_counts = Counter()
        for b in base_blobs:
            base_counts.update(b["entity_types"])
        content = []
        for bucket_guid, rows in payloads_by_node.items():
            bucket_index = graph_by_guid.get(bucket_guid, -1)
            if bucket_index < 0 or not child_of(bucket_index, chunk_index):
                continue
            if records.get(bucket_guid, {}).get("type") != "BucketDataAsset":
                continue
            by_nugget = defaultdict(list)
            for row in rows:
                nugget_guid = value(row["ContentSetId"])
                if records.get(nugget_guid, {}).get("type") == "NuggetDataAsset":
                    by_nugget[nugget_guid].append(row)
            for nugget_guid, nugget_rows in by_nugget.items():
                payload_guids = [value(g) for row in nugget_rows for g in row["Payload"]]
                matched_blobs = [blobs_by_guid[g] for g in payload_guids if g in blobs_by_guid]
                counts = Counter()
                for b in matched_blobs:
                    counts.update(b["entity_types"])
                dynamic = [b for b in matched_blobs if b["entity_types"].get("DynamicSpawnerData", 0)]
                spawners = [s for b in dynamic for s in spawners_for_blob(b)]
                has_npc_tags = any(s["all_tags"] or s["any_tags"] for s in spawners)
                content.append({
                    "bucket_guid": bucket_guid, "bucket_name": graph[bucket_index]["Name"],
                    "nugget_guid": nugget_guid, "nugget_name": records[nugget_guid]["name"],
                    "layer_guids": [value(r["ContentId"]) for r in nugget_rows],
                    "blob_guids": [b["guid"] for b in matched_blobs],
                    "entity_types": dict(counts), "ordinary_candidate": bool(spawners) and has_npc_tags
                    and not any(counts[t] for t in FORBIDDEN),
                    "spawners": spawners,
                })
        arenas.append({"floor_guid": floor_guid, "floor_name": floor["name"],
                       "chunk_guid": chunk_guid, "chunk_name": records[chunk_guid]["name"],
                       "base_blob_guids": [b["guid"] for b in base_blobs],
                       "base_entity_types": dict(base_counts),
                       "content": content})
    arenas.sort(key=lambda r: (r.get("floor_name") or ""))
    result = {"source_db": str(DB), "source_blobs": str(BLOBS),
              "criteria": "FloorType=2; real worldlut floor->chunk->Nugget->QPURE mapping; nonempty tagged DynamicSpawnerData; no NpcInstance/BossGate/DirectorZone/EnemyWaveSpawner in selected Nugget payload",
              "arenas": arenas}
    (HERE / "arena-manifest.json").write_text(json.dumps(result, indent=2), encoding="utf8")
    compact = [{"floor": r["floor_name"], "floor_guid": r["floor_guid"],
                "chunk_guid": r.get("chunk_guid"),
                "candidates": [(c["nugget_name"], c["nugget_guid"], len(c["spawners"]))
                               for c in r.get("content", []) if c["ordinary_candidate"]]}
               for r in arenas]
    (HERE / "arena-manifest-compact.json").write_text(json.dumps(compact, indent=2), encoding="utf8")
    print(json.dumps(compact, indent=2))


if __name__ == "__main__":
    main()
