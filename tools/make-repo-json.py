#!/usr/bin/env python3
"""Generates repo.json, the store manifest for the third-party plugin repository.

Every field is derived from something that already exists - the plugin manifest, the
csproj version, and the Dalamud SDK version - so the store entry cannot drift out of step
with what was actually built. Run by the release workflow; safe to run by hand.

Download links are pinned to the release tag rather than pointing at releases/latest. That
matters because this file is committed before the release is published: with "latest" the
manifest would advertise the new version while the link still served the previous release's
zip, so anyone refreshing in that window installed the old plugin under the new version
number. A pinned link simply is not there yet, which fails visibly instead.

Nothing here reads the build output - not the zip, not its size, not a hash - which is why
this file belongs in the release commit rather than being written back to main afterwards.
Run with --check, the release workflow verifies the committed file instead of rewriting it,
so main needs no push from CI and can require pull requests like any other branch.
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

    # The csproj carries four parts and the tag three, so the tag is derived rather than read
    # from the environment - this has to produce the same manifest run by hand as it does in CI.
    tag = "v" + ".".join(version.split(".")[:3])
    download = f"{REPO}/releases/download/{tag}/{ASSET}"

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

    rendered = json.dumps([entry], indent=2) + "\n"

    if "--check" in sys.argv:
        check(rendered, version, api_level)
        return

    OUTPUT.write_text(rendered, encoding="utf-8")
    print(f"wrote {OUTPUT.name}: {entry['Name']} {version}, API {api_level}")


def check(rendered: str, version: str, api_level: int) -> None:
    """Verifies the committed manifest is what this script would write, without writing it.

    The point of generating repo.json was that the store entry cannot drift from what was
    built. Comparing gives the same guarantee without CI needing to push to main - which is
    what lets main require pull requests. The failure names the command that fixes it,
    because the usual cause is a version bump that forgot this file.
    """
    if not OUTPUT.exists():
        fail(f"{OUTPUT.name} is missing; run tools/make-repo-json.py and commit it")

    if OUTPUT.read_text(encoding="utf-8") == rendered:
        print(f"{OUTPUT.name} is current: {version}, API {api_level}")
        return

    fail(
        f"{OUTPUT.name} does not match the csproj ({version}, API {api_level}). "
        "Run tools/make-repo-json.py and commit the result."
    )


if __name__ == "__main__":
    main()
