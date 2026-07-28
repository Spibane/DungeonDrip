<h1 align="center">Dungeon Drip</h1>

<h2 align="center"><strong>Knows what glamour you already own, and says so wherever the game shows you gear</strong></h2>

<div align="center">

[![Version](https://img.shields.io/badge/version-0.11.0-black)](./CHANGELOG.md)
[![Status](https://img.shields.io/badge/status-Alpha-orange)](./CHANGELOG.md)
[![Changelog](https://img.shields.io/badge/changelog-blue)](./CHANGELOG.md)
[![License](https://img.shields.io/badge/license-AGPL--3.0--or--later-663366)](./LICENSE)
[![AI](https://img.shields.io/badge/AI--DECLARATION-pair-ffedd5)](./AI-DECLARATION.md)

[![C#](https://img.shields.io/badge/C%23-14-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![.NET](https://img.shields.io/badge/.NET-10.x-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Dalamud](https://img.shields.io/badge/Dalamud-API%2015-2F5BB6)](https://dalamud.dev/)

</div>

Dungeon Drip keeps track of what is in your Glamour Dresser and Armoire, and answers the same
question at each point where you might pick something up.

On entering a dungeon or alliance raid it lists the gear that drops there and is not yet collected.
Any duty can also be looked up without entering it. While a Need/Greed roll is open, a companion
window beside it marks the pieces still missing. And at a vendor, a panel beside the shop lists the
glamour gear it stocks, marked by where you already have each piece. Outside a duty it names the
role to queue each roulette as.

## Installing

Not yet in the official Dalamud plugin list. In the meantime it is available from a
third-party repository.

In game, open `/xlsettings` → **Experimental** → **Custom Plugin Repositories**, add this URL
and press save:

```
https://raw.githubusercontent.com/Spibane/DungeonDrip/main/repo.json
```

Dungeon Drip then appears in `/xlplugins` under **All Plugins**, and updates arrive the same
way as any other plugin.

Dalamud offers only minimal support for third-party repositories and would rather you used
the official list, so treat this as temporary — it goes away once the official submission
lands.

## Architecture

```
        loot sources                                  collection
  ┌──────────────────────────────┐          ┌──────────────────────────────┐
  │ Teamcraft         every duty │          │ Glamour Dresser  + outfits   │
  │ Console Games Wiki  per duty │          │ Armoire                      │
  │ drops seen in game     live  │          │ bags, armoury, saddlebags    │
  │ loot-overrides.json  by hand │          │                      opt-in  │
  └──────────────┬───────────────┘          └───────────────┬──────────────┘
                 │ merged, keyed by territory               │ snapshot per character
                 └─────────────────┬────────────────────────┤
                                   ▼                        │
                         missing = drops − owned            │
                                   │                        │
                   ┌───────────────┴───────────────┐        └────────────┐
                   ▼                               ▼                     ▼
              main window                  companion window beside   panel beside
        (grouped by slot or role)             the Need/Greed roll     vendor stock
```

The vendor panel branches straight off the collection: it asks only "do I have this?", so it needs
no loot data and works for any shop, not just the ones that stock duty gear.

**The Glamour Dresser needs opening now and then.** The game wipes dresser data on every zone change
and only loads the Armoire on demand, so nothing is readable from inside a dungeon. Dungeon Drip
works from the last snapshot it managed to take, per character, and the window reports its age.

## At vendors

Open a shop and a panel appears beside it, listing only stock that can be kept as a glamour.
Materials, dyes and food are left out entirely, so an unmarked row in the shop never means "you
already have this".

| Marker | Colour | Meaning |
| --- | --- | --- |
| x | red | Not collected |
| tick | grey | In the Glamour Dresser |
| layers | grey | In a stored outfit set that still has gaps |
| star | green | In an outfit set you have completed |
| archive box | grey | In the Armoire |
| briefcase | grey | Carried or equipped |
| question mark | amber | No dresser data — nothing can honestly be said either way |

The marker carries the state; the name only says whether you need the thing. Missing pieces are
named in plain white and everything you own is greyed out, so the only two things that catch the eye
are the red x and the green star.

**A stale snapshot turns the red x amber, not the grey ticks.** Dresser contents are near-monotonic:
you add glamours far more often than you remove them, so an old snapshot's "you own this" almost
always still holds, while its "you don't" is exactly what goes out of date. Amber on an x means
*probably new, but worth checking*.

Three buttons at the top flip the list filters without a trip to Settings — whether owned pieces are
listed, whether the list is held to your current job, and whether weapons are included. They write
the shared settings, so they change the duty list too, and each tooltip says so.

The panel lists whatever the vendor is currently showing: switching a category or tab re-reads it,
scrolling does not. It opens at the shop window's height with the list scrolling inside so a long
list cannot run off the screen, and its width fits the longest name. Drag it to any size and that
size is remembered; a fourth button appears while a custom size is in use, to go back to matching the
shop. It is pinned where you cannot move it, so fold it to its title bar when it is in the way.

It reads the shop window's position and stock but never draws into it, so it cannot fight with
plugins that do. Covered: gil vendors and Calamity Salvagers, item-exchange counters, currency and
scrip exchanges, Grand Company quartermasters, free-item counters and the Firmament. Anything else
gets no panel — run `/dungeondrip shop` there and report the addon name it prints.

## Roulettes

Outside a duty the window shows, per roulette, which role to queue as:

| Roulette | Queue as | New | Chance |
| --- | --- | --- | --- |
| Expert | Melee DPS (NIN VPR) | 12 | 35% |
| Level Cap Dungeons | Healer | 8 | 27% |

**New** counts uncollected pieces that role can roll Need on across the roulette; **Chance** is those
as a share of everything it can roll on there, which is what the ranking uses. Roles are the
[grouping](#grouping) used everywhere else, melee split by gear type. Hovering gives every role.

Listed roulettes are Expert, Level Cap Dungeons, Alliance Raids, High-level Dungeons and Leveling —
the ones whose gear is tracked. Membership and names come from the game's own sheets, so a renamed
or re-pooled roulette follows the patch.

Odds are per piece you can roll on, not per run, and assume the pool is unlocked; only your level is
readable. Roles you have nothing levelled enough for, and duties above your level, are left out.

Looking a duty up replaces the table; the toolbar's pin button lets go of it again, showing a die
when that lands back here and a thumbtack when it lands on the duty you are standing in.

## Commands

| Command | What it does |
| --- | --- |
| `/dungeondrip` | Toggle the window |
| `/drip`, `/ddrip` | Aliases, claimed only if no other plugin owns them |
| `/dungeondrip <duty name>` | Show that duty; an ambiguous name opens the picker |
| `/dungeondrip config` | Settings |
| `/dungeondrip refresh` | Re-read the dresser and armoire |
| `/dungeondrip update` | Re-download the loot data |
| `/dungeondrip shop` | Identify the open vendor window, for reporting one that is not covered |

## Settings

Settings are split by what they affect. **General** holds the rules every gear list obeys — the duty
window, the loot-roll companion and the vendor panel alike. **Duties** and **Vendors** hold only
what is specific to that surface.

| Setting | Tab | Default |
| --- | --- | --- |
| List owned pieces, greyed out | General | off |
| Only show gear the current job can wear | General | off |
| Skip weapons, main hands and off-hands | General | off |
| Compare against Dresser, Armoire or both | General | both |
| Also count bags, armoury, equipped, saddlebags | General | off |
| Outfit-set ownership: any set, or all sets | General | any |
| Open automatically on entering a duty | Duties | on |
| ...unless nothing is missing | Duties | on |
| Close again on leaving the duty | Duties | on |
| Call out the role with the most missing | Duties | on |
| Group the list by slot or by role | Duties | slot |
| Companion list beside the loot window | Duties | on |
| Panel beside vendor windows | Vendors | on |
| Group the vendor panel by slot | Vendors | on |
| Warn when dresser data is older than | Data | 7 days |
| Record gear that drops in duties | Data | on |
| Fill gaps from the wiki | Data | on |

Labels above are shortened from the in-game wording. Everywhere the plugin lists gear, only pieces
the chosen store can actually hold are shown. Duty coverage is dungeons and alliance raids; trials,
8-player raids, deep dungeons and the rest are not tracked.

## Grouping

Headings collapse with a click and stay collapsed between sessions.

**By slot** — head, body, hands and so on. Each piece appears once, and this is where the
"missing X of Y" count comes from.

**By role** — who can roll Need, for claiming during a run. Melee splits by gear type, since those
pieces go to different jobs:

| Heading | Gear type |
| --- | --- |
| Melee DPS (DRG RPR) | Maiming |
| Melee DPS (MNK SAM) | Striking |
| Melee DPS (NIN VPR) | Scouting |
| Melee DPS (MNK DRG SAM RPR) | Slaying accessories |

Role view repeats a shared piece under every heading that can roll on it, so Slaying accessories also
show under Maiming and Striking, and "of Aiming" shows under both Physical Ranged and Scouting.

## Outfit sets

A dresser slot can hold a whole outfit set, and its pieces count as owned. Hovering a piece shows the
sets it belongs to and where each stands:

| | Meaning |
| --- | --- |
| stored, includes this piece | Nothing to do |
| stored, but this slot is empty | The outfit is held; this slot needs topping up |
| not stored | The outfit is not in the dresser |

A piece can belong to more than one set, so settings offer two readings: owned when **any** set
containing it is stored, or only once **all** of them are.

## Loot data

The game ships no loot tables, so drop lists come from elsewhere. Each entry's source is named in its
tooltip.

| Source | Covers | Refresh |
| --- | --- | --- |
| [FFXIV Teamcraft](https://github.com/ffxiv-teamcraft/ffxiv-teamcraft) | Every duty | Checked on each plugin load |
| [Console Games Wiki](https://ffxiv.consolegameswiki.com) | The duty being viewed | Cached 14 days |
| Drops seen in game | Duties played, including other players' rolls | Immediate |
| `loot-overrides.json` | Hand-written additions | On reload |

Teamcraft lags on brand-new dungeons, which is what the other three are for. Everything is cached on
disk, so the plugin works offline and says when it is. Nothing is uploaded anywhere.

To add drops by hand, put a `loot-overrides.json` in the config folder
(**Settings → Open config folder**), keyed by territory id:

```json
{
  "1252": [45123, 45124, 45125]
}
```

## Files written

All in the Dalamud plugin config folder:

| File | Contents |
| --- | --- |
| `dungeon-loot-cache.json` | Downloaded dataset and its upstream ETags |
| `wiki-loot-cache.json` | Per-duty wiki lookups |
| `learned-loot.json` | Drops seen in game, per territory |
| `ownership-<contentId>.json` | Per-character dresser and armoire snapshot |

## Building

Needs the .NET 10 SDK (10.0.101 or newer) and the Dalamud dev assemblies, which the SDK finds from a
local XIVLauncher install:

| Host | Path |
| --- | --- |
| Windows | `%AppData%\XIVLauncher\addon\Hooks\dev\` |
| Linux | `~/.xlcore/dalamud/Hooks/dev/` |
| macOS | `~/Library/Application Support/XIV on Mac/dalamud/Hooks/dev/` |

```bash
dotnet build DungeonDrip.sln -c Release
```

Without XIVLauncher, extract <https://goatcorp.github.io/dalamud-distrib/latest.zip> and set
`DALAMUD_HOME` to it. The target framework is `net10.0-windows` but builds fine on Linux and macOS.

The solution sets `Platform=x64`, so the DLL lands in `DungeonDrip\bin\x64\Release\`; building the
project rather than the solution puts it in `bin\Release\`. The `latest.zip` beside it is the
distributable, not the build that gets loaded for testing.

### Loading it

**Windows** — `/xlsettings` → Experimental → add the full path to `DungeonDrip.dll`, then
`/xlplugins` → Dev Tools → Installed Dev Plugins → enable.

**Linux** — Dalamud runs inside Wine, so typed paths must be Wine-visible (`Z:\home\...`). Use
the `devPlugins` folder instead, which is scanned automatically:

```bash
mkdir -p ~/.xlcore/devPlugins/DungeonDrip && cp DungeonDrip/bin/x64/Release/DungeonDrip.{dll,json,deps.json} ~/.xlcore/devPlugins/DungeonDrip/
```

Enable it in the same Dev Plugins list, then re-copy and reload after each rebuild. **Open config
folder** does not work under Wine, so the settings window shows the path with a **Copy path** button.

## Project layout

```
DungeonDrip/
├── Plugin.cs                    services, territory tracking, commands
├── Configuration.cs
├── CommandRegistration.cs       claims commands, skipping any already taken
├── LegacyConfigMigration.cs     carries data over from the plugin's former name
├── Data/
│   ├── HttpFetcher.cs           the one HTTP client; capped, timed-out reads
│   ├── JsonStore.cs             every cache file; atomic writes
│   ├── LootDataService.cs       Teamcraft download, ETag revalidation, disk cache
│   ├── WikiLootSource.cs        per-duty wiki lookup, parse, cache, backoff
│   ├── LearnedLootStore.cs      drops seen in game
│   ├── DungeonLootData.cs       merges every source; territory → gear
│   └── LootModels.cs
├── Game/
│   ├── DresserReader.cs         prism box and outfit-set expansion
│   ├── ArmoireReader.cs
│   ├── InventoryReader.cs
│   ├── OutfitCatalog.cs         which sets a piece belongs to
│   ├── OwnershipTracker.cs      per-character snapshot and staleness
│   └── LootObserver.cs          records gear seen dropping
├── Core/
│   ├── MissingItems.cs          the ownership decision
│   ├── DutyReport.cs            territory + ownership → the drawn list
│   ├── DutyCatalog.cs           duty list for the picker
│   ├── ContentFinderIndex.cs    duty names; coverage; roulette pools
│   ├── RouletteAdvice.cs        which job to queue each roulette as
│   ├── JobRoles.cs              who can roll Need on a piece
│   ├── StorageEligibility.cs    what each store can hold
│   ├── ItemNameIndex.cs         item name → id, for the wiki
│   ├── EquipSlots.cs
│   └── Format.cs
└── Windows/
    ├── MissingItemsWindow.cs    picker, freshness banner, item list, roulette advice
    ├── LootCompanionWindow.cs   read-only list beside the Need/Greed window
    └── ConfigWindow.cs
```

The companion window reads the loot addon and never draws into it, so it does not conflict with
plugins that recolour game UI nodes.

## Releasing

Pushing a tag does everything:

```bash
git tag v0.11.0 && git push origin v0.11.0
```

`.github/workflows/release.yml` then builds on `windows-latest`, attaches `DungeonDrip.zip`
to a GitHub Release, regenerates `repo.json` and commits it back to `main`.

`repo.json` is generated by `tools/make-repo-json.py` rather than hand-edited, so the store
entry cannot drift from what was built. It takes the version from the csproj, the API level
from the `Dalamud.NET.Sdk` version, and everything else from `DungeonDrip/DungeonDrip.json`.
The download links point at `releases/latest`, so only the version ever changes.

Two things the workflow refuses to do, both of which produce a release Dalamud silently
ignores: publish a tag that is not on `main`, and publish a tag whose version disagrees with
the csproj.

## CI

`.github/workflows/build.yml` builds on `windows-latest`, fetching the Dalamud dev distribution so no
game install is needed, and uploads the packaged plugin.

## Not implemented

- Retainer inventories, which the client only loads at a retainer.
- Per-boss attribution of drops.

## Licence

[GNU AGPL v3.0 or later](./LICENSE).

## AI use

Largely written by an AI model; see [AI-DECLARATION.md](./AI-DECLARATION.md).
