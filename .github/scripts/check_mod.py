#!/usr/bin/env python3
"""Static checks for the Stasis Capture mod that otherwise only fail in game.

Checks:
  * ModInfo.json is strict JSON with the fields the game requires.
  * Every translation CSV has 4 columns, unique keys, the same keys as
    English.csv and the same {n} placeholders per key.
  * Every translation key used in Code/ exists in English.csv.
  * ModCover.jpg exists and is 800x450, as the Workshop upload requires.
  * Every *.csproj is well-formed XML (e.g. no "--" inside comments).
  * Code/AssemblyInfo.cs has the same version as ModInfo.json.
  * The built DLL (0.15/Libraries/StasisCapture.dll) carries the ModInfo.json
    version, so a release can't ship a DLL from older code.

Usage: python3 .github/scripts/check_mod.py [mod_root]
Exit code: number of failed checks (0 = all good).
"""
import csv
import json
import re
import struct
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

KEY_PREFIX = "stasiscapture."
REQUIRED_MODINFO_FIELDS = ("Id", "Name", "Description", "ModVersion", "GameVersion")
COVER_SIZE = (800, 450)
PLACEHOLDER = re.compile(r"\{(\d+)\}")
# "key".T(...)
LITERAL_T = re.compile(r'"(stasiscapture\.[a-z0-9.]+)"\.T\(')
# const string Name = "key";  ...  Name.T(...)
CONST_KEY = re.compile(r'const string (\w+) = "(stasiscapture\.[a-z0-9.]+)";')
# [assembly: AssemblyInformationalVersion("0.4.0")]
ASSEMBLY_VERSION = re.compile(r'AssemblyInformationalVersion\("([^"]+)"\)')
DLL_PATH = "0.15/Libraries/StasisCapture.dll"

errors = []


def fail(message):
    """Records a failed check."""
    errors.append(message)
    print(f"FAIL {message}")


def ok(message):
    print(f"OK   {message}")


def check_modinfo(root):
    """ModInfo.json must be strict JSON and contain the required fields."""
    path = root / "ModInfo.json"
    try:
        info = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as e:
        fail(f"ModInfo.json is not valid JSON: {e}")
        return
    missing = [f for f in REQUIRED_MODINFO_FIELDS if not str(info.get(f, "")).strip()]
    if missing:
        fail(f"ModInfo.json misses required fields: {', '.join(missing)}")
        return
    ok(f"ModInfo.json: {info['Id']} {info['ModVersion']} for game {info['GameVersion']}")


def read_translations(path):
    """Returns {key: text} of a translation CSV; reports malformed rows."""
    rows = list(csv.reader(path.read_text(encoding="utf-8").splitlines()))
    result = {}
    for line_no, row in enumerate(rows[1:], start=2):  # first line is the header
        if not row:
            continue
        if len(row) != 4:
            fail(f"{path.name}:{line_no}: expected 4 columns, got {len(row)}")
            continue
        key, text = row[0], row[1]
        if key in result:
            fail(f"{path.name}:{line_no}: duplicate key {key}")
        result[key] = text
    return result


def placeholders(text):
    return sorted(set(PLACEHOLDER.findall(text)))


def check_translations(root):
    """All languages must match English.csv in keys and placeholders."""
    folder = root / "Translations"
    english_path = folder / "English.csv"
    if not english_path.exists():
        fail("Translations/English.csv is missing")
        return {}
    english = read_translations(english_path)

    for path in sorted(folder.glob("*.csv")):
        if path == english_path:
            continue
        check_language(path, english)
    ok(f"English.csv: {len(english)} keys")
    return english


def check_language(path, english):
    """Compares one language file with English."""
    other = read_translations(path)
    missing = sorted(set(english) - set(other))
    extra = sorted(set(other) - set(english))
    if missing:
        fail(f"{path.name}: missing keys {missing}")
    if extra:
        fail(f"{path.name}: keys not in English.csv {extra}")
    for key in sorted(set(english) & set(other)):
        if placeholders(english[key]) != placeholders(other[key]):
            fail(f"{path.name}: {key} placeholders {placeholders(other[key])}, "
                 f"English has {placeholders(english[key])}")
    if not (missing or extra):
        ok(f"{path.name}: same keys as English")


def check_code_keys(root, english):
    """Every key the code asks for must have an English text."""
    used = set()
    for source in sorted((root / "Code").rglob("*.cs")):
        used.update(translation_keys(source.read_text(encoding="utf-8")))
    used = {k for k in used if k.startswith(KEY_PREFIX)}
    missing = sorted(used - set(english))
    if missing:
        fail(f"keys used in Code/ but missing in English.csv: {missing}")
        return
    ok(f"all {len(used)} keys used in Code/ are translated")


def translation_keys(text):
    """Keys a C# source passes to the translator (.T)."""
    keys = set(LITERAL_T.findall(text))
    for name, key in CONST_KEY.findall(text):
        if re.search(rf"\b{name}\.T\(", text):
            keys.add(key)
    return keys


def jpeg_size(path):
    """Reads (width, height) from the first SOF marker of a JPEG file."""
    data = path.read_bytes()
    if data[:2] != b"\xff\xd8":
        raise ValueError("not a JPEG file")
    i = 2
    while i + 9 < len(data):
        if data[i] != 0xFF:
            raise ValueError("broken JPEG marker")
        marker = data[i + 1]
        length = struct.unpack(">H", data[i + 2:i + 4])[0]
        if 0xC0 <= marker <= 0xCF and marker not in (0xC4, 0xC8, 0xCC):
            height, width = struct.unpack(">HH", data[i + 5:i + 9])
            return width, height
        i += 2 + length
    raise ValueError("no size marker found")


def check_cover(root):
    """The Workshop upload asks for ModCover.jpg of 800x450."""
    path = root / "ModCover.jpg"
    if not path.exists():
        fail("ModCover.jpg is missing")
        return
    try:
        size = jpeg_size(path)
    except ValueError as e:
        fail(f"ModCover.jpg: {e}")
        return
    if size != COVER_SIZE:
        fail(f"ModCover.jpg is {size[0]}x{size[1]}, expected {COVER_SIZE[0]}x{COVER_SIZE[1]}")
        return
    ok("ModCover.jpg is 800x450")



def check_projects(root):
    """MSBuild refuses malformed project files before compiling anything."""
    projects = sorted(p for p in root.rglob("*.csproj") if "obj" not in p.parts)
    for path in projects:
        try:
            ET.parse(path)
        except ET.ParseError as e:
            fail(f"{path.relative_to(root)} is not valid XML: {e}")
            return
    ok(f"{len(projects)} project file(s) are valid XML")


def mod_version(root):
    """ModVersion from ModInfo.json, or None when it can't be read."""
    try:
        return json.loads((root / "ModInfo.json").read_text(encoding="utf-8"))["ModVersion"]
    except (OSError, ValueError, KeyError):
        return None  # check_modinfo already reported it


def check_assembly_info(root, version):
    """AssemblyInfo.cs is the version the DLLs get; it must follow ModInfo.json."""
    path = root / "Code" / "AssemblyInfo.cs"
    found = ASSEMBLY_VERSION.search(path.read_text(encoding="utf-8")) if path.exists() else None
    if not found:
        fail("Code/AssemblyInfo.cs has no AssemblyInformationalVersion")
        return
    if found.group(1) != version:
        fail(f"AssemblyInfo.cs version {found.group(1)} != ModInfo.json ModVersion {version}")
        return
    ok(f"AssemblyInfo.cs version {version}")



def version_blob(version):
    """Custom attribute blob of AssemblyInformationalVersion(version) as stored in a DLL:
    prolog 01 00, length-prefixed UTF-8 string, no named arguments (00 00)."""
    text = version.encode("utf-8")
    return b"\x01\x00" + bytes([len(text)]) + text + b"\x00\x00"


def check_dll(root, version):
    """The committed DLL must be built from code of this ModVersion."""
    path = root / DLL_PATH
    if not path.exists():
        fail(f"{DLL_PATH} is missing: build it with build.cmd")
        return
    if version_blob(version) not in path.read_bytes():
        fail(f"{DLL_PATH} is not version {version}: rebuild it with build.cmd")
        return
    ok(f"{DLL_PATH}: version {version}")


def main():
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    check_modinfo(root)
    english = check_translations(root)
    check_code_keys(root, english)
    check_cover(root)
    check_projects(root)
    version = mod_version(root)
    if version:
        check_assembly_info(root, version)
        check_dll(root, version)
    print(f"\n{len(errors)} problem(s)" if errors else "\nALL CHECKS PASSED")
    return len(errors)


if __name__ == "__main__":
    sys.exit(main())
