"""Read-only inspection of the shipped room-seven curved-sword pooled prefab.

Reads the serialized Unity bundle node; never loads a game DLL or starts the game.
Writes only a compact project report.
"""
import json
import hashlib
import sqlite3
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT.parents[1]
sys.path.insert(0, str(PROJECT / "research/crucible-deep/combat"))
from bundle_select import Select, BASE  # noqa: E402

NAMES = (
    "deadTideCurvedSwordViewVariant",
)
WEAPON_NAMES = (
    "deadTideCurvedSwordData",
)
EXPECTED_POOLED_SHA256 = "3c41fb36d6c6cd64845532c3d9d0e59316ee5018f8a40e7b724ab1cc03d7dcba"


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def main():
    bundle = next(BASE.glob("pooled_prefabs_assets_all_*.bundle"))
    bundle_sha256 = digest(bundle)
    if bundle_sha256 != EXPECTED_POOLED_SHA256:
        raise ValueError("Shipped pooled prefab bundle hash differs from audited build")
    source = Select(bundle)
    env = source.load(0)
    sf = next(iter(env.files.values()))
    mono_bundle = Select(next(BASE.glob("*monoscripts*.bundle")))
    mono_sf = next(iter(mono_bundle.load(0).files.values()))
    assert mono_bundle.nodes[0]["path"] in sf.externals[0].path
    qdb_bundle = Select(next(BASE.glob("qdb_assets_all_*.bundle")))
    qdb_sf = next(iter(qdb_bundle.load(0).files.values()))
    assert source.nodes[0]["path"] in qdb_sf.externals[2].path
    ab = next(o for o in sf.objects.values() if o.type.name == "AssetBundle").read_typetree()
    qcon = sqlite3.connect(f"file:{(PROJECT / 'research/quantum-runtime/quantum-full.sqlite').as_posix()}?mode=ro", uri=True)
    ucon = sqlite3.connect(f"file:{(PROJECT / 'research/item-db/output/items.sqlite').as_posix()}?mode=ro", uri=True)
    lut = json.loads(ucon.execute("select raw_json from objects where script_type='WorldLUTAsset'").fetchone()[0])
    assert qdb_bundle.nodes[0]["path"] in sf.externals[2].path
    def quantum_guid(pointer):
        if not isinstance(pointer, dict) or pointer.get("m_FileID") != 3 or not pointer.get("m_PathID"):
            return None
        row = ucon.execute("select asset_guid from objects where serialized_file=? and path_id=?",
                           (qdb_bundle.nodes[0]["path"], str(pointer["m_PathID"]))).fetchone()
        return int(row[0]) if row and row[0] else None
    results = []
    compact = []
    def script_class(tree):
        ptr = tree.get("m_Script")
        if not ptr or ptr["m_FileID"] != 1:
            return None
        target = mono_sf.objects.get(ptr["m_PathID"])
        if target is None:
            return None
        data = target.read_typetree()
        return ".".join(part for part in (data.get("m_Namespace"), data.get("m_ClassName")) if part)

    def child_components(game_object):
        queue = [game_object]
        seen = set()
        result = set()
        while queue:
            go = queue.pop()
            if go in seen:
                continue
            seen.add(go)
            tree = sf.objects[go].read_typetree()
            for component in tree.get("m_Component", []):
                ptr = component["component"]
                if ptr["m_FileID"] or not ptr["m_PathID"]:
                    continue
                result.add(ptr["m_PathID"])
                obj = sf.objects.get(ptr["m_PathID"])
                if obj is None or obj.type.name != "Transform":
                    continue
                transform = obj.read_typetree()
                for child in transform.get("m_Children", []):
                    if child["m_FileID"] or not child["m_PathID"]:
                        continue
                    child_obj = sf.objects.get(child["m_PathID"])
                    if child_obj is None:
                        continue
                    child_go = child_obj.read_typetree()["m_GameObject"]
                    if child_go["m_FileID"] == 0 and child_go["m_PathID"]:
                        queue.append(child_go["m_PathID"])
        return result
    for name, weapon_name in zip(NAMES, WEAPON_NAMES):
        matches = [(path, info) for path, info in ab["m_Container"]
                   if Path(path).stem == name]
        assert len(matches) == 1, (name, len(matches))
        path, info = matches[0]
        root = sf.objects[info["asset"]["m_PathID"]]
        if root.type.name != "GameObject":
            raise ValueError(f"{name}: pooled root is not a GameObject")
        root_tree = root.read_typetree()
        component_rows = []
        for c in root_tree.get("m_Component", []):
            pointer = c["component"]
            obj = sf.objects.get(pointer["m_PathID"]) if pointer["m_FileID"] == 0 else None
            if obj is None:
                component_rows.append({"missing": pointer})
                continue
            tree = obj.read_typetree()
            component_rows.append({"type": obj.type.name, "path_id": obj.path_id,
                                   "script_class": script_class(tree),
                                   "keys": list(tree)[:24],
                                   "binding_fields": {k: tree[k] for k in
                                                      ("WeaponData", "RightHandWeapon", "LeftHandWeapon",
                                                       "PreviewRig", "MovementAnimation") if k in tree}})
        preload = ab["m_PreloadTable"][info["preloadIndex"]:info["preloadIndex"]+info["preloadSize"]]
        preload_ids = {p["m_PathID"] for p in preload if p["m_FileID"] == 0}
        descendant_ids = child_components(root.path_id)
        types = Counter()
        mono_keys = Counter()
        action_candidates = []
        quantum_views = []
        for pointer in preload:
            if pointer["m_FileID"]:
                types["external"] += 1
                continue
            obj = sf.objects.get(pointer["m_PathID"])
            if obj is None:
                types["missing"] += 1
                continue
            types[obj.type.name] += 1
            if obj.type.name != "MonoBehaviour":
                continue
            try:
                tree = obj.read_typetree()
            except Exception:
                continue
            mono_keys.update(tree.keys())
            if "QuantumAsset" in tree:
                guid = quantum_guid(tree["QuantumAsset"])
                quantum_views.append({"path_id": obj.path_id, "action_guid": guid,
                                      "script_class": script_class(tree),
                                      "root_descendant": obj.path_id in descendant_ids,
                                      "timeline": tree.get("Timeline"),
                                      "main_animator_static": tree.get("MainAnimatorStatic"),
                                      "main_animator_ref": tree.get("MainAnimatorRef")})
            if any("action" in key.lower() or "weapon" in key.lower() for key in tree):
                action_candidates.append({"path_id": obj.path_id, "name": tree.get("m_Name"),
                                          "keys": list(tree),
                                          "fields": {k: v for k, v in tree.items()
                                                     if k == "Id" or any(token in k.lower() for token in
                                                            ("action", "weapon", "guid", "rig", "gender", "hand"))}})
        weapon = json.loads(qcon.execute("select raw_json from records where internal_name=?", (weapon_name,)).fetchone()[0])
        source_guid = weapon["Guid"]["Value"]
        root_weapon = next((c for c in component_rows if c["script_class"] == "EnemyWeaponView"), None)
        if root_weapon is None or root_weapon["binding_fields"]["WeaponData"]["Id"]["Value"] != source_guid:
            raise ValueError(f"{name}: missing/wrong EnemyWeaponView or WeaponData binding")
        choices = [a for g in weapon["ActionList"]["ActionGroups"] for a in g["Actions"]]
        choices += [u["Action"] for u in weapon["UnlockActions"]["Actions"]]
        action_guids = sorted({a["Data"]["Id"]["Value"] for a in choices} |
                              {c["Action"]["Id"]["Value"] for a in choices for c in a["ComboParams"]})
        source_views = []
        paired_views = []
        for action_guid in action_guids:
            row = ucon.execute("select internal_name,raw_json from objects where asset_guid=?", (str(action_guid),)).fetchone()
            if row is None:
                raise ValueError(f"{name}: source action has no Unity wrapper {action_guid}")
            wrapped = json.loads(row[1])
            ptr = wrapped.get("View")
            target = sf.objects.get(ptr["m_PathID"]) if ptr and ptr["m_PathID"] in sf.objects else None
            target_tree = target.read_typetree() if target else None
            source_views.append({"action_guid": action_guid, "action_name": row[0],
                                 "source_view_ptr": ptr,
                                 "source_view_prefab_ref": wrapped.get("ViewPrefabRef"),
                                 "source_view_in_pooled_node": bool(ptr and ptr["m_PathID"] in sf.objects),
                                 "source_view_in_weapon_preload": bool(ptr and ptr["m_PathID"] in preload_ids),
                                 "source_view_object_type": target.type.name if target else None,
                                 "source_view_script_class": script_class(target_tree) if target_tree else None,
                                 "source_view_quantum_asset": target_tree.get("QuantumAsset") if target_tree else None,
                                 "source_view_quantum_guid": quantum_guid(target_tree.get("QuantumAsset")) if target_tree else None,
                                 "source_view_object_keys": list(target_tree) if target_tree else [],
                                 "source_view_object_fields": {k: target_tree[k] for k in
                                                               ("m_Name", "m_Script", "m_GameObject", "ActionType",
                                                                "Action", "ActionData", "Timeline", "TimelineData")
                                                               if target_tree and k in target_tree}})
            if ptr:
                if ptr["m_FileID"] != 3 or target is None or script_class(target_tree) != "Moon.Forsaken.MeleeAttackView" or quantum_guid(target_tree.get("QuantumAsset")) != action_guid:
                    raise ValueError(f"{name}: source action View pointer/type/QDB binding mismatch {action_guid}")
            else:
                ref = wrapped.get("ViewPrefabRef", {}).get("Guid", {})
                matches = [i for i, item in enumerate(lut["PooledPrefabs"]) if item["PrefabGuid"] == ref]
                if len(matches) != 1:
                    raise ValueError(f"{name}: paired action pooled ViewPrefabRef missing {action_guid}")
                debug_name = lut["DebugNames"][matches[0]]
                containers = [(p, i) for p, i in ab["m_Container"] if Path(p).stem == debug_name]
                if len(containers) != 1:
                    raise ValueError(f"{name}: paired action prefab container missing {action_guid}")
                paired_root = sf.objects[containers[0][1]["asset"]["m_PathID"]]
                paired_tree = paired_root.read_typetree()
                paired_components = [sf.objects[c["component"]["m_PathID"]].read_typetree()
                                     for c in paired_tree.get("m_Component", []) if c["component"]["m_FileID"] == 0]
                paired_classes = [script_class(component) for component in paired_components]
                paired_qguids = [quantum_guid(component.get("QuantumAsset")) for component in paired_components]
                paired_views.append({"action_guid": action_guid, "pool_index": matches[0],
                                     "pool_name": debug_name, "container": containers[0][0],
                                     "root_type": paired_root.type.name,
                                     "root_component_classes": paired_classes,
                                     "root_component_quantum_guids": paired_qguids})
                if (paired_root.type.name != "GameObject" or
                        not any(cls and "PairedActionView" in cls and guid == action_guid
                                for cls, guid in zip(paired_classes, paired_qguids))):
                    raise ValueError(f"{name}: paired action root class mismatch {action_guid}")
        source_melee = [s for s in source_views if s["source_view_ptr"]]
        descendant_by_guid = {v["action_guid"]: v for v in quantum_views
                              if v["root_descendant"] and v["action_guid"]}
        missing = [s["action_guid"] for s in source_melee
                   if s["action_guid"] not in descendant_by_guid or
                   descendant_by_guid[s["action_guid"]]["script_class"] != s["source_view_script_class"]]
        if missing:
            raise ValueError(f"{name}: weapon root lacks required action views {missing}")
        if (not root_weapon["binding_fields"]["RightHandWeapon"]["Transform"]["m_PathID"] or
                not root_weapon["binding_fields"]["PreviewRig"]["m_PathID"]):
            raise ValueError(f"{name}: missing right-hand transform or preview rig")
        results.append({"name": name, "path": path, "root_type": root.type.name,
                        "root_name": root_tree.get("m_Name"), "root_components": component_rows,
                        "preload_size": info["preloadSize"], "preload_types": dict(types),
                        "mono_keys": dict(mono_keys), "action_candidates": action_candidates,
                        "source_action_views": source_views,
                        "quantum_action_views": quantum_views,
                        "root_descendant_component_count": len(descendant_ids),
                        "paired_action_views": paired_views})
        compact.append({"weapon_name": weapon_name, "weapon_guid": source_guid,
                        "pooled_root": name, "root_script_class": root_weapon["script_class"],
                        "root_weapon_data_guid": root_weapon["binding_fields"]["WeaponData"]["Id"]["Value"],
                        "right_hand_transform": root_weapon["binding_fields"]["RightHandWeapon"]["Transform"]["m_PathID"],
                        "left_hand_transform": root_weapon["binding_fields"]["LeftHandWeapon"]["Transform"]["m_PathID"],
                        "preview_rig": root_weapon["binding_fields"]["PreviewRig"]["m_PathID"],
                        "source_melee_action_guids": [s["action_guid"] for s in source_melee],
                        "matched_child_action_guids": [s["action_guid"] for s in source_melee],
                        "child_melee_views": sum(v["script_class"] == "Moon.Forsaken.MeleeAttackView" and v["root_descendant"] for v in quantum_views),
                        "paired_action_views": paired_views,
                        "source_pointer_same_as_child": sum(s["source_view_in_weapon_preload"] for s in source_melee),
                        "limits": "Matched child ActionView GUIDs and root bindings do not prove the borrowed character rig plays every animation in game."})
        print(name, root.type.name, len(component_rows), len(action_candidates), flush=True)
    detail = ROOT / "artifacts/third-trial/weapon-prefab-audit-detail.json"
    detail.parent.mkdir(parents=True, exist_ok=True)
    detail.write_text(json.dumps({"source_bundle": str(bundle), "prefabs": results},
                                 ensure_ascii=False, indent=2), encoding="utf8")
    out = ROOT / "research/third-trial/weapon-prefab-audit.json"
    out.write_text(json.dumps({"source_bundle": str(bundle), "source_bundle_sha256": bundle_sha256,
                               "cab_links": {"qdb_asset_action_view_file_id_3": source.nodes[0]["path"],
                                             "pooled_quantum_asset_file_id_3": qdb_bundle.nodes[0]["path"],
                                             "pooled_monoscript_file_id_1": mono_bundle.nodes[0]["path"]},
                               "prefabs": compact,
                               "detail": str(detail.relative_to(ROOT)).replace("\\", "/")},
                              ensure_ascii=False, indent=2), encoding="utf8")
    print(out)


if __name__ == "__main__":
    main()
