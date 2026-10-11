"""Check room-seven correction against inspected shipped root, preserving R15 rooms."""
import copy
import json
import subprocess
from pathlib import Path

TASK = Path(__file__).resolve().parents[2]
BASE = "06d9c2ba614de45a185557ff980798f49b9f153f"


def baseline(path):
    return json.loads(subprocess.check_output(["git", "-C", str(TASK), "show", f"{BASE}:{path}"]))


def validate(bindings, rooms, evidence):
    original = baseline("research/enemy-restoration/bindings.json")
    original_rooms = baseline("research/third-trial/rooms.json")
    prefabs = evidence["prefabs"]
    if len(prefabs) != 1:
        raise ValueError("Expected exactly one room-seven source audit")
    root = prefabs[0]
    if root["pooled_root"] != "deadTideCurvedSwordViewVariant" or root["root_script_class"] != "EnemyWeaponView":
        raise ValueError("Inspected source is not the intended curved-sword weapon root")
    if root["root_weapon_data_guid"] != root["weapon_guid"]:
        raise ValueError("Weapon wrapper and root disagree")
    if root["source_melee_action_guids"] != root["matched_child_action_guids"]:
        raise ValueError("Inspected weapon lacks required child action views")
    expected = copy.deepcopy(original)
    p = expected["profiles"][6]
    if p["key"] != "dead_tide_curved_sword":
        raise ValueError("R15 room-seven identity changed")
    p["weapon_guid"] = root["root_weapon_data_guid"]
    p["weapon_name"] = root["weapon_name"]
    if bindings != expected:
        raise ValueError("Candidate binding differs from inspected curved sword or changes other R15 fields")
    if rooms != original_rooms:
        raise ValueError("R17 must retain all R15 rooms, spawners and ordering")


def main():
    bindings = json.loads((TASK / "research/enemy-restoration/bindings.json").read_text(encoding="utf8"))
    rooms = json.loads((TASK / "research/third-trial/rooms.json").read_text(encoding="utf8"))
    evidence = json.loads((TASK / "research/third-trial/weapon-prefab-audit.json").read_text(encoding="utf8"))
    validate(bindings, rooms, evidence)
    faults = []
    for label in ("old_axe_wrapper", "extra_profile", "changed_room", "wrong_root_id", "missing_action_view"):
        b, r, e = copy.deepcopy(bindings), copy.deepcopy(rooms), copy.deepcopy(evidence)
        if label == "old_axe_wrapper":
            b["profiles"][6]["weapon_guid"] = 157205877300334101
        elif label == "extra_profile":
            b["profiles"].append(copy.deepcopy(b["profiles"][0]))
        elif label == "changed_room":
            r["stages"][0]["spawner_guids"] = []
        elif label == "wrong_root_id":
            e["prefabs"][0]["root_weapon_data_guid"] = 2230631566238253937
        else:
            e["prefabs"][0]["matched_child_action_guids"].pop()
        try:
            validate(b, r, e)
        except ValueError:
            faults.append(label)
        else:
            raise AssertionError(f"Fault was accepted: {label}")
    result = {"passed": True, "profiles": len(bindings["profiles"]), "rooms": len(rooms["stages"]),
              "weapon_guid": bindings["profiles"][6]["weapon_guid"], "rejected_faults": faults,
              "runtime_verified": False, "game_dlls_executed": False}
    target = TASK / "artifacts/third-launch/r17-repair/bindings-green.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf8")
    print("PASS: source-backed room-seven binding; ten unchanged R15 rooms; five rejected fault cases")


if __name__ == "__main__":
    main()
