#!/usr/bin/env python3
"""Generates repo.json, the store manifest for the third-party plugin repository.

Every field is derived from something that already exists - the plugin manifest, the
csproj version, and the Dalamud SDK version - so the store entry cannot drift out of step
with what was actually built. Run by the release workflow; safe to run by hand.

Download links point at releases/latest rather than a pinned tag, so publishing a new
release does not require touching the links at all. Only AssemblyVersion moves, and that
is what tells Dalamud an update exists.
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

REPO = "https://github.com/Spibane/DungeonDrip"
BRANCH = "main"
ASSET = "DungeonDrip.zip"

ROOT = Path(__file__).resolve().parent.parent
PLUGIN_MANIFEST = ROOT / "DungeonDrip" / "DungeonDrip.json"
CSPROJ = ROOT / "DungeonDrip" / "DungeonDrip.csproj"
OUTPUT = ROOT / "repo.json"


def fail(message: str) -> "typing.NoReturn":  # noqa: F821
    print(f"error: {message}", file=sys.stderr)
    raise SystemExit(1)


def read_version(csproj: str) -> str:
    match = re.search(r"<Version>([^<]+)</Version>", csproj)
    if not match:
        fail("no <Version> in the csproj")
    return match.group(1).strip()


def read_api_level(csproj: str) -> int:
    """The API level is the Dalamud SDK's major version, so read it rather than hardcode it.

    Getting this wrong makes the plugin silently invisible in the installer, and a stale
    constant is exactly the kind of thing nobody notices until users report nothing showing up.
    """
    match = re.search(r'Sdk="Dalamud\.NET\.Sdk/(\d+)', csproj)
    if not match:
        fail("could not read the Dalamud SDK version from the csproj")
    return int(match.group(1))


def check_tag(tag: str, version: str) -> None:
    """Refuse to publish a tag that disagrees with the built version.

    Tagging v0.12.0 while the csproj still says 0.11.0.0 produces a release Dalamud will
    never offer as an update, because AssemblyVersion is what it compares. Cheap to catch
    here, confusing to diagnose later.
    """
    wanted = tag.lstrip("vV").split(".")
    have = version.split(".")
    if wanted[:3] != have[:3]:
        fail(f"tag {tag} does not match the csproj version {version}; bump one of them")


def main() -> None:
    manifest = json.loads(PLUGIN_MANIFEST.read_text(encoding="utf-8"))
    csproj = CSPROJ.read_text(encoding="utf-8")

    version = read_version(csproj)
    api_level = read_api_level(csproj)

    if "--check-tag" in sys.argv:
        check_tag(sys.argv[sys.argv.index("--check-tag") + 1], version)
    download = f"{REPO}/releases/latest/download/{ASSET}"

    entry = {
        "Author": manifest["Author"],
        "Name": manifest["Name"],
        "InternalName": CSPROJ.stem,
        "Punchline": manifest.get("Punchline", ""),
        "Description": manifest.get("Description", ""),
        "AssemblyVersion": version,
        "RepoUrl": manifest.get("RepoUrl", REPO),
        "ApplicableVersion": "any",
        "DalamudApiLevel": api_level,
        "IsHide": False,
        "IsTestingExclusive": False,
        "DownloadCount": 0,
        "LastUpdate": 0,
        "DownloadLinkInstall": download,
        "DownloadLinkUpdate": download,
        "DownloadLinkTesting": download,
        "IconUrl": f"https://raw.githubusercontent.com/Spibane/DungeonDrip/{BRANCH}/images/icon.png",
        "Tags": manifest.get("Tags", []),
        "CategoryTags": manifest.get("CategoryTags", []),
    }

    OUTPUT.write_text(json.dumps([entry], indent=2) + "\n", encoding="utf-8")
    print(f"wrote {OUTPUT.name}: {entry['Name']} {version}, API {api_level}")


if __name__ == "__main__":
    main()
