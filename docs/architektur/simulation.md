# Simulation (`src/Homestead.Simulation`)

Game logic without MonoGame, fixed tick rate (`World.TicksPerSecond`). AI: [ki.md](ki.md).

**Determinism** (lockstep multiplayer: every computer simulates identically): randomness only via `new Random(… + World.Seed)`, no clock, no threads,
no `GetHashCode` in the logic; trigonometric functions only via `DeterministicMath` (Sin/Cos/Atan2 from basic arithmetic – `Math.Sin` & co. come from
the platform's C library), basic arithmetic and `Math.Sqrt` are identical everywhere per IEEE. `WorldChecksum.Of` (FNV-1a on 64-bit words, per part `Parts`, over tick, settlers,
buildings, piles, map, mana) for desync detection; `DeterminismTests` (AI game on `random:4:mirror:11`, 25 min, checksum per minute) runs in the build
on Linux, Windows (x64) and macOS (ARM), `determinism-compare` compares.

- `Map/GameMap`: terrain, objects, walkability, mineral resources (`ResourceAt`).
  `Map/Vision`: fog of war per player – sight of own figures outside (soldiers `Sight`, scouts `scouts.sight` 26, donkeys `trade.sight` 3
  – not scouts –, otherwise `SettlerSight`) and buildings (`Military.SightOfBuilding`: finished ones with `buildingSight` in `military.json`,
  lookout tower 45; occupied military buildings territory radius + `militarySightMargin` (3), empty ones nothing; all other finished buildings
  `otherBuildingSight` (5), residences/storehouses too; construction sites nothing); own land itself sees nothing (demolished/taken by pioneers → shadow);
  explored stays explored; the AI does not use it.
- `Pathfinding/PathFinder`: A*; reachability via `Regions` (kept up to date with walkability changes). Step costs by walking time
  (`SetStepCosts`: path 8, flat 10, mountains 13); land areas per owner.
  Distant targets in legs (`FindRoute`, from `WalkTo`/`Replan`; searches not restricted to own land via `ExactDistance` (96)): coarse route via
  `Clusters` (16×16 cells, per cell its connected parts, neighbours cached, changed cells recomputed), exact only up to the part
  `LegNodes` (4) cells ahead; `Settler.PathIsLeg` → at the end of a leg `World` plans the next one. Exact near the target.
  Unreachable command target (sea, other island): nearest reachable point (`PathFinder.NearestReachable`, ring by ring;
  `World.CommandTarget` for soldiers, geologists/scouts/pioneers, priests).
- `World`/`Settler`: one field per figure, swapping, stepping aside, detour, watchdog pass-through (comment in `World`).
  `Settler.StepProgress` in `World.StepUnits`; speed per target field: mountains ×0.8, footpath ×1.2 (`walking` in `production.json`);
  in addition soldiers per type `speed` in `military.json` (swordsmen ×1.1, pikemen ×1.05, bowmen ×1); `armor` (pikemen 0.5):
  that much less damage per hit, rounded down, at least 1 (`SoldierStats.Taken`; the AI calculates with `Toughness`).
  Settlers and buildings have a player; logistics/recruitment only for the own one. `World.TerritoryRules` enforces own land
  (building spot including door, piles, builder spots, paths – except soldiers, priests, geologists, scouts, pioneers); the stranded flee (`Idling`); whoever loses the land under their feet while on a job (`World.IsStranded`) aborts, drops what they carry and only accepts new jobs on own land.
- `Trails`: carriers on a job tread footpaths into grass (`GroundCover.Trail`, after `trailSteps`), which disappear after `trailFadeSeconds` (300).
  Farmland and paths keep one field of distance from everything except grass/levelled ground and from each other (`GameMap.CoverFits`, otherwise the
  original transitions break). Grain is also sown on paths and shortens paths around it (`Trails.CutAround`); sowing turns grass within the radius
  `farmlandRadius` (1) into farmland, as far as it fits and lies within the work area.
- `Buildings/`: definitions from `data/buildings/*.json`.
  - Building spot check (`CanPlace`, `DoorLeadsOut`, `PlanLevelling`): "seen" via a marker field over the map (`NewSearch`/`See`), no HashSet.
  - `Construction`: levelling (`World.PlanLevelling`, `levelling` in `production.json`): footprint to the average height, then ring by ring around it,
    until no neighbour deviates by more than `maxSlope` (8) (wider on a slope). Effort = height moved; invalid above `maxWork` or if a field
    to be changed is built on, planned otherwise, unwalkable or foreign. Diggers: `heightPerDig` per run, never a slope above `maxSlope`
    (on a slope the top first). Buildings that are finished immediately level immediately. Mines: mountains only, no levelling (`levelling: false`, `NeedsLevelling`).
  - Builders without material become free (idle, pushable, one step away, switch construction sites); spots only as long as the material suffices for one more.
  - Buildings that JSettlers does not have (war machine works, gunpowder maker): `data/buildings/handmade.json` per people
    (footprint = grid points covered by the original image, door/piles according to the image), loaded before `overrides.json`.
  - `World.RemoveBuilding`: demolition/loss with a dust cloud (`DustClouds`); the share `demolition.payback` of the building material stays on the ground.
    The land of demolished military buildings stays with the player, but unprotected (an enemy military building takes it including its buildings).
  - Demolition: the worker keeps profession and tool and takes over the next building of their profession.
- `Economy/`: goods, piles.
  - New building pile (construction site, finished) on a loose pile: its goods are put down next to it (`AddPile` → `DropGoods`), not overwritten.
  - `Logistics` with watchdog: per good among the current consumers – construction sites as one (`Logistics.Construction`), otherwise per building type –
    one drawn by lot according to the player's shares (`ShareOf`/`SetShare`; defaults `distribution` in `production.json`, 0–1, e.g. food mines 1 : 0.1,
    planks construction site 1, charcoal burner/shipyard 0.5; listed goods give unlisted consumers 0, unlisted goods give everyone 1; share 0 gets nothing
    unless all are 0), then the nearest target with room; storehouse only if nobody else wants it.
  - Store only what the player stores (`IsStored`/`SetStored`); a storehouse can be specialised to one good
    (`SpecializationOf`/`SetSpecialization`, `StoresHere`, overrides the general rule); default `storage.materials`: tools, weapons, gold, gems.
  - Offers by transport order (`TransportOrder`/`MoveInTransportOrder`, default `storage.transportOrder`); loose piles on nobody's
    land: the player with free carriers. Otherwise goods stay at the output pile (8; full → the producer waits, gatherers too).
  - Construction site with highest priority (`Building.HasPriority`): its piles before all consumers (shares too), its goods at the front of
    the transport order; if more is missing than is ready, carriers only give up that many to other construction sites (`Logistics.FreeForPriority`, nearest
    first; on the way to the source: aborted, loaded: dropped – in the original all of them); cancelled deliveries are replaced at the next reconciliation.
  - `Problems` counts abort reasons.
  - `Production`: workshops; smiths make one product per cycle: what was ordered, then expected tools, otherwise weighted in turn by shares
    (`shares`: toolsmith 0 = demand only; weaponsmith sword 100 %, bow 70 %, spear 45 %), as long as below `keepInStock` (0 = no limit);
    choice only with iron and coal.
    Mines: food pile at most `mineFood.pileCapacity` (4); first `mineFood.freeRuns` (8) runs per building free; success chance =
    remaining ore in the radius against `resources.fullAmount` (8) per field (empty/foreign fields = blanks, every find lowers it; `MineChance`); a blank costs time and food.
    Gatherers: waterworks only at rivers; farmer, winegrower, rice farmer, beekeeper according to `crops` – grain creates farmland (`GroundCover.Farmland`,
    walkable, until demolition/loss of the farm), grain/wine/rice grow in 600 s as in the original; wine faster on a slope; rice only on swamp; vines/beehives grow back.
    Beekeeper: at most `maxFields` (12) hives per beekeeping (`Production._fields`; ownerless ones in the work area – old save games – it takes over), new ones only with
    at least one tree within the radius and `fieldSpacing` (5) fields of distance from other hives (setting up `sowSeconds` 1.2 s = one animation round), where most of them stand; hives without trees around them are collected after harvesting (then new ones near trees, otherwise none);
    demolition/loss of the beekeeping takes its hives along immediately. A hive is full in 720 s, with `idealTrees` (10) fully grown trees within the radius `treeRadius` (5) `1 + treeBonus` (3) × as fast = 240 s
    (12 hives with a harvesting run of ~20 s each → 3 honey/min as in the original; fewer trees proportionally).
  - `MapResources` (`Production` part `MapResources.cs`): saplings and crops grow in stages, stumps rot, exhausted resource fields
    regenerate (`Grow`, `Regenerate`); depletion per kind via `UsesUp` (`_depletionDue`), per field via `TakeResource` (`_resourceTaken`/`_regenerating`),
    stones shrink via `UsesUp`/`_stoneCuts`; spells intervene via `Hasten`/`GrowsSlowerThan`/`BurnTree`.
  - `SmithScheduler` (`Production` part `SmithScheduler.cs`): what a smith forges next (`ChooseProduct`: what was ordered, then missing
    tools, otherwise weighted in turn by shares below `keepInStock`), together with the player's orders, share bars and round-robin credits.
    Without a player share, the share of the calling smith itself applies (`SmithRecipe.Shares`), not that of the first smith in the data;
    the player share (`SetShare`) overrides both.
  - `Gathering` (`Production` part `Gathering.cs`): the gatherer's work run outside (`StartGathering`/`FinishGathering`/`FindTarget`: find a target,
    go there, work, bring the result home) and the farmland bookkeeping: `_farmland`, which goes with its farm (`RemoveFarmland` on demolition or
    loss), alongside the hives per beekeeping (`_fields`, see above).
  - `Healing`: the hospital healer calls the weakest (fewest HP, tie: nearest) wounded idle own soldier within the radius
    `hospital.callRadius` (40) onto the healing spot in front of the door (`HealSpotOf`; not those holding position); whoever stands still next to the door, the healer's spot or the healing spot
    is healed without being called (the weakest first),
    heals them (from then on only interruptible by demolition; full HP after `healSeconds`, wherever they are), goes inside, `callPauseSeconds` pause.
  - `Housing`: residences release carriers and provide that much housing (plus the starting population, `SettleStartPopulation`). Room is needed by
    carriers and workers (diggers/builders too – construction sites recruit at most the share `Share.Diggers`/`Builders`, at least one each),
    not by soldiers, geologists, scouts, pioneers. Without room they strike (`Settler.IsStriking`): first idle carriers, then workers (they stay inside).
  - `Recruitment`: carriers fetch tools and become specialists; the three shares together ≤ 100 % (`SetShare` limits). Tests that are not about tools: `TestConfig.NoTools`.
  - `Idling`: stranded carriers/professions outside own land flee to the nearest reachable own field (`RefugeRadius` 64, otherwise
    an own building door); without one they wander around and die after 30–60 s (fixed per settler via hash, ghost, death sound), saved once back on own land.
    Idle ones (carriers, soldiers, diggers, builders, geologists, scouts, pioneers, the unemployed) get out of the way
    (after `IdleTicks`; soldiers immediately, every tick, with one field of distance).
  - `Geology`: geologists walk like soldiers (spread over free fields); on mountains they search within the radius `geologists.searchRadius`
    (next free field, ties random) and put up signs with raw material and amount.
  - Scouts (`Profession.Scout`, graphic `THIEF`): converted/sent like geologists (`Geology.Convert(…, Profession.Scout)`), never search.
    To other players an own carrier (`Settler.IsRevealedTo`), not attacked until an enemy soldier outside comes within
    `scouts.revealRadius` (10) (`Military.RevealScouts`, every tick); once recognised, the scout stays recognised as long as the player sees them
    (`Military.Sees`: sight of own figures/buildings like the fog of war), then camouflaged again. Recognised: fought like a geologist
    (by tower bowmen too), the AI counts them among enemy groups. AI suspicion (`AiPlayer.InspectScouts`): scout on its land → the nearest
    idle soldier follows them (otherwise it calls one from the nearest military building with more than one soldier).
  - `Theft`: scouts steal (`Steal`; Alt+right-click: `StealAt`) the nearest good within the radius `scouts.stealRadius` (5) – an unreserved unit,
    not from own buildings or loose goods on own land; carry it (`Carrying`, crouched), speed `scouts.loadedSpeed` (0.5);
    drop it silently on own land or where they die. Sent with `StealAt`, they walk straight back with the good to where they stood at the command.
  - `Pioneering`: pioneers (converted/sent like scouts; `Claim`, Alt+right-click `ClaimAt`) take field by field: the next free or
    foreign, unprotected field (`Military.IsProtected`) within the radius `pioneers.searchRadius` (10), ties nearest to the target point;
    `claimSeconds` (2) post, then the field belongs to them including unwalkable neighbours (`Military.TakeLand`, unprotected foreign buildings fall);
    on the shore also the free water in front of it up to `pioneers.waterReach` (6) fields out (across own water) – room for a dock and fishermen; the client never draws border posts on water (shore = border).
    In protected enemy land towards free land, otherwise the end. Completely enclosed free land (not map edge/foreign land) is
    taken all at once (up to 4096 fields). Land unprotected.
  - `Trade`: land trade. The donkey farm (`trade` in `production.json`: grain + water → one donkey, `Profession.Donkey`, not a good) breeds
    as long as the player has fewer than `keepIdle` (4) donkeys without a job. Donkeys: not controllable, immortal, walk everywhere, do not count
    for housing/profession shares. Marketplace (`market_place`): 6 piles `PileRole.Trade` (never an offer); per marketplace destination = point (`Reachable`:
    walkable and reachable from the marketplace, on a building its door; a new destination deletes the stops), optionally up to 3 stops;
    per good an amount (`Remaining`, ∞ = `Trade.Unlimited`, decreases with each arrival at the marketplace). Carriers only deliver with a destination (logistics: marketplace always
    share 1, even from the destination itself – with ∞ an endless loop as in the original). Every unit at the marketplace calls the nearest free donkey;
    it loads up to `load` (2), walks the stops (watchdog skips), unloads at the destination (at the door of an own storehouse into its piles),
    back via the same stops in reverse. An enemy soldier (tower bowman too) strikes a loaded donkey once: the good falls,
    the donkey turns back (`Struck`, event `donkeyRobbed`, trade); unloading on foreign land → `World.GoodsHandedOver(from, to, good)` (C# event, not saved); nobody attacks empty donkeys (only loaded ones are `_combatants`). Destination unreachable → home with the load, next to the marketplace.
  - `Shipping`: the shipyard (`shipping` in `production.json`) first needs a dock (`Dock`: shore field + direction, `dockLength` (4) fields):
    `PlaceDock` sets it immediately where its end is closest to the click – free, walkable own shore up to `dockDistance` (20)
    from the shipyard, reachable from its door, dock over own open water (from the second field on, water all around), a new one replaces the old one,
    goes with the shipyard. Drawn as map object `MapObjectCodes.Dock` (219, file 1 sequence 112 per field), walkable (`GameMap.ComputeWalkable`).
    Shipbuilding (`Order`, one at a time per shipyard, `ships` with guessed costs): logistics only deliver to the shipyard what the ship still needs
    (`Wants`); the shipbuilder (`DOCKWORKER`, hammer, builder graphics; `Production` part `Shipbuilding.cs`) fetches one unit at a time from the pile, carries it
    to the end of the dock, hammers for `buildSeconds` (4); every unit makes the hull grow (`Progress`), the last one launches the ship
    (`Berth`: two fields behind the end of the dock, across the dock). Demolishing the shipyard discards the order. `Launch`: finished immediately (captures).
    `Ship` (own list, not settlers): steered like a soldier (`Move`, breadth-first search over the water to the nearest reachable point),
    only on `IsNavigable` (water with `ShoreClearance` (3) water all around, no dock), keeps `Spacing` (6) from other ships (waits, then detours),
    `stepSeconds` (0.5) per field, sight `sight` (16). Ferry (`Ferries.cs`): takes `passengers` (7) units (soldiers, priests, geologists, scouts,
    pioneers) on board when it lies still and has walkable land (shore, dock) within `landingDistance` (6) (`IsNearLand`); `Board`: each walks to the
    nearest reachable shore field (soldiers forced), boards there (`IsInside`, position = ship, travels along); a new command cancels.
    `Unload`: everyone onto the free land fields nearest to the ship (breadth-first search from the shore). Trade ship 0 places. No combat yet.
    Sea trade (`SeaTrade.cs`): harbour (`trade.harbor` = `harbor`, `Trade.IsHarbor`/`IsTradingPost`) like marketplace + donkeys – route, destination, amounts in `Trade`
    (piles `PileRole.Trade`, logistics share 1), needs a dock like the shipyard (`HasDocks`); route points on open water that the dock
    reaches (`SeaPoint`: nearest in the same body of water `SeaRegion`, not saved; destination with a beach within `landingDistance`). Every good
    at the harbour calls the nearest free trade ship (one per harbour loading at a time): sails to the dock, loads up to `cargoPiles` (3) piles of
    `pileSize` (8) of one good each (`loadSeconds` per unit, waits up to `loadWaitSeconds` for more), sails the stops, puts down one loose
    pile per pile on the beach at the destination (no dock there), back via the same stops. Watchdog: point not reached after 3 attempts → stop skipped,
    destination → home, load next to the dock. Trade ships under way take no commands (`HarborOf`, `CargoOf`).
- Teams (`World.Teams`, `AreAllies`; from the start screen, saved): allies do not fight each other (`MilitaryCombat.IsEnemy`, `Attack`,
  tower shooters, war machines `IsTarget`), enemy spells spare them (`Magic.Affects`), towers/pioneers take no allied land
  (`Claim`, `TakeLand`, `IsClaimable`), thieves do not steal from them (`Theft.IsOwn`); `ConquestGoals`: won when all enemies are defeated (outcome per player, `IGameGoals.OutcomeOf`/`Decide(player, …)`).
  Shared sight: `Vision`, `Military.Sees`, `Magic.SeesBySpell` count the whole team; an unmasked scout is unmasked to the soldier's whole team.
- `Military/`: the barracks train at the door (the carrier becomes a soldier there at the same moment, does not go inside), recruits walk to the gathering point.
  Military buildings only call idle soldiers up to `garrisonCallRadius` (100) fields from the door; those further away stay where they are.
  One carrier per weapon at a time (as far as the carrier reserve allows, those walking to any barracks count), each is trained at the door or right next to it. Target garrison per type (`ChangeWanted`: new 1 swordsman; never 0,
  never above the infantry/bow spots; "+" with full infantry spots replaces the other infantry kind; surplus soldiers turn back or come
  out, never the last one – a replacement swaps at the door). Occupied ones claim land; always their own ground (otherwise a conquered tower would fall again at once).
  - `Move`: target fields around the point free ones first (nobody stands there, no other soldier sent there), otherwise occupied ones, then in turn.
    On the last step soldiers do not walk through a standing figure onto their target (watchdog) but stop in front of it (counts as arrived).
  - `MilitaryCombat`: opponent search via cells (`CellSize` 8) of the fighters at the start of the tick, per player (only the enemies' cells searched) (`EnemiesNear`, same order as `EnemiesOf`).
    Idle soldiers and those with a non-forced move order attack enemy soldiers and occupied military buildings
    (door in sight), nearer ones first; walking ones continue afterwards; the garrison stays inside. Melee fighters first break down the door
    (`door`, HP like a pikeman; 5 s without an enemy within radius 6 → the guard goes inside, door regenerates 0 → full in 30 s).
    Then one defender at a time immobile in front of the door (`FightPoint`; spots per people/building as offsets in `door.points`, otherwise automatic,
    front first; infantry first, then bowmen), only attackable by melee fighters on the door spots (`door.fighters`: tower/large tower 2,
    castle 1; pikemen as with range 1, the rest wait). Whoever kills the last defender is inside. Waiting attackers: a free field 2–5 from the guard's field, never assigned twice in the same tick (`QueueSpot`);
    everything full → where they stand or the nearest free field, never onto the guard's field (otherwise hundreds on one field). Bowmen do not attack buildings
    (neither on their own nor on command – `Attack` skips them; AI campaigns without them) and on the ground do not shoot below `minRange` (5):
    too close → back off (`SoldierStats.InReach`/`OffReach`), door guards too; from the tower without a minimum range.
    Bowmen in military buildings shoot `towerRangeFactor` (1.69) further (`TowerRange`, AI tower fire too). Bowmen hit at the moment of the shot; the arrow (`Military.Arrows`, `ArrowTicks`) is display only. Cooldown between blows/shots per soldier (`_nextShot`), not per command: a new command in the middle of a fight does not reset it. Near the enemy local pathfinding (`combatPathRadius`).
    "Position halten" (`Military.Hold`, selection panel for soldiers, in the place of "Land einnehmen"; `Move(…, hold: true)` walks there first):
    not idle (no building calls them), only fights enemies in range, pursues nobody, returns to the spot, until the next command
    (`HoldTarget`/`Holds`; the hospital does not call those holding position). AI: the wounded, parked melee fighters, bowmen of the battle formation.
    Waypoints (`AddWaypoint`, Shift+right-click): with a command (walking, attack, own fight) that one first, then in sequence up to `MaxWaypoints` (3)
    points, a further one replaces the last (`Fighter.Waypoints`, `Dismiss` makes the next one a move order); without a command (or holding/patrolling) like `Move`.
    Patrol (`Patrol`, Alt+right-click): to the point and back to the starting position, back and forth until the next command (`Fighter.PatrolBack`); fights
    on the way as with a move order, independent attacks end with the patrol continuing (`EndAttack` keeps `MoveTo`).
  - Combat strength (`CombatStrength`): own land 100 %, otherwise a starting value per number of players (`startPercent`, measured in the original: 1 → 54 % (as there, although pointless), 2 → 50 %, 4 → 37 %, 8 → 25 %, 16 → 14 %); plus gold on own land (by land ownership; Egyptians: gold
    half, gems double), every further percentage point costs according to the combat strength already reached (bands `stepPercent` from 0 %, `unitsPerPercent`, measured in the original: up to 50 % 1.2 bars, from 90 % 20; a low start rises faster), with no upper limit (200 % = double damage outside; forces the rich to attack). The AI knows all values and assesses the place of combat.
  - Values: `data/military.json`; territory radii measured from original maps.
  - `WarMachines` (`warMachines` in `military.json`): one machine per people (catapult, ballista, cannon, gong) from its works:
    `Order` queues a machine or a load of ammunition (the gong has none; `Cancel` takes back the last one not yet started), logistics only deliver what
    the current order needs (`Wants`), the shipyard worker hammers unit by unit in front of the door (`IAssembly`, shared with the shipyard,
    `Shipbuilding.cs`); loads lie at the works (`Loads`). Machine = `Profession.WarMachine` (people in `People`, `Ammo`), does not count
    as population, moves like soldiers (`Move`, speed `speed`), occupies its field and the 6 neighbours (`Settler.IsWide`/`Body`: pathfinding only via points with
    6 walkable neighbours (`PathFinder.IsClear`, exact, at most `WideSearchLimit` nodes; if it cannot get through, it drives as close as possible – as in the original
    too), pushes idle figures aside, nobody swaps with it, waiting figures plan around all 7 fields, melee reaches to the edge), only fires at finished enemy military buildings (`Attack`; standing ones at anything within
    `range`) every `cooldownSeconds` (5) with `hitChance` (catapult 50 %, ballista 75 %, cannon 90 %, gong 50 % – from the user), projectile
    `MachineShot` (gong immediately), impacts briefly in `Impacts`; every hit counts against `buildingHits` (`DamageOf`), the last one demolishes it
    (the garrison comes out alive, event `buildingDestroyed`); one hit heals per `repairSeconds` (60) after the last one. `shots` (6) per load; `Reload`: to the nearest own works with a load. Melee fighters destroy machines
    (`health`, dust instead of a ghost), bowmen do not (`EnemiesOf(…, bowman)`). Range, speed, HP, hits per building guessed.
    Not yet: avoiding mountains, AI. Gunpowder maker (Asians): coal + sulfur → gunpowder (`POWDER_MAKER`, without a tool).
- `Magic/`: mana per player. Small temple: wine/mead/beer/liquor → mana (one unit per `templeSeconds` 35.7 as in the original, liquor 2 mana); the large temple sends a priest immediately and every `priestSeconds`.
  Spells as in the original (values from wiki.siedler3.net "Mana und Magie"): 8 per people in the menu (`menu`, order by the icons; `own` = the AI's people
  spell), per spell `cost`, `radius` (0 = whole map), `count`, `seconds`, `factor`; `placeholder` = not implemented yet (visible, not castable).
  `Cast`: the priest walks up to within `range` (24) (`anywhere`: works from where they are, unlimited range – reinforcements, call/send goods; without a radius immediately on the menu click),
  only casts where something affected is (`Magic.HasTargets`/`Affected`, nearest first up to `count`), no combat needed; cost per player and spell kind
  rises by `costIncrease` % with every cast (25 as in the original, rounded up: 20, 25, 32 …) (`Magic.CostOf`, the menu shows the current price); takes effect immediately at the start of the cast (`castSeconds` only the gesture).
  Spells that move/transform units (Convert, Banish, Reinforcements, Bows to pioneers) take priests, soldiers, scouts, geologists, pioneers outside,
  no soldiers of a military building (`Military.IsTowerBound`: garrison, door, on the way in); spells on soldiers for a time (`Settler.Charm`,
  `SpellCharm`: Samurai sword swordsmen only ×2 speed ×1.33 damage, Fear ×0.5 damage on the caster's land, Strengthen ×2 outside own land,
  Shield ×0.67 damage taken, Cursed arrows no arrow damage; Freeze `FrozenUntil`) take those too (frozen tower bowmen do not shoot);
  cast again it extends, does not add up (`Military.DamageOf`/`Taken`). Goods spells (Midas, Fish to meat, Stone to iron, Gold to stone: up to 40 units
  within the radius, no matter whose, loose/output/storehouse, not reserved, `World.TakeUnits` → `DropGoods`; Call goods: from own land to the priest; Send
  goods: those around the priest to the target; both unlimited range). Sight (`SpellSight`, `Magic.Sights`: Eye radius 32, Reveal the map entirely, 20 s; fog of war and `Military.Sees`).
  Land (`LandSpells.cs`): transform ground (Grassland, Horus' heat, Drain the swamp – moor and mud –, Melt snow → mountains) via ground classes,
  edges one field each like the random map generator (`Paint`, not under buildings/piles; farmland/paths that no longer fit are removed; the client draws
  `TerrainKinds.Of`, the minimap still the original); halve resources, Fish flood (+1–4 per water field, at most 15), Forest fire (trees within the radius → stumps,
  after `FireSpreadTicks` once those directly next to them, flames), Forest's favour (fully grown trees on free grass), Stone curse (10–20 rocks of different sizes, 2 fields apart),
  Growth (`Production.Hasten`: grain/wine/rice ripe in 180 s at the latest). Heavenly gifts (`SummonGoods`: 3–4 piles of 4–8 each of food, weapons,
  tools, gold, iron, coal, ore within the radius `SummonSpread`; Egyptians 50 % stones in addition; flames staggered). Priests can be attacked.
  Old save games adopt the current rules (`Magic.FollowRules`). Two accounts: spells consume `Mana`, upgrades check `ManaEarned`
  (`upgrades.manaEarned` per n-th upgrade, original: 10/30/60/110/170/200). `Upgrade`: needs a finished small temple; first all three types to level 2, then 3;
  only applies to those trained afterwards. Values: `data/magic.json`.
- `Commands/` (lockstep preparation): player commands as data (`GameCommand` records: player, IDs of settlers/buildings/ships, points,
  enums, numbers; building type as `BuildingDefinition`, unique via people + name), only act on the player's own things. `World.Commands`
  (`CommandQueue`): `Issue` assigns `Sequence`, `World.Step` first applies all waiting ones in this order, results in `Applied` until
  the next step; while paused only `ApplyWhilePaused`. Groups: `UnitCommands` (right-click `MoveUnits`: attack an enemy building, otherwise
  walk; waypoint, patrol, hold, specialists, priests, spells, ships), `BuildingCommands` (construction site, demolition, pause, priority,
  work area, garrison, storehouse, marketplace, shipyard, war works), `EconomyCommands` (distribution, transport, smiths, professions, recruitment,
  upgrade), `ChatCommands` (`Say`, `AiReply` for the AI chat, `Cheat`). Waiting commands are stored in the save game.
- `Persistence/`: `Snapshot` writes the whole object graph via reflection (all fields, private ones too, object identity; type table with
  field names – missing ones skipped, new ones: collections/classes with a parameterless constructor empty, non-nullable references without one → incompatible, otherwise the default value (not the initialiser!); Dictionary/HashSet with their inner structure, hashes recomputed on loading → the match continues
  tick-exactly, `SaveTests`). Delegates only with targets in our own assemblies (client subscriptions are set again after loading);
  `[NonSerialized]` omits; foreign types abort with the path, `JsonElement` as text. New fields need no save code.
  `SaveSlots.Load` recomputes walkability (`GameMap.RefreshWalkable`; docks of earlier builds). Type names resolved without the assembly version; a missing type/method → `SaveIncompatibleException`. `SaveLoadFailure.Of`: kind (incompatible/damaged/unreadable)
  and error code `HS-<kind>-<FNV-Hash>`; the title screen shows it in the message box (`ShowLoadFailure`), the console the exception.
  `SaveGame`: world, catalogues, map, sight, client values. `SaveSlots`: 8 slots `slot N.hsav` in `~/.config/homestead/saves`
  (`HOMESTEAD_SAVE_DIR`), header `SaveInfo` (map, people, time, tick) uncompressed, the rest gzip, via a temporary file.
- `Events/` (`GameEvents`, `World.Events`): messages per player from `data/events.json` – category info/combat/resources/trade,
  `cooldownSeconds` per player/event (0 = always), text (`{building}`), on/off. `Raise(id, player, place, settler/building)`.
  Combat: `attacked`, `buildingAttacked` (door/guard), `buildingLost`/`buildingCaptured`. `Tick` checks the states of finished buildings every second,
  reports once after `afterSeconds` until the end: `noStaff`, `noTool`, `noResources`, `mineExhausted` (chance ≤ `maxChance` %). `Announce`: without a place.
- `ChatLog` (`World.Chat`, `World.Say(player, text)`): global chat, lines with tick and player in order, trimmed, empty ones nothing,
  at most `MaxLength` (200) characters; saved along.
- `SoundCues` (`World.SoundCues`, not saved): only collected when the client sets a list (it empties it per frame); `Strike` on every
  hit/shot (at the door too, with the soldier type), `Death` in `MilitaryCombat.Kill` (with the people of the dead), `TreeFalls` on felling, `BuildingFalls` in
  `RemoveBuilding`, `Spell` at the start of a cast, `Healing` (hospital), `Claiming` (pioneer post), `GeologistFinds` (with amount),
  `Sowing` (with `Trade`); `Strike` carries the people (the Egyptian bow sounds different).
- `GameHistory` (`World.History`): every 30 s (`SampleTicks`) one `HistorySample` per player (land, settlers by kind, soldiers by type,
  buildings, construction sites, military buildings, housing, strikers, stock, produced goods, combat strength, mana) plus running totals
  (killed/fallen, buildings captured/lost, completed/destroyed); for the statistics screen.
- `Lockstep/`: `LockstepSession` (commands for turn now + delay, waits for the bundle, compares checksums → `Desync`), `TurnRelay` (server logic), `CommandCodec`
  (JSON network format), `LocalRelay`, `GameFingerprint` (protocol, version, data hashes; details: `docs/MULTIPLAYER.md`).
- `GameSetup` (`MapChoice`, `PlayerSetup`): game setup as data (JSON) – map by seed or hash, players, starting positions, game values, one seed for
  all draws; `Start` → world with AIs (details: `docs/MULTIPLAYER.md`).
- `OriginalScenario`: map → world (buildings, piles, settlers; settler type 23 = scout/thief, 22 geologist, 36 priest, 40 pioneer; the garrison of
  military buildings from the map is inside immediately, `Military.Station`, target = garrison; without a value 1 swordsman level 1 as in the original). Maps without buildings (multiplayer, also single player such as Beginner I/III): `StartSetup` gives every player the equipment from `data/start.json` (the starting tower only wants infantry, bowmen wait outside).
- `Map/Generation/RandomMapGenerator`: random maps as `OriginalMap` (`OriginalMap.Create`), same `RandomMapSettings` + seed = same map,
  values `data/randommap.json`, size by number of players. One landmass with a rugged coast (distorted noise, half circle, half rhombus;
  detached parts sink, cut-off starts get a land bridge to the centre); mirrored, a circle in grid geometry. Every start the same: flat starting area,
  mountain with coal/iron/gold, pond with a brook (waterworks need rivers), forest, stones. In addition mountains, lakes, rivers to the sea, swamps (at least one),
  forests, stone fields, ore veins, fish, decoration, treasures (goods piles ≥ 32 fields from starts; "Viel": in addition a hoard full of piles of gold/weapons in the
  centre of the map, `hoard`). King of the Hill (`RandomMapSettings.KingOfTheHill`, `kingOfTheHill` in `randommap.json`): the centre as a hill with a hoard,
  mountains between the passes (full veins of gold/gems/coal) and a ring of unwalkable moor (80/81) with one pass each towards a start;
  home features keep their distance (`OnHill`). Rivers as a steadily turning course in half-field steps (downhill, meanders via noise), wider downstream; grassland slightly uneven. Symmetry: `Mirror` (wedge area; exact for 2/3/4/6,
  5 rounded), `Repeat` (starting tile `homeRadius` copied). Landscape bytes last, from ground classes (water depth, beach, edges one field each,
  so that every triangle has an original transition tile – features keep their distance). Starts without a connection → reroll.
