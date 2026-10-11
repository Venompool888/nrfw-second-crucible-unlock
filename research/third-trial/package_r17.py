"""Freeze a verified R17 candidate; never installs or starts the game."""
import datetime
import hashlib
import json
import shutil
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
EVIDENCE = ROOT / "artifacts/third-launch/r17-repair"
VERSION = "0.9.17-third-trial-candidate-r17"
BRANCH = "feature/third-trial-presentation-repair-20261011"
BASE = "7db4e4a06d04cb1f6ca1d8cfa1a3aaceaeb66a08"
R15 = "06d9c2ba614de45a185557ff980798f49b9f153f"


def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def main():
    assert git("branch", "--show-current") == BRANCH
    assert git("rev-parse", "origin/main") == BASE
    assert not git("status", "--porcelain"), "Commit reviewed source before freezing"
    core = ROOT / "src/CrucibleUnlock/bin/Release/CrucibleUnlock.dll"
    audio = ROOT / "src/BowgunRepair/bin/Release/NRFWBowgunAudioSync.dll"
    verification = read(EVIDENCE / "verification/summary.json")
    assert verification["passed"] and len(verification["checks"]) >= 28
    assert all(c["exit_code"] == 0 for c in verification["checks"])
    assert verification["core_sha256"] == sha(core)
    assert sha(audio) == "e861d4babf57da77259bb2dffca50bb51257c7d8c3adb6d8eb0984d5e333d7c7"
    assert read(EVIDENCE / "bindings-green.json")["passed"]
    assert read(ROOT / "research/third-trial/r17-curved-sword-closure.json")["missing"] == []
    protected = read(EVIDENCE / "protected-before.json")
    for item in protected:
        assert sha(Path(item["path"])) == item["sha256"], item["path"]
    installed = next(i for i in protected if i["path"].endswith("/Mods/CrucibleUnlock.dll"))
    installation_record = ROOT.parents[1] / "research/third-trial-restored-enemies-20261010/artifacts/third-launch/current-installation.json"
    installation = read(installation_record)
    assert installation["sha256"] == installed["sha256"], "Installation record differs from the preserved installed core"
    observed_installation = {"record": str(installation_record), "record_sha256": sha(installation_record),
                             "plugin": installation["plugin"], "source_commit": installation["source_commit"],
                             "sha256": installed["sha256"], "preserved": True}
    target = ROOT / "dist/third-trial-20261011-r17"
    archive = target.with_suffix(".zip")
    assert not target.exists() and not archive.exists(), "Never replace an existing frozen candidate"
    target.mkdir(parents=True)
    shutil.copy2(core, target / "CrucibleUnlock.dll")
    shutil.copy2(audio, target / "NRFWBowgunAudioSync.dll")
    shutil.copy2(ROOT / "research/third-trial/r17-repair.md", target / "README.md")
    (target / "MelonPreferences.cfg").write_text(
        "# R17 example only. Preserve the complete installed UserData configuration.\n"
        "[CrucibleUnlock]\nboss_trace_drop_amount = 300\n", encoding="utf8")
    evidence = target / "evidence"
    evidence.mkdir()
    shutil.copy2(EVIDENCE / "protected-before.json", evidence / "protected-before.json")
    shutil.copy2(installation_record, evidence / "observed-installation.json")
    shutil.copytree(EVIDENCE / "verification", evidence / "verification")
    voice = evidence / "voice"
    voice.mkdir()
    for name in ("tests-red.json", "tests-red-reproduced.json", "tests-green.json", "tests-red-console.txt", "tests-green-console.txt",
                 "test-input-hashes.json", "audio-input-hashes.json", "init-target-absence.json",
                 "dialogue-routing.json", "donor-media-closure.json", "weapon-event-closure.json"):
        path = EVIDENCE / "voice" / name
        if path.is_file():
            shutil.copy2(path, voice / name)
    weapon = evidence / "weapon-fx"
    weapon.mkdir()
    for name in ("source-audit.json", "donor-match-audit.json", "fire-view-audit.json", "hirc-closure-audit.json"):
        path = EVIDENCE / "weapon-fx" / name
        assert path.is_file(), str(path)
        shutil.copy2(path, weapon / name)
    log_audit = evidence / "log-audit"
    log_audit.mkdir()
    for name in ("report.md", "summary.json", "evidence-excerpts.txt", "quantum-view-nre-supplement.md"):
        source = ROOT.parents[1] / "research/third-trial-r17-feedback-20261011/artifacts/log-audit" / name
        assert source.is_file(), str(source)
        shutil.copy2(source, log_audit / name)
    for path in (
        EVIDENCE / "final-review.md", EVIDENCE / "bindings-red.txt",
        EVIDENCE / "bindings-green.json", ROOT / "research/third-trial/r17-voice.md",
        ROOT / "research/third-trial/r17-weapon-fx.md",
        ROOT / "research/third-trial/weapon-prefab-audit.json",
        ROOT / "research/third-trial/r17-curved-sword-closure.json",
    ):
        assert path.is_file(), str(path)
        shutil.copy2(path, evidence / path.name)
    source_paths = git("ls-files", "src/CrucibleUnlock", "research/enemy-restoration/bindings.json",
                       "research/third-trial/rooms.json").splitlines()
    assert all((ROOT / name).stat().st_mtime <= core.stat().st_mtime for name in source_paths), \
        "Source changed after the verified core build"
    source_hashes = {name: sha(ROOT / name) for name in source_paths}
    manifest = {"version": VERSION, "source_commit": git("rev-parse", "HEAD"), "branch": BRANCH,
                "base_main": BASE, "r15_source_dependency": R15, "observed_installation": observed_installation,
                "utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                "core_sha256": sha(core), "audio_sha256": sha(audio), "source_sha256": source_hashes,
                "installed": False, "game_started": False, "runtime_verified": False,
                "game_dlls_executed": False, "main_merged": False, "protected_files_verified": len(protected),
                "offline_tasks": len(verification["checks"]),
                "files": {p.relative_to(target).as_posix(): sha(p) for p in sorted(target.rglob("*")) if p.is_file()}}
    (target / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf8")
    (target / "SHA256SUMS.txt").write_text(
        "".join(f"{sha(p)}  {p.relative_to(target).as_posix()}\n" for p in sorted(target.rglob("*")) if p.is_file()),
        encoding="utf8")
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as output:
        for path in sorted(target.rglob("*")):
            if path.is_file():
                output.write(path, path.relative_to(target).as_posix())
    with zipfile.ZipFile(archive) as output:
        assert output.testzip() is None
        for path in sorted(target.rglob("*")):
            if path.is_file():
                assert output.read(path.relative_to(target).as_posix()) == path.read_bytes()
    for item in protected:
        assert sha(Path(item["path"])) == item["sha256"], item["path"]
    assert sha(installation_record) == observed_installation["record_sha256"]
    result = {"version": VERSION, "source_commit": manifest["source_commit"], "package": str(target),
              "archive": str(archive), "archive_sha256": sha(archive), "core_sha256": sha(core),
              "offline_tasks": manifest["offline_tasks"], "installed": False, "game_started": False,
              "runtime_verified": False, "main_merged": False, "observed_installation": observed_installation,
              "protected_files_verified": len(protected)}
    (EVIDENCE / "candidate.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf8")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
