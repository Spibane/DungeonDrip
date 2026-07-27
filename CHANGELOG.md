# Changelog

### v0.1.0 - 2026-07-26
- Initial release
- Added automatic detection of the duty you zone into, listing the glamour-able gear that drops there and is not yet in your Glamour Dresser or Armoire
- Added duty lookup without entering: a searchable picker plus `/glamassist <duty name>`, with a pinned selection that survives zoning
- Added outfit-set awareness — pieces inside a stored set count as owned, with an Any/All toggle for pieces belonging to several sets, and partially-filled sets resolved via `MirageManager.IsSetSlotUnlocked`
- Added a per-character collection snapshot persisted to disk, because the client clears Glamour Dresser data on every zone change and only loads the Armoire on demand; the window reports how stale the snapshot is
- Added an opt-in toggle to count bags, armoury chest, equipped gear and saddlebags as owning a piece
- Added an optional current-job filter, re-evaluated when you switch job
- Added the dungeon loot dataset as a download refreshed on every plugin load, revalidated with `If-None-Match` and cached to disk for offline use; `/glamassist update` forces a re-download
- Added `loot-overrides.json` for hand-patching duties the upstream dataset has not caught up with
- Added a `windows-latest` CI build that fetches the Dalamud dev distribution, so builds need no game install

**Known limitations:**
Upstream loot data is thin for the newest dungeons (2 recorded drops for Mistwake, 1 for the Clyteum, against 60–80 for older content). Retainer inventories are never counted. Not yet verified in-game.
