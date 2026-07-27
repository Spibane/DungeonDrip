# Changelog

### v0.3.0 - 2026-07-27
- Added the FFXIV Console Games Wiki as a supplementary loot source, looked up one duty at a time when you view it and merged into that duty's list — the Clyteum goes from 1 listed drop to a full table, Mistwake from 2
- Added provenance markers so each entry shows whether it came from the downloaded dataset, the wiki, your overrides, or your own sightings
- Added a companion window pinned beside the Need/Greed roll window marking pieces you do not own; it reads the loot addon and writes nothing into it, so it cannot conflict with plugins that recolour game UI nodes
- Added wiki settings: enable/disable, per-duty status, re-fetch, and clear cache
- Changed the duty picker to sort by highest level first
- Fixed the wiki lookup returning nothing for duties whose article is a redirect (MediaWiki's parse endpoint does not follow redirects unless asked, and `ContentFinderCondition` spells "Toto–Rak" with an en dash while the article uses a hyphen)

### v0.2.0 - 2026-07-27
- Added learning from observed drops: gear seen dropping in a duty, including rolls won by other party members, is recorded and merged into that duty's list, marked *(seen here)* to distinguish it from downloaded data
- Added `learned-loot.json`, written in the same format as `loot-overrides.json` so sightings can be promoted to hand-maintained overrides or contributed upstream; nothing is uploaded
- Added a setting to disable drop learning and to wipe what has been recorded
- Changed the duty picker to sort by level rather than by expansion; duties with no duty-finder entry sort last
- Fixed the item row binding its tooltip and right-click menu to the wrong element

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
