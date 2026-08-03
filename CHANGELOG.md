# Changelog

### v0.12.0 - 2026-08-03
- **Added a panel beside the market board**, marking which of the gear in the browse list you already have. Only the browse list gets one: by the time the listings for a single item are open you have chosen the piece, and every row would be the same piece once per seller. Results arrive in pages of twenty so the panel fills in as a category loads; scrolling and text searches both re-read correctly. This is where a stale dresser snapshot matters most, since you reach a board by travelling and zoning is what wipes the data
- **Added a panel beside the Glamour Dresser** listing what you are carrying that is not stored yet - the mirror of the other panels, and the one surface where the answer is something you can act on where you stand. Its markers give the location rather than ownership, since by construction you do not own these yet, and a row goes amber when something has to happen first: taking it off, or fetching it from the saddlebag. The header says how full the box is, because which of these to put in is only a question when they will not all fit. Toolbar buttons drop armoury chest gear and gear you are wearing, both of which are usually spoken for
- Rows there read "not stored" rather than "you can add this". Some of the dresser's refusals cannot be read from outside it, and it waits for the box's contents rather than its window, since a list built against an unloaded dresser would claim everything you own is unstored
- **Added gear options to the game's own right-click menus.** Right-click a piece of gear in your inventory, the armoury chest, the inspect window, a chat link, the Glamour Dresser, a vendor or the market board: **Try on outfit** for each set the piece belongs to, and **Where does this drop?**. Only what the game does not already offer is added - it has its own Try On, Link and Copy. Entries sit directly on the menu rather than under a heading, so trying an outfit on is one click, and they are marked with a D and sit below the game's own. Turn them off under Settings > In-game UI
- **Added an optional marker to the game's own item tooltips**, saying where the piece is in your collection: one of the game's own icons plus a single word, on the category row. Off by default, because it is the only thing here that changes what a game window contains rather than sitting beside it, and the only thing that hooks a game function. It appends and never replaces, and marks its own line so it cannot double up or overwrite another plugin's. If a patch moves what it hooks the line simply does not appear and Settings says so
- **Added a Collection view to the main window**, on a new toolbar switch beside the duty list. Three things no duty is involved in:
- **Sets in progress** - outfit sets you are part way through, closest to done first. Expanding one lists what is missing and where each piece drops
- **Glamour Dresser** - how full the box is and what would free space. An outfit stored a piece at a time could be one set item instead, and a piece in both the dresser and the Armoire is duplicated. The collapse advice stays conditional, because storing a set needs the outfit item and owning the pieces does not give you one
- **Already in your collection** - carried gear a box already has, read from your bags, armoury chest and saddlebag. Named for the fact rather than the conclusion, split by where the piece is because what is safe to do differs, and never listing anything you are wearing
- Added **Where does this drop?** to the game's right-click menus, listing the duties that drop a piece and taking you to whichever you pick. A piece nothing lists says exactly that rather than claiming it drops nowhere, since loot coverage for new content is thin by design
- Added `/dungeondrip <gear name>` alongside the existing duty lookup, answering where a piece is in your collection and where it comes from. Duty names still win, so nothing that worked before changed; `/dungeondrip item <name>` forces the gear reading for a piece whose name sits inside some duty's
- Added emptying the fitting room before an outfit goes into it, so the set is shown on its own rather than over whatever was left in there. Only the preview is discarded - outfits you have saved out of the room are in your Glamour Dresser and are not touched. Trying on a single piece never clears anything. Turn it off under Settings > General > Trying on
- Added try-on to the right-click menu on any gear row. **Try on** puts that one piece in the fitting room; below it, one entry per outfit set the piece belongs to, named, which fills the room with the whole set - weapon first, then top down. Pieces in no set get the single entry, and gear the fitting room will not take gets neither rather than an entry that does nothing
- Added that right-click menu to the vendor panel and the loot-roll companion, which had none, so **Link in chat** and **Copy name** work at a vendor and mid-roll as well as in the duty list
- Trying on a whole outfit turns the fitting room's Save/Delete Outfit option on first. With it off the room holds one piece at a time, so a set fed in piece by piece showed only the last one to arrive. Trying on a single piece leaves the option however you had it, and neither case turns it back off - that is what would throw away the outfit you just put together
- Changed the Vendors tab to Panels, one section per panel from one shared routine, with the marker legend shown once instead of per panel
- Changed the Glamour Dresser's size to be read rather than only its occupancy, so "used" has something to be out of. It is the box's structural size and not a per-character unlocked count, which the client does not expose, so the wording says what it means and falls back to a bare count if even that is unreadable
- Changed the three pinned panels onto one base class holding the anchoring, sizing and drag handling they had been about to have three copies of - the vendor panel drops from 554 lines to 29 - and onto one shared row cache, so a piece looked up at a vendor costs nothing when it turns up again at a board
- Changed what the right-click menus offer into one list shared by the plugin's windows and the game's own, so an option added to one cannot go missing from the other
- Fixed the vendor panel coming up oversized and overhanging the shop window on anyone whose Dalamud UI scale is not 100%. Dalamud scales a window's `Size` for you; the panel was handing it a height and width already measured in screen pixels, so the scale landed twice
- Fixed that oversizing then growing. Because the panel believed it had asked for the smaller size, the difference read as a drag on the first frame it appeared, saved itself as a custom size, and was scaled again the next time a shop opened. If your panel is already stuck large, the fourth toolbar button puts it back to matching the shop

### v0.11.1 - 2026-07-28
- Added the plugin icon, and published through a third-party plugin repository so the plugin can be installed before the official submission lands
- No changes to the plugin itself; this release exists to carry the packaging and listing work

### v0.11.0 - 2026-07-28
- Fixed the collection cache being rewritten to disk once a second. Both stores stay loaded until you zone, so from the moment you opened a Glamour Dresser every poll re-read it, counted as a change, saved the file and invalidated every derived list. Reads still happen and the timestamps still move; only an actual change now saves
- Fixed the downloaded dataset being handed from the worker thread to the framework thread without the lock that guards its companion field
- Hardened the raw vendor readers against a registry entry whose indices fall outside the block it describes - a typo there would have thrown inside a draw call rather than declining the shop
- Changed the three files keyed by territory id onto one pair of read/write helpers, so string-keyed JSON is parsed back in one place instead of three
- Changed the settings window's thirteen checkboxes onto one shared toggle that takes its own tooltip, removing the repeated hover plumbing
- Removed a dead ordering constant left over from the combined role headings dropped in 0.9.2
- Changed the main window's toolbar from five text buttons to icons, matching the vendor panel. The words set the window's minimum useful width at roughly twice what the list under them needs; they now live in the hover tooltips, which also read on a greyed-out button so it can still say why it is greyed out
- Changed both toolbars onto one shared button, so the two cannot drift apart
- Added roulette advice to the window when you are not in a duty: for Expert, Level Cap Dungeons, Alliance Raids, High-level Dungeons and Leveling, the role to queue as for the best odds of uncollected glamour, with the count and the share of what that role can roll on. Counted by the same role headings the missing list uses, melee still split by gear type, so "Melee DPS (NIN VPR)" names what to queue rather than lumping pools that share nothing. Hovering gives every role and how much of the roulette's pool has loot data. Ranked by share rather than raw count, so a role handed less gear is not penalised for it. Trials, Main Scenario, Guildhests, Normal Raids, Mentor and Frontline are absent - the plugin carries no loot for them
- Changed the button that stops following a duty you looked up to read **Roulettes** when letting go would land you back on that table, which standing in a city it does. It is the same button doing the same thing, named for where it goes
- Added level gating to that advice: roles you have nothing levelled enough for, and duties above your level, are left out, since a roulette will not queue you into them. Duty unlocks are not readable, so the odds still assume an unlocked pool
- Changed roulette membership to be read from each duty's own roulette flags rather than reconstructed from level bands, and the roulette names from the game's sheet, so renamed and re-pooled roulettes follow the patch
- Changed the job index to record the jobs behind each role heading, resolving a job's parent class on the way, so gear that old sheet rows mark for the class alone ("GLA PGL MRD LNC ARC ROG MNK WAR DRG BRD NIN" is a real row) still counts Paladin as a tank. Low-level dungeon gear uses those rows heavily

### v0.10.0 - 2026-07-28
- **A new panel now appears beside vendor windows by default.** Turn it off under Settings > Vendors if you would rather it did not
- Added vendor collection markers. Open a shop and a panel rides beside it listing the glamour gear that vendor stocks, each piece marked by where you already have it: a red x for not collected, a tick for the Glamour Dresser, layers for a stored outfit set with gaps, a green star for an outfit you have completed, a box for the Armoire, a case for carried or equipped. Anything that cannot be kept as a glamour is left out entirely, so an unmarked shop row never reads as "you already have this"
- Covers gil vendors and Calamity Salvagers, item-exchange counters, currency and scrip exchanges, Grand Company quartermasters, free-item counters and the Firmament. Anything else gets no panel rather than a wrong one
- The panel opens at the shop window's height with the list scrolling inside, and its width fits the longest item name. Drag it to any size and that size is remembered; a fourth button appears while a custom size is in use, to go back to matching the shop
- Three buttons at the top of the panel flip the list filters without a trip to Settings: whether owned pieces are listed, whether the list is held to your current job, and whether weapons are included
- The panel degrades rather than refusing when the dresser snapshot is old: a stale snapshot turns the "not collected" marker amber and leaves the "you have this" markers alone. Dresser contents are near-monotonic, so an old snapshot's positives hold up far better than its negatives
- Like the loot-roll companion, the panel reads the shop window but never draws into it, so it cannot collide with plugins that write to those nodes
- Added `/dungeondrip shop`, which names the open vendor window so an uncovered one can be reported
- Changed the settings window from one General tab into General, Duties and Vendors, so each setting sits under the surface it affects. General holds the rules every gear list obeys, and both other tabs say so
- Changed "list owned pieces", "only gear my job can wear" and "skip weapons" to govern the vendor panel as well as the duty list, with one shared job check rather than two that could disagree
- Changed the loot-roll companion to be collapsible. It is pinned where you cannot drag it, so folding it to the title bar is the only way to get it out of the way without switching the feature off
- Changed the plugin's framing to match what it does. The collection is the engine and duty loot is one question asked of it; vendors are the third place that question gets answered, after the duty list and the loot-roll companion
- Changed four copies of the window colour constants into one palette, three copies of the item icon-and-name row into one helper, and the two pinned panels' duplicate placement maths into one. The two greys that had drifted a shade apart doing the same job are now a single colour
- Removed a record that had lost both its fields to disuse, four empty disposers and their call sites, and two members nothing read

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
