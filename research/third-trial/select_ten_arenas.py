"""Select ten distinct shipped Arena chunks with ordinary NPC spawner layers."""
import json
from pathlib import Path

TASK_ROOT = Path(__file__).resolve().parents[2]
ROOT = TASK_ROOT.parents[1]
HERE = TASK_ROOT / "artifacts/first-trial-spawning"
HERE.mkdir(parents=True, exist_ok=True)
FLOORS = [
    "infiniteDungeonArenaAFloorData",
    "infiniteDungeonArenaD1FloorData",
    "infiniteDungeonArenaE1FloorData",
    "infiniteDungeonArenaEFloorData",
    "infiniteDungeonArenaIFloorData",
    "infiniteDungeonArenaJFloorData",
    "infiniteDungeonArenaM1FloorData",
    "infiniteDungeonArenaP1FloorData",
    "infiniteDungeonArenaWFloorData",
    "infiniteDungeonLargeWaterfallAFloorData",
]


def main():
    all_arenas = json.loads((HERE / "arena-manifest.json").read_text(encoding="utf8"))["arenas"]
    by_name = {r["floor_name"]: r for r in all_arenas}
    profiles = json.loads((TASK_ROOT / "research/enemy-restoration/bindings.json").read_text(encoding="utf8"))["profiles"]
    assert len(FLOORS) == len(profiles) == 10
    result = []
    for index, (floor_name, profile) in enumerate(zip(FLOORS, profiles)):
        arena = by_name[floor_name]
        nugget = next(c for c in arena["content"] if c["nugget_name"] == "plagued")
        assert nugget["ordinary_candidate"]
        assert not any(arena["base_entity_types"].get(t, 0) for t in (
            "NpcInstanceData", "DynamicSpawnerData", "EnemyWaveSpawnerStaticInstanceData",
            "BossGateStaticInstanceData", "DirectorZone"))
        assert nugget["spawners"] and all(s["all_tags"] or s["any_tags"] for s in nugget["spawners"])
        assert any(not s["spawn_conditions"] and s["miss_chance"] == 0 for s in nugget["spawners"])
        assert len(nugget["layer_guids"]) == 1
        aux = []
        for bucket_guid in sorted({c["bucket_guid"] for c in arena["content"]} - {nugget["bucket_guid"]}):
            options = [c for c in arena["content"] if c["bucket_guid"] == bucket_guid]
            safe = [c for c in options if not any(c["entity_types"].get(t, 0) for t in (
                "NpcInstanceData", "DynamicSpawnerData", "EnemyWaveSpawnerStaticInstanceData",
                "BossGateStaticInstanceData", "DirectorZone"))]
            assert safe, (floor_name, bucket_guid)
            selected = next((c for c in safe if c["nugget_name"] == "default"), None)
            if selected is None:
                selected = next((c for c in safe if c["nugget_name"] == "dark"), safe[0])
            aux.append({"bucket_guid": bucket_guid, "bucket_name": selected["bucket_name"],
                        "nugget_guid": selected["nugget_guid"], "nugget_name": selected["nugget_name"],
                        "layer_guids": selected["layer_guids"],
                        "blob_guids": selected["blob_guids"],
                        "entity_types": selected["entity_types"]})
        restored_guid_u64 = 4000000000000000000 + index * 10 + 3 | 0xC000000000000000
        restored_guid = restored_guid_u64 - (1 << 64)
        result.append({
            "room": index + 1, "profile_key": profile["key"],
            "restored_npc_guid": restored_guid,
            "source_npc_template_guid": profile["npc_template_guid"],
            "floor_name": floor_name, "floor_guid": arena["floor_guid"],
            "chunk_name": arena["chunk_name"], "chunk_guid": arena["chunk_guid"],
            "base_blob_guids": arena["base_blob_guids"],
            "base_entity_types": arena["base_entity_types"],
            "bucket_name": nugget["bucket_name"], "bucket_guid": nugget["bucket_guid"],
            "nugget_name": nugget["nugget_name"], "nugget_guid": nugget["nugget_guid"],
            "layer_guid": nugget["layer_guids"][0],
            "qpure_blob_guids": nugget["blob_guids"],
            "auxiliary_nuggets": aux,
            "entry_nugget_guids": [nugget["nugget_guid"]] + [x["nugget_guid"] for x in aux],
            "entity_types": nugget["entity_types"], "spawners": nugget["spawners"],
        })
    assert len({r["floor_guid"] for r in result}) == 10
    assert len({r["chunk_guid"] for r in result}) == 10
    assert len({r["nugget_guid"] for r in result}) == 10
    assert len({s["guid"] for r in result for s in r["spawners"]}) == sum(len(r["spawners"]) for r in result)
    output = {"provenance": "worldlut floor->chunk->bucket->fixed Nugget->layer->QPURE; QPURE redecoded from original QDB",
              "filter": "No NpcInstanceData, EnemyWaveSpawnerStaticInstanceData, BossGateStaticInstanceData or DirectorZone in chosen content; every DynamicSpawnerData has nonempty AllTags or AnyTags",
              "rooms": result}
    (HERE / "chosen-ten-arenas.json").write_text(json.dumps(output, indent=2), encoding="utf8")
    with (HERE / "chosen-ten-arenas.md").open("w", encoding="utf8") as out:
        out.write("# 十个普通竞技场固定内容层\n\n")
        out.write("每行均来自已出货 worldlut 关联与原 QDB QPURE 实体复解；完整动态出生点 GUID、位置、标签、难度、miss、多人条件与各房**全部**应固定选择的Nugget（内容、环境、灯光）见 `chosen-ten-arenas.json`。`plagued` 是原内容 Nugget 的内部名，不决定替换后的 NPC 类型。\n\n")
        out.write("| 房 | 恢复profile | Floor GUID | Chunk GUID | Bucket GUID | Nugget GUID | Layer GUID | QPURE GUIDs | 动态点 |\n")
        out.write("|---:|---|---:|---:|---:|---:|---:|---|---:|\n")
        for r in result:
            out.write(f'| {r["room"]} | `{r["profile_key"]}` | {r["floor_guid"]} | {r["chunk_guid"]} | {r["bucket_guid"]} | {r["nugget_guid"]} | {r["layer_guid"]} | {", ".join(r["qpure_blob_guids"])} | {len(r["spawners"])} |\n')
        out.write("\n所有十个选定内容层的 `entity_types` 都只有动态出生点、PatrolRoute、relations/debug；没有静态 NPC、BossGate、DirectorZone 或 EnemyWaveSpawner。十个 chunk 的常驻基础层也经 QPURE 清点，均无 NpcInstanceData、DynamicSpawnerData、BossGate、DirectorZone 或 EnemyWaveSpawner。**每个其他bucket也显式选择无NPC/spawner的环境/灯光Nugget**，避免随机落到带静态NPC的陷阱层。原生 miss/激活/多人条件仍可能使一部分出生点不产生敌人。\n")
    print("selected", len(result), "rooms; dynamic spawners", sum(len(r["spawners"]) for r in result))


if __name__ == "__main__":
    main()
