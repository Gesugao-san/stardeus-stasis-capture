#!/usr/bin/env python3
"""Packages Stasis Capture for a GitHub release.

Creates <out>/StasisCapture-<version>.zip with a ready-to-install mod folder
(ModInfo.json, cover, license, translations and the built DLL of each game
version) and <out>/notes.md with the CHANGELOG.md section of that version.

Run check_mod.py first: it verifies the DLLs match ModInfo.json's version.

Usage: python3 .github/scripts/package_release.py [out_dir] [mod_root]
On GitHub Actions, writes version, tag, title, zip and notes to $GITHUB_OUTPUT.
"""
import json
import os
import re
import sys
import zipfile
from pathlib import Path

MOD_FOLDER = "StasisCapture"
DLL_NAME = "StasisCapture.dll"
# Files and folders a player needs; DLLs are added per game version
RUNTIME_FILES = ("ModInfo.json", "ModCover.jpg", "LICENSE")
RUNTIME_FOLDERS = ("Translations",)
# Trailing game version label of the mod name, e.g. " [v0.15]"
GAME_VERSIONS_SUFFIX = re.compile(r"\s*\[[^\]]*\]\s*$")


def changelog_section(root, version):
    """Body of the "## <version>" section of CHANGELOG.md."""
    text = (root / "CHANGELOG.md").read_text(encoding="utf-8")
    found = re.search(rf"^## {re.escape(version)}\s*$(.*?)(?=^## |\Z)", text, re.M | re.S)
    if not found or not found.group(1).strip():
        raise SystemExit(f"CHANGELOG.md has no '## {version}' section: add one before releasing")
    return found.group(1).strip() + "\n"


def runtime_paths(root):
    """Paths, relative to the mod root, that go into the release zip."""
    paths = [Path(name) for name in RUNTIME_FILES]
    for folder in RUNTIME_FOLDERS:
        paths += sorted(p.relative_to(root) for p in (root / folder).rglob("*") if p.is_file())
    dlls = sorted(p.relative_to(root) for p in root.glob(f"0.*/Libraries/{DLL_NAME}"))
    if not dlls:
        raise SystemExit(f"no <game version>/Libraries/{DLL_NAME} found: build the mod first")
    return paths + dlls


def write_zip(root, paths, target):
    """Zips the paths under a single mod folder, so it unpacks straight into Mods/."""
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in paths:
            archive.write(root / path, f"{MOD_FOLDER}/{path.as_posix()}")


def set_outputs(values):
    """Passes values to the next workflow steps; printed as well for local runs."""
    for key, value in values.items():
        print(f"{key}={value}")
    output = os.environ.get("GITHUB_OUTPUT")
    if output:
        with open(output, "a", encoding="utf-8") as f:
            f.writelines(f"{key}={value}\n" for key, value in values.items())


def main():
    out = Path(sys.argv[1] if len(sys.argv) > 1 else "dist").resolve()
    root = Path(sys.argv[2] if len(sys.argv) > 2 else ".").resolve()
    info = json.loads((root / "ModInfo.json").read_text(encoding="utf-8"))
    version = info["ModVersion"]

    out.mkdir(parents=True, exist_ok=True)
    notes = out / "notes.md"
    notes.write_text(changelog_section(root, version), encoding="utf-8")
    paths = runtime_paths(root)
    archive = out / f"{MOD_FOLDER}-{version}.zip"
    write_zip(root, paths, archive)

    for path in paths:
        print(f"  + {MOD_FOLDER}/{path.as_posix()}")
    set_outputs({
        "version": version,
        "tag": f"v{version}",
        # The Workshop title lists game versions; the release title names the mod version
        "title": f"{GAME_VERSIONS_SUFFIX.sub('', info['Name'])} {version}",
        "zip": archive,
        "notes": notes,
    })


if __name__ == "__main__":
    main()
