#!/usr/bin/env python3
"""Prints the most recent CHANGELOG section, for use as GitHub release notes.

Keeps the release body and the changelog as one source of truth rather than two things to
remember to update.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

CHANGELOG = Path(__file__).resolve().parent.parent / "CHANGELOG.md"


def main() -> None:
    if not CHANGELOG.exists():
        print("No changelog available.")
        return

    text = CHANGELOG.read_text(encoding="utf-8")
    sections = re.split(r"^### ", text, flags=re.MULTILINE)

    # sections[0] is the "# Changelog" preamble; the first real entry is the newest.
    if len(sections) < 2:
        print("No changelog entries found.")
        return

    body = sections[1].rstrip()
    print(f"### {body}")


if __name__ == "__main__":
    main()
    sys.exit(0)
