# Changelog

### v0.8.0 - 2026-07-27
- **BREAKING:** Renamed the plugin from Glamour Assistant to Dungeon Drip. The old name sat in a crowded shelf alongside Glamourer, Glamaholic and the existing Glamour Log plugin
- **BREAKING:** Commands are now `/dungeondrip`, with `/drip` and `/ddrip` as aliases, replacing `/glamassist` and `/gla`
- Added collision-safe command registration: aliases another plugin already owns are skipped rather than failing silently, and Settings > General lists which ones were claimed
- Added a one-time migration of settings and caches from the old plugin name, so the dresser snapshot, learned drops and wiki lookups survive the rename

### v0.7.1 - 2026-07-27
- Added a toolbar button to show or hide pieces you already have, without opening settings
- Moved "refresh collection" out of the toolbar into Settings > Data, beside the snapshot timestamps it acts on

### v0.7.0 - 2026-07-27
- Added outfit-set membership to the item tooltip: which sets a piece belongs to, and for each whether it is stored with the piece included, stored with that slot empty, or not stored at all
- Added tracking of which outfit sets are in the dresser regardless of slot contents, so the "stored but this slot is empty" case can be distinguished; caches written before this reconstruct it on load

### v0.6.0 - 2026-07-27
- Changed role grouping to split melee into its actual gear types instead of one bucket: Maiming (DRG RPR), Striking (MNK SAM), Scouting (NIN VPR) and the shared Slaying accessories. The split comes from the job set the game lists on each item, so a new job lands in the right heading with no code change; tanks and healers stay whole because their armour and accessories really do share a category
- Changed melee headings to drop base classes, reading "DRG RPR" rather than "LNC DRG RPR"
- Removed the inline provenance markers from the item list; that detail is already in the hover tooltip

### v0.5.0 - 2026-07-27
- Added an option, on by default, to close the window again when you leave a duty; a duty you pinned yourself stays open
- **BREAKING:** Restricted coverage to dungeons and alliance raids (103 and 18 duties). Trials, 8-player raids, ultimates, guildhests and deep dungeons are no longer tracked, and drops are no longer learned in them
- Changed the list to only include pieces that can actually be stored, so "compare against" now also decides what is eligible to appear. The Dresser and Armoire overlap rather than being alternatives: Dawntrail dungeon sets are accepted by both, older sets are Dresser-only
- Changed the "skip weapons" toggle to cover off-hands as well, since they drop alongside main hands
- Changed the wiki parser to stop reading game sheets off the framework thread; storability is decided during the merge instead

### v0.4.0 - 2026-07-27
- Added collapsible group headings to the missing list, remembered between sessions, with a missing count per group
- Added grouping by the role allowed to roll Need — Tank, Healer, Melee DPS, Physical Ranged, Magical Ranged — for claiming during a run, switchable from the window toolbar; pieces spanning roles are labelled as such rather than forced into one
- Added a setting to skip weapons (main hand only; shields are still listed)
- Added a choice of what ownership is compared against: Glamour Dresser, Armoire, or both
- Changed settings into General and Data tabs, splitting what the window shows from the collection snapshot and loot sources

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
- Added duty lookup without entering: a searchable picker plus `/dungeondrip <duty name>`, with a pinned selection that survives zoning
- Added outfit-set awareness — pieces inside a stored set count as owned, with an Any/All toggle for pieces belonging to several sets, and partially-filled sets resolved via `MirageManager.IsSetSlotUnlocked`
- Added a per-character collection snapshot persisted to disk, because the client clears Glamour Dresser data on every zone change and only loads the Armoire on demand; the window reports how stale the snapshot is
- Added an opt-in toggle to count bags, armoury chest, equipped gear and saddlebags as owning a piece
- Added an optional current-job filter, re-evaluated when you switch job
- Added the dungeon loot dataset as a download refreshed on every plugin load, revalidated with `If-None-Match` and cached to disk for offline use; `/dungeondrip update` forces a re-download
- Added `loot-overrides.json` for hand-patching duties the upstream dataset has not caught up with
- Added a `windows-latest` CI build that fetches the Dalamud dev distribution, so builds need no game install

**Known limitations:**
Upstream loot data is thin for the newest dungeons (2 recorded drops for Mistwake, 1 for the Clyteum, against 60–80 for older content). Retainer inventories are never counted. Not yet verified in-game.
