# Changelog

### v0.10.0 - 2026-07-28
- **A new panel now appears beside vendor windows by default.** Turn it off under Settings > General > Vendors if you would rather it did not
- Added vendor collection markers. Open a shop and the panel lists the glamour gear it stocks, marked as not collected, in the Glamour Dresser, part of a stored outfit set, in the Armoire, or carried. Anything that cannot be kept as a glamour is left out entirely, so an unmarked shop row never reads as "you already have this". Covers gil vendors and Calamity Salvagers, item-exchange counters, currency and scrip exchanges, Grand Company quartermasters, free-item counters and the Firmament
- Added `/dungeondrip shop`, which names the open vendor window so an uncovered one can be reported
- Added three buttons at the top of the vendor panel for the list filters: whether owned pieces are listed, whether the list is held to your current job, and whether weapons are included. They write the shared settings, so the panel says that the duty list changes with them
- Changed the not-collected marker to a red x with the item named in plain white, and greyed the names of everything you already own. Gold still read as a reward, which was the same problem the star had
- Added a completed-outfit marker. A piece held in an outfit set whose every slot is filled now reads "Outfit completed" and takes the star, in green. The star used to mark what you were missing, which read backwards - a star is a reward everywhere else in the game. Not-collected is now an x
- Changed the settings window from one General tab into General, Duties and Vendors, so each setting sits under the surface it affects. General holds the rules every gear list obeys and both other tabs say so
- Changed "list owned pieces", "only gear my job can wear" and "skip weapons" to govern the vendor panel as well as the duty list. The vendor panel's own duplicate of the first is gone, and the job check is now one shared implementation rather than two that could disagree
- Changed the loot-roll and vendor panels to be collapsible. They are pinned where you cannot drag them, so folding them to the title bar is the only way to get them out of the way without switching the feature off
- Changed the plugin's framing to match what it does. The collection is the engine and duty loot is one question asked of it; vendors are the third place that question gets answered, after the duty list and the loot-roll companion
- The vendor panel degrades rather than refusing when the dresser snapshot is old: a stale snapshot turns the "not collected" marker amber and leaves the "you have this" markers alone. Dresser contents are near-monotonic, so an old snapshot's positives hold up far better than its negatives
- Like the loot-roll companion, the panel reads the shop window but never draws into it, so it cannot collide with plugins that write to those nodes

### v0.9.5 - 2026-07-28
- Added the GNU AGPL v3.0-or-later licence, with the SPDX expression and copyright recorded in the project file
- Added AI-DECLARATION.md (spec 0.1.2) declaring how much of this codebase was AI-authored, what the human decided and tested, and the caveats that follow

### v0.9.4 - 2026-07-28
- Fixed downloads being read without a size limit. Content-Length is advisory and a chunked response can stream indefinitely, so every response now goes through a hard byte ceiling enforced while reading, not just on the declared length
- Fixed cache writes being able to truncate a good file if the game died mid-write; they now go to a temporary file and are moved into place
- Fixed the cancellation source being disposed while a request could still be unwinding on it during plugin unload
- Changed the two HTTP clients into one, so the identifying User-Agent and the safety limits are stated once
- Changed the four copies of JSON load/save boilerplate into a single store, and the two age formatters into one
- Changed raw item ids to resolve through Dalamud's `ItemUtil.GetBaseId` instead of hand-rolled offset arithmetic, and to reject event items by kind. Event items sit behind an offset that shares the id space with real gear, so stripping it blindly maps them onto unrelated equipment
- Removed dead code: the unread LootDataState/State and LastCheckedUtc, ContentFinderIndex.IsDuty, JobRoleIndex.NameOf (unreachable since role headings replaced combined ones), DungeonLootData's unread counters, CommandRegistration.Primary, and two never-read DutyEntry fields

### v0.9.3 - 2026-07-27
- Changed role view to also show "of Slaying" accessories under the narrower melee headings they serve, so they appear under Maiming and Striking as well as their own heading. Role view answers "what can I claim", so it duplicates on purpose; slot view still never duplicates and the headline count still counts each piece once
- Changed the list and the most-missing callout to share one bucketing routine, so the two can no longer disagree

### v0.9.2 - 2026-07-27
- Changed gear shared between roles to appear under each role that can roll on it instead of a combined heading of its own. "of Aiming" accessories now sit under both Physical Ranged and Melee DPS (NIN VPR), so a scouting player sees them in their own pile; this replaces the combined "Physical Ranged / Melee DPS" heading added in 0.9.1
- Changed the "most missing" callout to count shared pieces under each applicable role, noting on hover that it does so

### v0.9.1 - 2026-07-27
- Changed shared role headings to name the primary owner first, so "of Aiming" reads as "Physical Ranged / Melee DPS" rather than implying the pieces are melee gear

### v0.9.0 - 2026-07-27
- Added a line above the list naming whichever role still needs the most from this duty, with the full per-role breakdown on hover. Counts by role even when the list is grouped by slot, reports ties as ties, and can be turned off in Settings > General

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
