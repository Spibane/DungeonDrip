<h1 align="center">👗 Glamour Assistant</h1>

<h2 align="center"><strong>Shows which dungeon glamour pieces are still missing from your Glamour Dresser and Armoire</strong></h2>

<div align="center">

[![Version](https://img.shields.io/badge/version-0.5.0-black)](./CHANGELOG.md)
[![Status](https://img.shields.io/badge/status-Alpha-orange)](./CHANGELOG.md)
[![Changelog](https://img.shields.io/badge/changelog-blue)](./CHANGELOG.md)

[![C#](https://img.shields.io/badge/C%23-14-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![.NET](https://img.shields.io/badge/.NET-10.x-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Dalamud](https://img.shields.io/badge/Dalamud-API%2015-2F5BB6)](https://dalamud.dev/)

</div>

## Architecture

Three independent pipelines meet at the ownership check: a **downloaded** loot dataset, the
**territory** you are looking at, and a **cached** picture of your collection.

```
  upstream                              plugin load, then on demand
  ┌────────────────────────┐            ┌────────────────────────────────────┐
  │ FFXIV Teamcraft        │◀── 304 ────│ LootDataService                    │
  │  instance-sources.json │            │  ETag revalidate                   │
  │  instances.json        │─── 200 ───▶│  invert + join → cache to disk     │
  └────────────────────────┘            └─────────────────┬──────────────────┘
                                                          │ map id
                                                          ▼  (Lumina Map sheet)
  ┌────────────────────────┐            ┌────────────────────────────────────┐
  │ IClientState           │            │ DungeonLootData                    │
  │  TerritoryChanged      │───────────▶│  territory → glamour-able item ids │
  │ duty picker / command  │  territory └─────────────────┬──────────────────┘
  └────────────────────────┘                              │
                                                          ▼
  ┌────────────────────────┐            ┌────────────────────────────────────┐
  │ MirageManager          │───────────▶│ OwnershipTracker                   │
  │  prism box + outfit    │            │  per-character JSON snapshot       │
  │ UIState.Cabinet        │───────────▶│  + staleness                       │
  │ InventoryManager       │  (opt-in)  └─────────────────┬──────────────────┘
  └────────────────────────┘                              │
                                                          ▼
                              MissingItems.Resolve → DutyReport → ImGui window
```

Two constraints shape everything above:

**The game ships no loot tables.** Drop lists are server-side and no Excel sheet maps a duty to its
drops, so the dataset is downloaded. Because the upstream numbers instances its own way — its
instance ids are *not* `ContentFinderCondition` ids — the dataset is keyed on **map id** and
translated to a territory through Lumina at runtime. A cached dataset therefore stays valid across
patches that renumber duties.

**Your collection is not readable when you need it.** `MirageManager` clears the Glamour Dresser on
every zone change, and `UIState.Cabinet` only loads the Armoire when you open it. Standing in a
dungeon there is nothing live to read, so the plugin snapshots both whenever the client happens to
have them loaded, persists that per character, and reports how old the snapshot is.

## Features

- Detects the duty you zone into and lists only the glamour-able gear you do not yet have, closing
  again when you leave.
- Covers **dungeons and alliance raids only** — the content whose gear people actually farm.
- Looks up **any** duty without entering it, from a searchable list or `/glamassist <duty name>`.
- Counts a piece as owned when it is in the dresser directly, inside a stored **outfit set**, or in
  the Armoire. Bags, armoury chest, equipped gear and saddlebags are opt-in.
- Handles partially-filled outfit sets by asking the client which slots are populated
  (`MirageManager.IsSetSlotUnlocked`) rather than trusting the sheet.
- Refreshes the loot dataset on every plugin load, revalidated with `If-None-Match` so unchanged
  data costs two `304`s.
- Falls back to the on-disk dataset when offline, and says so in the window.
- Lists only pieces that can genuinely be **kept**, dropping the materia, orchestrion rolls, cards
  and coffers that share the same drop lists.
- **Learns from what you see drop**, including rolls other party members win, and merges it into the
  duty's list.
- **Fills gaps from the Console Games Wiki**, which documents new dungeons the primary dataset has
  barely touched — the Clyteum goes from 1 listed drop to a full table.
- **Marks unowned items beside the loot roll window** in a companion window that never touches the
  game's own UI nodes, so it cannot fight plugins that do.
- **Groups by equipment slot or by who can roll Need**, with collapsible headings that stay
  collapsed between sessions.

## Commands

| Command | What it does |
| --- | --- |
| `/glamassist` | Toggle the window (`/gla` also works) |
| `/glamassist <duty name>` | Pin the window to that duty; ambiguous names open the picker |
| `/glamassist config` | Settings |
| `/glamassist refresh` | Re-read the dresser and armoire now |
| `/glamassist update` | Force a re-download of the loot dataset |

## Configuration

All settings live in the in-game window (`/glamassist config`).

Settings are split across two tabs: **General** for what the window shows and how ownership is
judged, **Data** for the collection snapshot and the loot sources behind it.

| Setting | Tab | Default | Notes |
| --- | --- | --- | --- |
| Open automatically when I enter a duty | General | on | Pops the window on zoning in |
| ...but not when I already have everything | General | on | Suppresses the pop-up at 0 missing |
| Close again when I leave the duty | General | on | A duty you pinned yourself stays open |
| List pieces I already have, greyed out | General | off | |
| Only show gear my current job can wear | General | off | Re-evaluated when you switch job |
| Skip weapons | General | off | Main hands and off-hands, which drop together |
| Group the list by | General | Slot | Equipment slot, or role that can roll Need |
| Compare against | General | Both | Dresser, Armoire, or both |
| Count bags / armoury / equipped / saddlebags | General | off | A drop in your bag is not collected yet |
| Outfit-set ownership | General | Any | See below |
| Companion list beside the loot window | General | on | Side: Auto / Left / Right |
| Warn when dresser data is older than | Data | 7 days | |
| Record gear that drops in duties | Data | on | See below |
| Fill gaps from the wiki | Data | on | See below |

### Grouping and collapsing

Headings collapse with a click and stay collapsed between sessions. Two groupings:

- **Equipment slot** — head, body, hands and so on.
- **Role that can roll Need** — Tank, Healer, Melee DPS, Physical Ranged, Magical Ranged, for
  claiming during a run. The toolbar button switches between them without opening settings.

Roles come from the jobs that can equip each piece. `ClassJob.Role` alone lumps Bard in with Black
Mage, so `PrimaryStat` splits physical ranged (DEX) from casters (INT) — no hardcoded job lists.
Pieces that genuinely span roles say so: "of Aiming" accessories show as *Melee DPS / Physical
Ranged*, because NIN and VPR really can roll on them alongside BRD, MCH and DNC.

### What counts as owned, and what can be kept at all

**Compare against** picks the storage that decides it: the Glamour Dresser, the Armoire, or both.
The list then only shows pieces that store can actually hold.

The two stores **overlap** — most Armoire items can live in either place, so this is not an
either/or. Everything wearable goes in the Dresser; the `Cabinet` sheet is the authoritative list of
the subset the Armoire also accepts.

Which items the Armoire accepts is read in full from the game's `Cabinet` sheet every time the
plugin loads. **Nothing is hardcoded** — not a level, not an expansion, not a set name — so when
Square Enix adds gear to the Armoire, a game patch is all it takes and the plugin follows on the
next start. Eligibility is never written to disk either: the caches hold raw item ids and are
re-judged against the current sheet on load, so existing data reclassifies itself too.

As a snapshot of how that looked when this was written — descriptive, not a rule — every Dawntrail
dungeon set was in the Armoire and no Endwalker or earlier one was, putting the line between equip
Lv90 and Lv91. The Lv89–90 entries that did exist were job artifact gear and crafter tool sets
rather than dungeon drops. Expect that to move.

One known gap: relic weapons cannot go in the Dresser and no sheet column marks them. They are not
dungeon drops, so it does not affect what is listed here.

### Which duties are covered

Dungeons and alliance raids only — 103 and 18 respectively at the time of writing. Trials, 8-player
raids, ultimates, guildhests, deep dungeons and everything else are excluded, which is 508 of the
629 duty territories the game defines.

Alliance raids share `ContentType` 5 with 8-player raids, so the party layout tells them apart:
`ContentMemberType` 4 is the 24-player alliance, 3 is a full party.

### Outfit-set ownership

A dresser slot can hold a whole outfit set. Its component pieces count as owned — but a single piece
can belong to several sets, so there are two readings:

- **Any** (default) — owned as soon as one stored outfit contains it.
- **All** — owned only once every outfit set listing that piece is stored, with that slot filled.

### Filling gaps from the Console Games Wiki

The primary dataset lags badly on brand-new dungeons — Mistwake listed 2 drops and the Clyteum 1,
against 60–80 for older content, and Garland Tools returns identical counts. The
[FFXIV Console Games Wiki](https://ffxiv.consolegameswiki.com) documents both in full.

So when you view a duty, the plugin looks up that one page and merges its loot table in, marking
those entries *(wiki)*. It reads item **names** out of the page's `Drops table row` templates and
resolves them against the game's own Item sheet, so a name the game does not know is counted and
skipped rather than guessed at.

It is strictly additive and hedged at every step:

- One page per duty, on demand — never a bulk crawl.
- Cached 14 days; a re-check compares the page revision before re-parsing.
- Misses cached 3 days, so an undocumented duty is not re-fetched constantly.
- One request at a time, minimum 2s apart, 20s timeout, response size capped.
- Any failure leaves the primary dataset untouched and is recorded so it backs off.
- Redirects are followed — `ContentFinderCondition` spells "Toto–Rak" with an en dash while the
  article uses a hyphen, and MediaWiki's parse endpoint does not follow redirects unless asked.

Not every page is templated the same way; a few (Alzadaal's Legacy, for one) yield nothing, in which
case the primary dataset still applies. Turn it off or clear the cache in settings.

### Learning from observed drops

The wiki covers documented content; this covers everything else, including a dungeon released
yesterday. The plugin watches loot chat while you are in a duty and records any glamour-able gear it sees,
including rolls won by other party members. Item ids come straight out of the chat message's item
link, so there is no text parsing and no localisation problem. New sightings merge into that duty's
list immediately, and are marked *(seen here)* so you can tell them from downloaded data.

Nothing is uploaded anywhere. Sightings go to `learned-loot.json` in the **same format as
`loot-overrides.json`**, so an entry can be promoted to a hand-maintained override, or contributed
upstream, by copying it across. Turn it off or wipe the record in settings.

### Loot overrides

To patch a duty by hand, drop a `loot-overrides.json` into the plugin config folder
(**Settings → Open config folder**) and reload. It is additive, keyed by territory id:

```json
{
  "1252": [45123, 45124, 45125]
}
```

### Files written

All live in the Dalamud plugin config folder:

| File | Contents |
| --- | --- |
| `dungeon-loot-cache.json` | Downloaded dataset plus the upstream ETags |
| `wiki-loot-cache.json` | Per-duty wiki lookups, with page titles and revisions |
| `learned-loot.json` | Drops this client has watched fall, per territory |
| `ownership-<contentId>.json` | Per-character dresser/armoire snapshot and its timestamps |

## Package Structure

```
GlamourAssistant/
├── Plugin.cs                    # services, territory tracking, report cache, commands
├── Configuration.cs
├── Data/
│   ├── LootDataService.cs       # download, ETag revalidation, transform, disk cache
│   ├── DungeonLootData.cs       # map → territory, gear filter, source merge + provenance
│   ├── WikiLootSource.cs        # per-duty wiki lookup, parse, cache, backoff
│   ├── LearnedLootStore.cs      # drops observed first-hand, persisted
│   └── LootModels.cs            # cache + upstream DTOs
├── Game/
│   ├── DresserReader.cs         # prism box + outfit-set expansion
│   ├── ArmoireReader.cs         # Cabinet sheet reverse map
│   ├── InventoryReader.cs       # bags / armoury / equipped / saddlebags
│   ├── LootObserver.cs          # records gear seen dropping in a duty
│   ├── OutfitCatalog.cs         # MirageStoreSetItem membership + slot order
│   ├── OwnershipTracker.cs      # per-character snapshot, staleness, persistence
│   └── ItemId.cs                # HQ / collectable offset normalisation
├── Core/
│   ├── MissingItems.cs          # the ownership decision, Dalamud-free
│   ├── DutyReport.cs            # territory + ownership → the drawn list
│   ├── DutyCatalog.cs           # duty list for lookup, ordered by level
│   ├── ContentFinderIndex.cs    # territory → duty name; "am I in a duty"
│   ├── ItemNameIndex.cs         # item name → row id, for wiki name resolution
│   └── EquipSlots.cs
└── Windows/
    ├── MissingItemsWindow.cs    # picker, pin, freshness banner, item list
    ├── LootCompanionWindow.cs   # read-only list pinned beside the Need/Greed window
    └── ConfigWindow.cs
```

## Building

Requires the .NET 10 SDK (10.0.101 or newer) and the Dalamud dev assemblies. The target framework is
`net10.0-windows`, but it compiles fine on Linux and macOS — nothing Windows-only is referenced.

The SDK auto-detects the assemblies from the local XIVLauncher install, so on a machine that has run
the game with Dalamud once, nothing extra is needed:

| Host | Auto-detected path |
| --- | --- |
| Windows (XIVLauncher) | `%AppData%\XIVLauncher\addon\Hooks\dev\` |
| Linux (XIVLauncher.Core) | `~/.xlcore/dalamud/Hooks/dev/` |
| macOS (XIV on Mac) | `~/Library/Application Support/XIV on Mac/dalamud/Hooks/dev/` |

```bash
dotnet build GlamourAssistant.sln -c Release
```

With no XIVLauncher present — CI, or compile-checking on a dev machine — extract
<https://goatcorp.github.io/dalamud-distrib/latest.zip> and point `DALAMUD_HOME` at it, which
overrides all of the above:

```bash
DALAMUD_HOME=/path/to/dalamud dotnet build GlamourAssistant.sln -c Release
```

### Output paths

The solution sets `Platform=x64`, so a **solution** build and a **project** build land in different
places:

| Build command | Plugin DLL |
| --- | --- |
| `dotnet build GlamourAssistant.sln -c Release` | `GlamourAssistant\bin\x64\Release\GlamourAssistant.dll` |
| `dotnet build GlamourAssistant\GlamourAssistant.csproj -c Release` | `GlamourAssistant\bin\Release\GlamourAssistant.dll` |

The `...\Release\GlamourAssistant\latest.zip` beside it is DalamudPackager's *distributable* — that
folder holds only the zip and the manifest, so it is not what you load for testing.

### Loading it in-game — Windows

1. `/xlsettings` → **Experimental** → add the **full path to `GlamourAssistant.dll`** (the file, not
   its folder) under Dev Plugin Locations. This is only needed once.
2. `/xlplugins` → **Dev Tools → Installed Dev Plugins** → enable **Glamour Assistant**.
3. After a rebuild, reload it from that same Dev Plugins list — no game restart needed.

### Loading it in-game — Linux (XIVLauncher.Core)

Dalamud runs inside the Wine prefix, so a path typed into Dev Plugin Locations has to be
Wine-visible (`Z:\home\you\...`, since Wine maps `/` to `Z:`). Avoid that entirely by using the
`devPlugins` folder, which Dalamud scans automatically:

```bash
mkdir -p ~/.xlcore/devPlugins/GlamourAssistant && cp GlamourAssistant/bin/x64/Release/GlamourAssistant.{dll,json,deps.json} ~/.xlcore/devPlugins/GlamourAssistant/
```

Then `/xlplugins` → **Dev Tools → Installed Dev Plugins** → enable **Glamour Assistant**. Re-run the
copy after each rebuild and hit reload there.

The runtime files are plain text and directly inspectable, which is the easiest way to confirm the
data pipeline while testing:

```bash
ls -la ~/.xlcore/pluginConfigs/GlamourAssistant/
```

Note that **Open config folder** in settings generally fails under Wine — there is no shell handler
to hand the folder to. The window shows the path as text with a **Copy path** button, and the failure
also prints the path to chat.

## CI/CD

`.github/workflows/build.yml` runs on push to `main`, on pull requests, and manually. It builds on
`windows-latest`, fetching the Dalamud dev distribution and exporting `DALAMUD_HOME` so no game
install is needed, then uploads the packaged plugin folder as an artifact.

## The loot roll window

While a Need/Greed roll is up, a small companion window rides beside it marking which pieces you do
not own.

It is a **separate window**, not a recolouring of the game's loot list. Several popular plugins
(Allagan Tools, Simple Tweaks, VanillaPlus, Collections) already write to game addon nodes, and two
plugins setting the same node's colour is a genuine conflict — whoever writes last in a frame wins,
and a refresh can revert either. This reads the addon's item list and screen position and writes
nothing into it, so there is nothing to collide with, and it survives the addon's node layout
changing in a patch.

Because a roll is time-critical and you cannot go and check, a stale or missing dresser snapshot
suppresses the verdict and says so rather than showing a confident guess.

## Not implemented

- Retainer inventories — the client cannot read them unless you are at a retainer.
- Per-boss attribution of drops.
- Recolouring the game's own loot window (deliberately avoided — see below).
