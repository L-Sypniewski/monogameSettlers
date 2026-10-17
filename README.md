# monoSettlers

**A remake of "The Settlers III" in C# and MonoGame – with a strong AI, carriers that never get stuck, and mountains that don't run dry in no time.**

![Roman economy after one hour of play](docs/screenshots/economy.png)

The graphics come from your own copy of the original game and are read at runtime. Game logic, rendering, AI and tools are
written from scratch. The original is full of bugs and hasn't been maintained for years; monoSettlers plays like the game you
remember, minus the old annoyances.

## Combat

| Skirmish at the tower | Pitched battle |
|---|---|
| ![Small fight: attackers at the guard tower](docs/screenshots/skirmish.png) | ![Pitched battle between Romans and Asians](docs/screenshots/battle.png) |
| Enemies storm the starting tower; the garrison fights back, bowmen shoot from the tower. | Two armies of 30 clash in the middle of the village (staged with the capture option `BATTLE=big`). |

- **Swordsmen, pikemen and bowmen** in three ranks; combat strength depends on territory and gold
- **Conquer towers, large towers and castles**: break down the door, defeat the defenders in front of it, take the building
- **RTS-style controls**: drag to select, groups on Ctrl+1–9, forced moves with Ctrl, orders by right-clicking the minimap
- **Hospitals, priests and spells** per people: convert, samurai sword, freeze, scatter, summon goods
- **Divine upgrades** for soldiers, paid with mana from wine, mead, beer or rice liquor
- **Geologists, scouts and pioneers**: prospect for resources, steal goods, claim land without a tower

## Economy

- **Four peoples** (Romans, Egyptians, Asians, Amazons) with their own buildings, production chains and settlers
- **Logistics without traffic jams**: carriers step aside, swap places and re-plan; a watchdog resolves every deadlock
- **Trails**: well-used routes turn into footpaths you walk faster on
- **Levelling** with real terrain changes, construction sites with diggers and builders
- **Controls like the original**: distribution, transport order, storehouses specialised on one good, smith shares and orders, production statistics
- **Configurable balancing** in JSON: amounts in mountains, fish and stone up to "infinite", production times, recipes

## AI

Computer opponents use the same commands as a human player and each has its own character:

| Character | People | Style |
|---|---|---|
| Hothead | Egyptians | Early army, attacks with everything |
| Unyielding | Asians | Holds on long, gives up only below half strength |
| Legion | Romans | Early gold, attacks in waves, pincer moves |
| Skirmisher | Amazons | Small squads, nearby targets, campaigns only at 2:1 |

On top of that: guerilla strikes, defence by threat, gathered campaigns, retreats when outnumbered, targeted expansion towards missing resources.
The **Arena** (`tools/Homestead.Arena`) runs AI against AI without a window; a tournament script compares characters across many maps.

## Maps

![Random map generator on the start screen](docs/screenshots/random-map.png)

- All single-player and multiplayer maps of the original
- **Random maps** for 2–6 players with mirrored or repeated symmetry, rivers, swamps, ore veins and treasures – fair for every start position
- **Hardcore mode** per player, **save games** in 8 slots, **fog of war**

## Getting started

You need .NET 9 and your own copy of "The Settlers III".

### Getting the original game

The Settlers III is sold DRM-free on GOG as the **Ultimate Collection** (base game, Quest of the Amazons and Mission CD):

- Buy: <https://www.gog.com/en/game/the_settlers_3_ultimate_collection>
- Download after buying: in your GOG library at <https://www.gog.com/en/account> or with [GOG Galaxy](https://www.gog.com/galaxy)

On Linux the Windows installer works with Wine or Lutris; monoSettlers only needs the installed files.

### Build and run

```sh
# Path to the installation (or ~/.config/homestead/settings.json: {"gameDirectory": "..."})
export HOMESTEAD_GAME_DIR="/path/to/Settlers 3 Ultimate"

dotnet build Homestead.sln -c Release
dotnet src/Homestead.Client/bin/Release/net9.0/Homestead.Client.dll
```

Tests: `dotnet test`.

## Project layout

| Project | Contents |
|---|---|
| `src/Homestead.Assets` | Parsers for the original files (GFX, maps), no MonoGame dependency |
| `src/Homestead.Simulation` | Game logic at a fixed tick rate: economy, combat, AI, save games – testable without graphics |
| `src/Homestead.Client` | The game (MonoGame DesktopGL) |
| `src/Homestead.AssetViewer` | Viewer for the original graphics |
| `tools/` | Arena, asset tool, AI tournament, data import |
| `data/` | Game data and balancing as JSON |

Internal project name and namespaces: *Homestead*.

## Legal

- This repository contains **no original assets** (graphics, sound, maps). The game needs your own, legally acquired copy.
  The screenshots above show original graphics from such a copy.
- "The Settlers" is a trademark of Ubisoft; the original game was made by Blue Byte. monoSettlers is a private fan project and not affiliated with them.
- Some data and tables come from [JSettlers / settlers-remake](https://github.com/paulwedeck/settlers-remake) (MIT),
  see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
