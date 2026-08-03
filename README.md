<h1 align="center">Dungeon Drip</h1>

<h2 align="center"><strong>Knows what glamour you already own, and says so wherever the game shows you gear</strong></h2>

<div align="center">

[![Version](https://img.shields.io/badge/version-0.13.0-121212)](./CHANGELOG.md)
[![Status](https://img.shields.io/badge/status-Beta-yellow)](./CHANGELOG.md)
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

Its answers also reach the game's own right-click menus, and the window has a second view for the
collection as a whole — which outfit sets you are closest to finishing, how full your dresser is,
and what you are carrying that a box already has.

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
              main window                  companion window beside   panel beside vendor
        (grouped by slot or role)             the Need/Greed roll    stock, panel beside
                   │                                                 the dresser, and the
                   ▼                                                 game's own menus
            collection view
     (sets in progress, dresser
      pressure, carried gear)
```

Everything on the right branches straight off the collection: those ask only "do I have this?", so
they need no loot data and work at any shop, any board and any dresser — not just the ones that
involve duty gear.

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

## At the market board

Open a board, pick a category, and a panel appears beside the browse list marking which of that
gear you already have — the same markers as at a vendor, and the same amber-x rule.

Only the **browse list** gets a panel. The listings for a single item do not: by the time that
window is open you have chosen the piece, so the question was needed one screen earlier, and every
row there would be the same piece repeated once per seller.

Results arrive from the server in pages of twenty, so the panel fills in as the category loads.
Scrolling does not re-read anything, and a text search lists the search's results rather than the
category's underneath them.

This is where a stale snapshot matters most and where it costs the most, since you reach a board by
travelling to one and zoning is exactly what wipes the dresser data. The amber x means *probably
new, but worth checking before you spend the gil*.

## At the Glamour Dresser

Open a dresser and a panel appears beside it listing what you are carrying that is **not** stored
yet — the mirror of every other panel, which ask whether you need what the game is showing you.
This is the one surface where the answer is something you can act on without going anywhere.

The marker column shows where the piece is rather than whether you own it, since by construction you
do not: a case for your bags, a box for the armoury chest, a figure for something worn and a horse
for the saddlebag. A row is amber when something has to happen first — taking it off, or fetching it
from the saddlebag. **Settings → Panels → Glamour Dresser** lists the markers. The header says how
full the box is, because "which of these do I put in" only becomes a question when they will not all
fit.

A toolbar button drops armoury-chest gear from the list, since that gear usually belongs to a
gearset rather than being loose.

Rows read **not stored** rather than "you can add this". Some of the dresser's refusals are not
readable from outside it, so a few pieces the game turns down will still appear.

The panel waits for the box's contents to arrive rather than the window: a list built against an
unloaded dresser would claim everything you own is unstored, at exactly the moment you would act
on it.

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

## Collection

The toolbar's first button swaps the window between duty loot and your collection as a whole. Three
sections, none of which involves a duty:

**Sets in progress** — outfit sets you are part way through, closest to done first. Expanding one
lists what is missing and where each piece drops. A set you own nothing from is not "in progress",
and neither is a finished one; that definition is what keeps the list a readable length.

**Glamour Dresser** — how full the box is and what would free space. An outfit stored a piece at a
time could be one set item instead; a piece in both the dresser and the Armoire is duplicated.
Collapsing is phrased as a conditional, because it needs the outfit item — a tradeable attire box —
and owning all the pieces does not give you one. The loose copies also come back to your bags, so
the space only appears once you clear them.

**Already in your collection** — gear you are carrying that a box already has. Named for the fact
rather than the conclusion: it reports that a piece is also stored and leaves the decision to you,
because that decision is irreversible and is being read off a snapshot that may be days old. Split
by bags, armoury and saddlebag, since what is safe to do differs; nothing you are wearing is ever
listed. Retainers cannot be read, so they are never counted, and the section says how old the
snapshot behind it is.

## In the game's menus

Right-click gear anywhere the game offers a menu — inventory, armoury, inspect, a chat link, the
dresser, a vendor, the market board — and the plugin's options are there. This is the only place
**Where does this drop?** appears: inside the plugin's own windows you can already see where you
are, so it would only be restating the obvious.

| Option | |
| --- | --- |
| Try on outfit: *name* | One per set the piece belongs to; fills the fitting room with the whole set |
| Where does this drop? | The duties that drop it, and takes you to whichever you pick |

Only what the game does not already offer is added. It has its own Try On, Link and Copy, and a
second of each would sit next to the real one saying the same word.

The entries sit directly on the menu rather than under a heading of their own, so trying an outfit
on is one click. A piece belonging to several sets gets a row each; the **D** marks them all as
this plugin's.

**Settings → In-game UI** can also mark the game's own item tooltips, adding an icon and a word to
the category row — the one that says *Body* or *Hands* — saying where the piece is in your
collection. Shorthand rather than a sentence, because that row is not wide.

| | |
| --- | --- |
| gold star | Put away — the Dresser, the Armoire, or a finished outfit |
| silver star | In a stored outfit that still has gaps |
| orange diamond | On you, in no box at all |
| no entry | Not collected |

Four icons for six states, because the word beside it already says which one. The stars are a scale
of how well collected a piece is; the diamond is deliberately not a third grade of star, because
gear on your back is not a worse kind of stored — it is a different thing, and one trip to a vendor
from being gone. It is off by default: it is the only thing in the plugin that changes
what a game window *contains* rather than sitting beside it, and the only thing that hooks a game
function. It appends and never replaces, and marks its own line so it cannot double up or overwrite
another plugin's. If a patch moves what it hooks, the line simply does not appear and Settings says
so — nothing else is affected.

## Commands

| Command | What it does |
| --- | --- |
| `/dungeondrip` | Toggle the window |
| `/drip`, `/ddrip` | Aliases, claimed only if no other plugin owns them |
| `/dungeondrip <duty name>` | Show that duty; an ambiguous name opens the picker |
| `/dungeondrip <gear name>` | Say where a piece is in your collection and where it drops |
| `/dungeondrip item <name>` | The same, forced — for a piece whose name is inside a duty's |
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
| Leave out gear locked to the other gender | General | off |
| Leave out gear locked to another race | General | off |
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
| Panel beside vendor windows | Panels | on |
| Panel beside the market board | Panels | on |
| Panel beside the Glamour Dresser | Panels | on |
| Group each panel by slot | Panels | on |
| Gear options on the game's right-click menus | In-game UI | on |
| Marker on the game's item tooltips | In-game UI | off |
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
│   ├── InventoryReader.cs       ids in bulk, and stack by stack with locations
│   ├── OutfitCatalog.cs         which sets a piece belongs to, and how far along each is
│   ├── TryOnService.cs          feeds the fitting room, one piece per frame
│   ├── ItemActions.cs           what the right-click menus offer, in one list
│   ├── GameContextMenu.cs       that list, rendered into the game's own menus
│   ├── GearRowFactory.cs        item id -> drawable row, cached, shared by the panels
│   ├── ShopWatcher.cs           which vendor is open and what it is selling
│   ├── MarketBoardWatcher.cs    what the board's browse list is showing
│   ├── DresserAddWatcher.cs     what you carry that the dresser has not got
│   ├── OwnershipTracker.cs      per-character snapshot and staleness
│   └── LootObserver.cs          records gear seen dropping
├── Core/
│   ├── MissingItems.cs          the ownership decision
│   ├── CollectionMarkers.cs     that decision turned into a glyph
│   ├── GearRow.cs               one resolved row, shared by every panel
│   ├── DutyReport.cs            territory + ownership → the drawn list
│   ├── DutyCatalog.cs           duty list for the picker
│   ├── DropSources.cs           the loot tables backwards: piece → duties
│   ├── SetCompletion.cs         how far through each outfit set you are
│   ├── DresserPressure.cs       how full the box is and what would free space
│   ├── CarriedGear.cs           what you carry, split by whether it is stored
│   ├── ContentFinderIndex.cs    duty names; coverage; roulette pools
│   ├── RouletteAdvice.cs        which job to queue each roulette as
│   ├── JobRoles.cs              who can roll Need on a piece
│   ├── StorageEligibility.cs    what each store can hold
│   ├── ItemNameIndex.cs         item name → id, for the wiki
│   ├── GearNameIndex.cs         the same for gear that drops, for the command
│   ├── EquipSlots.cs
│   └── Format.cs
└── Windows/
    ├── MissingItemsWindow.cs    picker, freshness banner, item list, roulette advice
    ├── CollectionView.cs        the window's other mode: sets, dresser, carried gear
    ├── AddonPanelWindow.cs      anchoring, sizing and the drag latch every panel shares
    ├── VendorPanelWindow.cs     what is vendor-specific about the vendor panel
    ├── DresserPanelWindow.cs    the add list beside the Glamour Dresser
    ├── MarketBoardPanelWindow.cs what is board-specific about the board panel
    ├── PanelGrouping.cs         slot headings and the filter counts
    ├── LootCompanionWindow.cs   read-only list beside the Need/Greed window
    └── ConfigWindow.cs
```

The companion window reads the loot addon and never draws into it, so it does not conflict with
plugins that recolour game UI nodes.

Try-on is the one place the plugin asks the game to do something rather than reading what it has
already done, and it only ever happens because you picked it out of a right-click menu. Everything
else - dresser, armoire, inventory, shop and loot addons - is read and nothing more.

The game's right-click menus are the one place the plugin appears inside the game's own UI rather
than beside it. That goes through the interface Dalamud provides for exactly this, which composes
plugin entries rather than letting one overwrite another, so no game UI node is touched. It can be
switched off under **Settings → In-game UI**.

## Releasing

Pushing a tag does everything:

```bash
git tag v0.13.0 && git push origin v0.13.0
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
- Nothing else currently planned.

## Licence

[GNU AGPL v3.0 or later](./LICENSE).

## AI use

Largely written by an AI model; see [AI-DECLARATION.md](./AI-DECLARATION.md).
