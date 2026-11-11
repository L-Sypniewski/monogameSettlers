# AI (`src/Homestead.Simulation/Ai`)

Concept of the characters and tactics: [../KI-TACTICS.md](../KI-TACTICS.md).

- `Ai/` – `AiPlayer`: the same commands as a human, knows the whole map. `World.AiPlayers` triggered every tick. `MapSurvey`: map overview in cells.
  - Building according to `data/ai.json` and demand: a residence as long as carriers ≤ 3 × production buildings (size by finished sawmills/stonecutters
    per people); no material held back for towers; no further building of a kind as long as one is waiting for workers; raw-material buildings without
    a spot try further cells; waterworks without a spot → expansion towards the river. Buildings without a spot at the anchor → the roomiest own cell.
  - Growth after the build order (`AiPlayer.Growth`, `growth` in `ai.json`): per window (120 s) it measures what finished, occupied buildings
    are waiting for; a good that one kind of consumer waits for ≥ `starved` (25 %) of the time (per kind, not averaged), with no stock in storage → one more producer (workshop/mine only if the
    existing ones work ≥ `busy` (70 %) – otherwise the bottleneck lies further up the chain); fully loaded weaponsmiths/barracks (with weapons in storage)/small temples → one more.
    One per kind at a time; not if one was finished within the window.
  - Mines only from a success chance of `mines.minChance` (0.3), demolition below `mines.abandonChance` (0.1); no spot → ore missing → expansion.
    Resource buildings without a spot (`_noSpot`) are only searched for again after `NoSpotRetryTicks` (15 s, a constant – config fields are missing in old save games).
    Smelters only with an own mine for their ore.
  - Before demolishing exhausted stonecutters/fishermen: move the work area (up to `MaxWorkCenterDistance`) to where more is in range (`BetterWorkCenter`).
  - Toolsmiths not in the build order, only when a worker is waiting for a tool (without a plank/stone reserve: if the saw is missing, no more
    planks come). Without a toolsmith, further weaponsmiths only as long as a smith remains besides (unemployed smiths + hammers − smithies
    without a smith ≥ 2, `AiPlayer.Smiths`); if the toolsmith still finds none, a weaponsmith is demolished (unoccupied ones first) and its smith switches.
  - Storehouse only with gold/gems (gold smelter/gem mine finished or bars/gems on own land, also conquered), then at `stockWhenWaiting` (8)
    storable goods lying around and fewer than 2 free storage spots.
  - Residence demolition if `spareRoom` is still free afterwards. Foresters checked every `forester.checkSeconds` (10), stopped from `forester.maxTrees` (10) trees/saplings in their area.
    Foresters in the build order only as an upper limit: built at the finished woodcutter with the fewest, below `forester.buildBelowTrees` (15), trees/saplings
    in its work area, whose centre lies in no forester work area (full forest: none), at most forester radius − 2 away (otherwise the next
    needy woodcutter, otherwise none – previously they ended up far away, the woodcutter stayed needy, one forester after another), one after another
    (the last one must be working); without a woodcutter (no tree on own land) at the starting point.
    Beekeeping like a raw-material building: the cell with the most trees, woodcutters around it (16) count against it; without trees none.
  - No building on farmland or in the field area of own farms (radius 4 around the work centre); a farm needs 35 free grass fields in its area.
  - Expansion: towers deliberately towards missing raw materials (distant targets tower by tower), avoids spots with more than `maxWaterShare` water.
  - After the food chain: large temple, mana chain, small temple; late gold mine/gold smelter (Egyptians: gem mine);
    expansion towards gold (`lateOres`) only once the build order is fulfilled up to the mine.
  - Enemies: all players except allies (`IsEnemy` → `World.AreAllies`, teams from the start screen) – it leaves their soldiers, buildings,
    piles (thief) and spell targets alone.
  - `AiPlayer.Military`: target garrison full (`Staff`), knows all enemy soldiers, re-weighs every second. Soldiers on a
    simple move order and on (forced) retreat also count as idle – retreating ones are immediately free for defence/guerrilla/campaign, otherwise keep walking
    (guarding does not redirect them). Defence first; guerrilla against smaller troops (also geologists, `geologistHealth`) with clear
    superiority; enemies standing around no deeper than `military.hinterlandDepth` in their land (Skirmisher 8);
    enemy soldiers within `military.reinforceRadius` (30) of the target count too (scattered ones come to help); no guerrilla where tower bowmen
    make up more than `military.towerFireRatio` (0.5) × the target strength – checked from the centre of the attackers (a running one is broken off if the enemy lures
    them under the towers); an attack on the land = fighting or a move target outside own land; after a break-off the target is left alone for 30 s; guerrilla before holding back
    (defenders may attack as long as enough remain against other troops). The rest: the trait `guardByThreat` guards by threat
    (without it: stay put); without a threat, tower distribution: `frontTowers` (Skirmisher), `everyOtherTower` (General, Legion),
    `rotatingTowers` (Unyielding: 30 %, redrawn every minute), Hothead: one heap.
  - `AiPlayer.Campaign`: gathers soldiers, conquers enemy military buildings at 1.5:1 (garrison weighted by `attack.garrisonWeight`, castle double).
    While gathering, gives up only below 85 % of the break-off threshold; an abandoned/vacated target is left alone for 60 s.
    Values: `attack.armyRatio` (0 = irrelevant), `attack.commit` (share of free soldiers, 1 = all), `attack.abortRatio` (0 = never give up),
    `attack.maxDistance` (0 = irrelevant), `military.retreatRatio` (0 = never back).
  - `AiPlayer.Assault` (cheat `/attackme`, `AssaultOn`, saved): instead of strikes/campaign/defence/hospital, every military tick garrisons down to 1,
    all other soldiers (new ones too) against the player's military building nearest to their centre (`Military.Attack`; bowmen and, without
    military buildings, everyone via `Move` to the door), never a retreat (no `TendWounded`); those fighting and those already sent against the player stay on it; priests follow.
    Ends when the player has no building left; never allies.
  - `AiPlayer.Challenge` (AI chat, `ChallengedBy`, saved): at the next military tick a campaign only against this player, without
    `armyRatio`, `maxDistance`, timing traits and a free path (even if own land is under pressure); 1:1 against the defence is enough,
    at least half of the remaining free fighters join; after that a normal campaign. Not during a campaign/assault, never allies.
    `CanTakeUpChallenge`: the same target search as a dry run (for `cannotAttackSpeaker` in the situation).
  - `AiPlayer.Superiority` (`superiority` in `ai.json`, all characters; against opponents out of reach on large maps): from `startMinutes` (60)
    every `intervalSeconds` (300) a crude comparison of the whole armies (all soldiers × combat strength); against the weakest enemy with
    ≥ `armyRatio` (2):1 a campaign as with the challenge (without distance, free path, timing traits), but with `ratio` (2) × the defence
    of the target; if no target is found, the next weakest. Not with enemies on own land, a running campaign/assault, or a truce.
  - Allies in all power comparisons (`AiPlayer.Allies`): the enemy side counts all enemies anyway; on the own side
    allied soldiers outside (`AlliedStrength`: campaign/hospital retreat within `attack.defenseRadius` around the target, strike within
    `reinforceRadius`, holding back/`forwardBase` within `threatRange`) and bowmen of allied military buildings in range count too –
    this is how the AI joins its allies' battles and campaigns. `armyRatio` and `superiority` compare team against team (`TeamArmy`).
  - `AiPlayer.Diplomacy` (AI chat, saved, lazy for old saves): `Truce(player, ticks)` ends campaign/assault/challenge
    against them (soldiers home), breaks off guerrilla strikes against them, no new ones until it expires; defence on own land remains, their
    soldiers on own land break it, `Challenge`/`Assault` end it. `Help(ally, ticks)`: half of the free soldiers (at least 1,
    no longer below `IdleSoldiers`) to the strongest enemy group in the ally's land, otherwise to the ally's military building nearest to an
    enemy building; updated every military tick, home after expiry / when the ally has no buildings. `CanHelp` → `cannotHelpSpeaker`.
    Situation for the chat: `AiSituation` (compact JSON, zeros/false omitted, per player: people, character, army, `powerVsSpeaker`, `distanceToSpeaker`, enemies in the land, land,
    buildings, battles, trend of the last 5 min from `GameHistory`, `doing`; plus the last 16 chat lines).
  - `AiPlayer.Tactics`: in small engagements (strike/campaign ≤ `military.wounded.maxSoldiers` 10) fighters below `withdrawHealth` (35 %)
    withdraw (forced, hold position `restDistance` from the enemy in the direction of `RetreatDoor`; pursued → to the building; an own hospital within calling range, not
    closer to the enemy: released to its door instead), as long as the rest has `holdRatio` × the opponents;
    back at `rejoinHealth` (healed), below `needRatio` × the opponents or when the opponents are almost defeated (`mopUpRatio`); no renewed retreat for 20 s. Hothead: never.
    Hospital (`hospital` in `ai.json`): idle wounded (< `woundedHealth` 80 %) to the nearest own hospital up to `sendDistance` (60): the weakest
    onto the healing spot, the others wait 3 fields in front of the door (otherwise they push each other away and the healer never comes out); more than `wounded` (5) of them (not counting those still fighting) further than `sendDistance` from every hospital
    (construction sites included) → a hospital ahead of the build order (like residences) at the military building nearest to them; at most `max` (3), none within the radius
    `spacing` (120) of another one (construction sites included).
    Late game = large army (`army.lateSoldiers` 20 soldiers outside the buildings; too large to withdraw individuals): weaponsmith shares
    `lateShares` (spear 100 %, bow 80 %, sword 15 %); below ¾ of that, the shares from before.
    Strikes pursue all parts of disintegrated target groups (nearest first).
  - `AiPlayer.Magic`: priests follow attacks at a distance, cast from `magic.minTargets` targets not yet hit (enemies near own
    fighters/strike/campaign soldiers, also bowmen and reinforcements; frozen ones count again shortly before thawing), several at once
    at different spots, mana of running spells reserved. Frozen ones count for nothing in the power comparison, again for more as they thaw (`Strength`).
    Turning the tables (`TurnWithSpell`): instead of breaking off a strike, giving up or retreating from a campaign, first the people's spell (not haste), as long as
    `magic.turnRatio` (0.6) of what is needed is still there. Upgrades immediately with enough mana
    (`magic.upgradeOrder`: swordsmen, bowmen, pikemen). The AI only casts the people's spell.
  - Characters/traits (concept: `docs/KI-TACTICS.md`; ids in English, German names in `ui.json` `aiCharacters`/`aiTraits`):
    a character (`AiCharacter`, `characters` in `ai.json`) names traits (`AiTrait`, `Ai/Traits/<Id>Trait`; defaults under `traits`,
    overridable per character) and overrides values; `characterWeights` (per people) draw one per match.
    Decision points: `LeadCampaign` (troop command), `LeadStrike` (engagement command, `StrikeLeader`), `Guard` (guarding), `AllowCampaign` (timing), `GarrisonLimit` (garrison),
    `ArrangeBuildOrder` (build order, once at the start), `RunErrands` (errands, after guarding).
    Traits: `waves` (calm/waves in alternation), `frontGarrison` (only buildings near the front full, otherwise `backline`), `earlyArmy` (smelter,
    weaponsmith, barracks directly after wood/stone; smelter even without a mine if there is ore on own land), `forwardBase` (gathering at the military building
    nearest the front, retreat without a fight from stronger ones), `pincer` (General: feint from the gathering point, main group in an arc to the flank),
    `thief` (Skirmisher, Hothead: `scouts` scouts steal piles at the edge of enemy land, `ranks` gold/weapons/ores > tools > food > the rest;
    not with enemy soldiers within `soldierRadius`, otherwise called home, pile avoided for `retrySeconds`).
    `summoner` (Legion: mana beyond `reserveSpells` × the people's spell → "Himmlische Geschenke" by a free priest on own land
    near them, `edgeDistance` from the edge, without enemy soldiers, with `minFreeFields` free fields).
    `formation` (Legion, General, Skirmisher: a strike with bow + melee against enemies on foot, still out of range – bowmen forward to
    range − 2, melee fighters hold `parkDistance` behind; enemy ≤ `fallBackDistance` from bowmen → these hold behind the melee fighters,
    who go in; after that again every `fallBackSeconds`, bowmen without a target move up; in at the latest after `screenSeconds`).
    `buildOrderMoves` moves buildings in the build order.
    People characters (`standard`/`general` only via `--characters`): `hothead` (Egyptians: `earlyArmy`, everything attacks, thin hinterland, steals),
    `unyielding` (Asians: only gives up below half the defence strength), `legion` (Romans: early gold, waves, pincer, goods summoner),
    `skirmisher` (Amazons: campaigns only at 2:1, small troops, nearby targets, steals).
    Ad hoc (Arena, tournament): traits with `+` (`skirmisher+forwardBase`, appended ones before its own), values with `@path=value`
    (`general@attack.ratio=1.3@buildOrderMoves.barrack=-5`).
    A trait's notes start with its id and appear live (`AiPlayer.Noted`, `TraitOfNote`).
    Values in `ai.json`, overridable per people (`civilizations`, where the build order is too).
