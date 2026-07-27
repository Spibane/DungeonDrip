<h1 align="center">👗 Glamour Assistant</h1>

<h2 align="center"><strong>Shows which dungeon glamour pieces are still missing from your Glamour Dresser and Armoire</strong></h2>

<div align="center">

[![Version](https://img.shields.io/badge/version-0.1.0-black)](./CHANGELOG.md)
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

- Detects the duty you zone into and lists only the glamour-able gear you do not yet have.
- Looks up **any** duty without entering it, from a searchable list or `/glamassist <duty name>`.
- Counts a piece as owned when it is in the dresser directly, inside a stored **outfit set**, or in
  the Armoire. Bags, armoury chest, equipped gear and saddlebags are opt-in.
- Handles partially-filled outfit sets by asking the client which slots are populated
  (`MirageManager.IsSetSlotUnlocked`) rather than trusting the sheet.
- Refreshes the loot dataset on every plugin load, revalidated with `If-None-Match` so unchanged
  data costs two `304`s.
- Falls back to the on-disk dataset when offline, and says so in the window.
- Filters loot lists down to equippable, non-soul-crystal items, dropping the orchestrion rolls,
  Triple Triad cards, materials and job coffers that share the same drop lists.

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

| Setting | Default | Notes |
| --- | --- | --- |
| Open automatically when I enter a duty | on | Pops the window on zoning in |
| ...but not when I already have everything | on | Suppresses the pop-up at 0 missing |
| List pieces I already have, greyed out | off | |
| Only show gear my current job can wear | off | Re-evaluated when you switch job |
| Count bags / armoury / equipped / saddlebags | off | A drop in your bag is not collected yet |
| Outfit-set ownership | Any | See below |
| Warn when dresser data is older than | 7 days | |

### Outfit-set ownership

A dresser slot can hold a whole outfit set. Its component pieces count as owned — but a single piece
can belong to several sets, so there are two readings:

- **Any** (default) — owned as soon as one stored outfit contains it.
- **All** — owned only once every outfit set listing that piece is stored, with that slot filled.

### Loot overrides

Upstream lags on brand-new dungeons. At the time of writing the newest few had a handful of drops
recorded (Mistwake: 2, the Clyteum: 1) against 60–80 for older ones; Garland Tools returns the same
counts, so there is no better free source.

To patch a duty yourself, drop a `loot-overrides.json` into the plugin config folder
(**Settings → Open config folder**) and reload. It is additive, keyed by territory id:

```json
{
  "1252": [45123, 45124, 45125]
}
```

### Files written

Both live in the Dalamud plugin config folder:

| File | Contents |
| --- | --- |
| `dungeon-loot-cache.json` | Downloaded dataset plus the upstream ETags |
| `ownership-<contentId>.json` | Per-character dresser/armoire snapshot and its timestamps |

## Package Structure

```
GlamourAssistant/
├── Plugin.cs                    # services, territory tracking, report cache, commands
├── Configuration.cs
├── Data/
│   ├── LootDataService.cs       # download, ETag revalidation, transform, disk cache
│   ├── DungeonLootData.cs       # map → territory, gear filter, overrides merge
│   └── LootModels.cs            # cache + upstream DTOs
├── Game/
│   ├── DresserReader.cs         # prism box + outfit-set expansion
│   ├── ArmoireReader.cs         # Cabinet sheet reverse map
│   ├── InventoryReader.cs       # bags / armoury / equipped / saddlebags
│   ├── OutfitCatalog.cs         # MirageStoreSetItem membership + slot order
│   ├── OwnershipTracker.cs      # per-character snapshot, staleness, persistence
│   └── ItemId.cs                # HQ / collectable offset normalisation
├── Core/
│   ├── MissingItems.cs          # the ownership decision, Dalamud-free
│   ├── DutyReport.cs            # territory + ownership → the drawn list
│   ├── DutyCatalog.cs           # named, sorted duty list for lookup
│   └── EquipSlots.cs
└── Windows/
    ├── MissingItemsWindow.cs    # picker, pin, freshness banner, item list
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

## Not implemented

- Retainer inventories — the client cannot read them unless you are at a retainer.
- Per-boss attribution of drops.
- Learning drops from the loot window to close the upstream data gap on new dungeons.
