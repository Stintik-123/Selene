#!/usr/bin/env python3
"""Prepares a release: python3 tools/release.py 1.0.0

Turns the "## Unreleased" notes in CHANGELOG.md into this version, writes the version into theme/theme.yaml,
SeleneCore/extension.yaml (the README's release badge follows GitHub by itself), and adds the version to both Playnite installer manifests
(manifests/*.yaml, which Playnite reads to offer installs and updates). Then, on Windows: tools\\package.ps1 and
publish the release (see docs/guide/building.md, "Releasing").
"""
import datetime
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REPO = "https://github.com/mattxslv/Selene"


def replace_once(path, pattern, replacement):
    text = path.read_text(encoding="utf-8-sig")
    new, count = re.subn(pattern, replacement, text, count=1, flags=re.M)
    if count != 1:
        raise SystemExit(f"couldn't find {pattern!r} in {path.relative_to(ROOT)}")
    path.write_text(new, encoding="utf-8")


def main():
    if len(sys.argv) != 2 or not re.fullmatch(r"\d+\.\d+\.\d+", sys.argv[1]):
        raise SystemExit("usage: python3 tools/release.py <major.minor.patch>")
    version = sys.argv[1]
    today = datetime.date.today().isoformat()

    changelog = ROOT / "CHANGELOG.md"
    text = changelog.read_text(encoding="utf-8")
    match = re.search(r"^## Unreleased\n(.*?)(?=^## )", text, flags=re.M | re.S)
    if not match:
        raise SystemExit("CHANGELOG.md has no '## Unreleased' section followed by an older version")
    notes = [n.strip() for n in re.split(r"^- ", match.group(1), flags=re.M) if n.strip()]
    if not notes:
        raise SystemExit("nothing under '## Unreleased' in CHANGELOG.md")
    if f"## {version} " in text:
        raise SystemExit(f"{version} is already in CHANGELOG.md")
    changelog.write_text(text.replace("## Unreleased\n", f"## Unreleased\n\n## {version} - {today}\n", 1), encoding="utf-8")

    replace_once(ROOT / "theme" / "theme.yaml", r"^Version: .*$", f"Version: {version}")
    replace_once(ROOT / "SeleneCore" / "extension.yaml", r"^Version: .*$", f"Version: {version}")

    lines = "\n".join("      - >-\n        " + " ".join(n.split()) for n in notes)
    for manifest, file_name, api in (("Selene.yaml", "Selene.pthm", "2.10.0"), ("SeleneCore.yaml", "SeleneCore.pext", "6.0.0")):
        path = ROOT / "manifests" / manifest
        entry = (f"  - Version: {version}\n    RequiredApiVersion: {api}\n    ReleaseDate: {today}\n"
                 f"    PackageUrl: {REPO}/releases/download/v{version}/{file_name}\n    Changelog:\n{lines}\n")
        replace_once(path, r"^Packages:\n", "Packages:\n" + entry.replace("\\", "\\\\"))
    print(f"prepared {version}: commit, then run tools\\package.ps1 on Windows and publish v{version}")


main()
