# AI tactics and characters

A collection of ideas for making the computer opponents more challenging, more tactical and more surprising.
The basis is the existing AI (`src/Homestead.Simulation/Ai/`, values in `data/ai.json`).

## Terms

A uniform vocabulary – in discussions, docs, code and data:

| Term | English (code, `ai.json`) | Meaning |
|---|---|---|
| **Charakter** | character (`AiCharacter`) | *how* an AI plays: a list of traits together with their values |
| **Charakterzug** | trait (`AiTrait`, `Ai/Traits/`) | a self-contained building block of behaviour, attached to decision points |
| **Entscheidungspunkt** | decision point (method in `AiTrait`) | a fixed point in the AI's thought process at which traits may decide |
| **eingebautes Verhalten** | built-in behavior | what the AI does without a trait (economy, defence, guerrilla, campaign, garrison) |
| **Schwierigkeit** | difficulty | *how well* an AI plays (Easy, Medium, Hard) – independent of the character |
| **Kennung** | id | English identifier in `ai.json` and code, e.g. `pincer`, `general` |
| **Name** | – | German display name in `data/ui.json` (`aiCharacters`, `aiTraits`), e.g. "Zange und Finte", "Feldherr" |

## Core idea: traits and characters

The AI remains a player with the same commands as a human. New behaviour, however, is not
hard-wired into `AiPlayer`, but comes as a **trait**: a self-contained building block with its own
id, its own values and a clearly defined place of use. A **character** is a list of
traits together with their values. Every people gets one or more characters; one of them is chosen
per match.

```
People ──(one of them per match)──> Character ──> Traits (+ values)
```

### What remains the foundation

Not everything should be switchable. Without the following basic behaviour an AI would be broken, not different:

- Economy following the build order, housing, expansion towards raw materials
- Defence first (enemies on own land are always fought)
- No demolition of a military building whose land carries own buildings (since demolition leaves land
  unprotected, the AI would otherwise make itself vulnerable)
- No demolition of the last finished garrison – without it the game is lost (`ConquestGoals`);
  the AI never asks for it.

The built-in behaviour (guerrilla, campaign at 1.5:1, full garrison) is the character **Standard**
(`standard`, without traits). Guarding by threat is already a trait
(`guardByThreat`) and switched off for Standard: its idle soldiers stay where they are.

### Decision points

Traits attach to fixed points in the AI's thought process. Each point defines how several
traits interact:

| Decision point | When | Interaction of several traits |
|---|---|---|
| **Garrison** (`GarrisonLimit`, implemented) | every military round, per own military building | traits limit the garrison; the lowest limit applies (at least 1) |
| **Attack targets** | planning of a campaign | traits rate targets (points add up), the best target wins |
| **Troop command** (`LeadCampaign`, implemented) | during a campaign | one leads the troop (e.g. pincer *or* ambush) |
| **Engagement command** (`LeadStrike`, implemented) | start of a strike (guerrilla, defence), then every military round | one leads the strike (e.g. battle formation); without it everyone goes in at once |
| **Guarding** (`Guard`, implemented) | every military round, for the remaining idle soldiers | the first one to position them decides; without it they stay where they are |
| **Tower spots** | expansion | traits rate building spots, points add up |
| **Timing** (`AllowCampaign`, implemented) | every military round, before planning a campaign | every trait can hold back a new campaign |
| **Spells** | priests at the battle | one decides spell and moment |
| **Build order** (`ArrangeBuildOrder`, implemented) | once at the start | every trait rearranges the build order the previous one left behind |
| **Errands** (`RunErrands`, implemented) | every military round, after guarding | every trait sends out its own people (e.g. scouts to steal) |
| **Thresholds** | wherever a ratio decides | modifier on values such as `attack.ratio` |

### Distributing soldiers

Several traits want the same idle soldiers. Fixed order:

1. Defence (basic behaviour)
2. troops already under way keep their soldiers
3. traits by their priority in the character (named first = first)
4. the rest: guarding (if the character has it)

A trait takes only what it needs and returns soldiers when its plan ends.

### Data (`ai.json`)

```json
"traits": {
  "pincer": { "minSoldiers": 4, "feintShare": 0.35, ... }
},
"characters": {
  "standard": { "traits": [] },
  "general":  { "traits": ["pincer", "guardByThreat"], "attack": { "ratio": 1.2 }, "military": { "strikeRange": 40 } }
},
"characterWeights": [["standard", 1]],
"civilizations": {
  "Asian": { "characterWeights": [["trickster", 2], ["rusher", 1]] }
}
```

`traits` contains the defaults per trait. A character can override them under the trait's
id, and also any general value (after the people's value). The weights
(`characterWeights`, overridable per people) determine how often a character is chosen.

### Code

- Base class `AiTrait` with overridable methods per decision point (`LeadCampaign`, `Guard`;
  more come with the traits that need them).
- Each trait in its own file under `Ai/Traits/` (`<Id>Trait`, e.g. `PincerTrait`).
- At every decision point `AiPlayer` asks the traits of its character.
- Client and Arena: `--characters ,general` sets the character per player.
- A trait's notes start with its id; client and Arena show them immediately,
  the client with the German names of character and trait.

## Traits

Status: all **open**. Rough effort: S (small), M (medium), L (large).

### Guarding

**`guardByThreat` – Guard by threat** · **implemented** (with `general`, not with `standard`)
Idle soldiers guard the own military buildings, distributed according to the strength and proximity of enemy
troops (`military.threatRange`); without a threat they gather at the home tower. Whoever stands within
`military.guardDistance` of their post stays. Decision point "Guarding" (`Guard`).
Without this trait, idle soldiers stay where they are.

**`forwardBase` – Forward base** · M · **implemented**
Idle soldiers gather at the own military building closest to an enemy one, as a launch point
for guerrilla. If the enemy troops within `military.threatRange` are stronger (`fallBackRatio`), they
fall back into own land without a fight (a building at least `fallBackDistance` fields further away from the
enemy, otherwise the home tower). If the enemy pushes further into own land, defence and guerrilla
fall on its rear. Together with `frontGarrison` (thin hinterland, more soldiers free) a staging post
for raids. Stands before `guardByThreat` in the character (both at the decision point "Guarding").

**Distributing towers** · **implemented**
Without a threat (no enemy soldiers within `military.threatRange` of own military buildings) these
traits distribute the idle soldiers evenly over selected towers; under threat
the next trait (`guardByThreat`) takes over, which is why they stand before it in the character:
- `frontTowers` – Towers to the front (`skirmisher`): towers from which the nearest enemy
  military building can be reached without a detour (path at most `detour` × straight-line distance + 5) and no own
  military building stands in between.
- `everyOtherTower` – Every other tower (`general`, `legion`): every other one, counting from the front – a tower
  is dropped if a neighbour (at most `neighborFactor` × the distance to the nearest tower) has already been chosen.
  Larger groups that quickly reach the skipped neighbours.
- `rotatingTowers` – Rotating towers (`unyielding`): a random `share` (30 %) of the towers, redrawn every
  `rotateSeconds` (60) (fixed seed of the world).
The `hothead` keeps everything in one heap (no such trait).

The AI re-weighs military orders every second (`military.thinkSeconds` 1): soldiers on a simple
move order from the AI (to their post, home) count as idle, campaigns are already abandoned on the way to the
gathering point if the defence has become too strong. Guerrilla also hits enemy
geologists (one soldier is enough). The target of a guerrilla also includes enemy soldiers within `military.reinforceRadius` (30) – slightly
scattered ones come to help; without this, early guerrillas almost always failed. Tower bowmen are
not opponents to be defeated but a cost: guerrilla (not defence) is skipped where they make up more than
`military.towerFireRatio` (0.5) × the strength of the enemies there (a single soldier next to a
full castle otherwise cost three men for one); if the enemy retreats under the towers during a guerrilla,
the AI breaks off. The check is made from the centre of the chosen attackers, not from the nearest one
(it was already standing behind the castle, the others walked past it). After a break-off the same
enemies are left alone for 30 s (otherwise: attack, break-off, attack … every few seconds). A soldier who only walks in
their own land (a recruit to the gathering point) does not attack the land – `hinterlandDepth` applies to them. `military.hinterlandDepth` (`skirmisher`: 8) distinguishes attacks on
land (enemies walking or fighting – attacked everywhere) from enemies standing around, who are only attacked
if they stand at most this deep inside their owner's own land. The strength of a target
also includes the bowmen of enemy military buildings that reach the path or the target (a
soldier next to a fully garrisoned castle is not alone); likewise, for campaigns, the bowmen of
neighbouring buildings. Whoever retreats walks to the nearest own military building that is not closer
to the enemy – not all the way home.

### Garrison

**`frontGarrison` – Garrison by front** · M · **implemented** (character `hothead`)
Military buildings in the hinterland get 1 soldier, buildings on the border with the enemy are filled up, there with
bowmen. Result: considerably more soldiers free for attacks.
Values: `frontDistance` (distance to an enemy military building from which a building counts as "front"; it stays front up to `frontDistance + 10` without an enemy military building, so that the garrison does not flicker), `backline` (garrison in the hinterland).

**`reinforceThreatened` – Reinforce early** · S
If an enemy troop stands within range of a border tower, its garrison is immediately set to full
before the attack begins.

**`rotateWounded` – Rotate the wounded** · M
Battered soldiers return into a tower, fresh ones come out.
*Prerequisite:* soldiers currently do not heal. Only sensible once they regain health inside the building
(our own game rule, the same for human and AI).
*Built in instead of a trait* (`military.wounded`, `AiPlayer.Tactics`): in small engagements (≤ `maxSoldiers`
own soldiers) badly wounded soldiers withdraw from combat as long as the rest holds out, and hold
out of the enemy's sight with "Position halten"; if an own hospital lies within calling range, the AI instead releases them
and sends them to its door (the hospital does not call those holding position).
The AI also sends idle wounded to the nearest hospital (`hospital.sendDistance`): the weakest onto the
healing spot (whoever stands there is healed without being called), the others wait a few fields in front of the door – in a crowd at the
door they would push each other away and the healer would never come out. It builds hospitals when needed, like residences: more than `hospital.wounded` (5) wounded outside
the range of all hospitals → one at the military building closest to them. They come back when they are healed,
are needed or the enemy is almost defeated. The `hothead` never withdraws (`withdrawHealth` 0).

### Troop command

**`formation` – Bowmen in front, melee fighters behind** · M · **implemented** (`legion`, `general`, `skirmisher`)
Decision point "Engagement command" (`LeadStrike`, strikes = guerrilla and defence). If a strike has
bowmen and melee fighters, the enemies are mainly on foot (their bowmen at most
`maxEnemyBowShare` of their strength) and still out of bow range, the bowmen go forward and shoot
from range; the melee fighters hold `parkDistance` fields behind, out of the enemies' sight. If an
enemy comes within `fallBackDistance` of a bowman, the bowmen fall back without a fight behind the
melee fighters and shoot from there, and the melee fighters go in; likewise in the further fight (at most
every `fallBackSeconds`, bowmen without a target move up in between). After `screenSeconds` the
melee fighters go in regardless. Bowmen and parked melee fighters use "Position halten" like the
player (`Military.Hold`, `Move(…, hold: true)`): only enemies in range, no pursuit, not idle –
so not called by buildings as a garrison.

**`pincer` – Pincer and feint** · L · **implemented** (character `general`)
Two troops: a small one lures the defenders out at one spot, the main attack comes shortly
afterwards from the other side. For this the campaign gets two gathering points.
Implementation: `Ai/Traits/PincerTrait.cs`. The feint (`feintShare`) gathers at the own building closest to
the target, the main group walks via a waypoint in an arc to a staging point
(`flankAngle`, `flankDistance`, out of range of the target's bowmen); both ready → the feint
attacks, the main group `strikeSeconds` later. Takes `extraShare` additional soldiers along, only gives
up below `retreatRatio`. The General already attacks at 1.2:1 and leaves guerrilla far from
own land alone (`strikeRange` 40), so that soldiers remain for campaigns.
Observed (Salomons, 3 players): from about minute 37 several pincers; the opponent often pulls soldiers
together, then the General breaks off – intended behaviour.

**`flanking` – Ambush by outflanking** · L
A small troop walks in a wide arc around the opposing camp, outside the sight of enemy
soldiers and the range of bowmen in towers, and attacks from behind. Targets are weakly
garrisoned buildings in the hinterland. If one is conquered, it takes land, and the opponent's
buildings on it fall (rule "unprotected land"). A human player does not expect that.
Values: troop size, minimum distance to the enemy on the detour, highest garrison of a target.
If the troop is discovered (enemies come into sight), it attacks immediately or withdraws.

**`spellTiming` – Spells at the right moment** · M
Freeze immediately before the clash, scatter on clumps in front of a tower, convert on
the strongest opponents in range. Today the AI casts as soon as enough targets stand within the radius.

**`summoner` – Goods summoner** · S · **implemented** (`legion`)
Mana beyond `reserveSpells` (2) spells of the people goes into "summon goods". A free priest casts
on the best spot within `priestRadius` (12) around themselves: own land, at least `edgeDistance` (10)
fields from its edge (light fingers, enemies; the map edge does not count), no enemy soldier outside within
`soldierRadius` (20), at least `minFreeFields` (30) free fields within the spell radius; the most free
fields win, then the one nearest to the priest. Only as long as no priest of the player is casting; checked every
`checkSeconds` (5). Upgrades cost no mana and remain untouched. Decision point "Errands".

### Exploiting weaknesses

**`exposedTowers` – Counter exposed towers** · M
If the opponent pulls soldiers out of their towers for an attack (down to one), exactly
these towers are attacked immediately. Rates targets by the current garrison, not by the normal garrison.

**`landGrab` – Land grab without a fight** · M
Prefer tower spots whose territory covers unprotected opposing land (e.g. after a
demolition). As soon as the tower is garrisoned, the opponent's buildings on it fall.

**`economicTargets` – Hit the economy** · S
Rate attack targets higher whose land carries mines, smelters, weaponsmiths or temples.
A hit there costs the opponent more than any old tower.

### Scouts

**`thief` – Light fingers** · M · **implemented** (`skirmisher`, `hothead`)
Keeps `scouts` (2) scouts – free carriers become scouts, losses are replaced after `replaceSeconds` (90)
at the earliest – and sends them to goods piles at the edge of enemy land: on foreign land at most
`edgeDepth` (12; the piles of a storehouse right at the border lie a good 8 fields deep) fields from its edge, at most `maxDistance` (30) from own land. Goods by `ranks`: gold, gems, weapons, ores first, then tools, then food (bread, fish,
meat), then the rest; at the pile the scout takes the best good within the thieving radius. If an
enemy soldier stands outside within `soldierRadius` (12, a little more than `scouts.revealRadius`) of the pile,
no attempt is made; if one comes that close to the scout or their pile, the AI calls the scout home and leaves the
pile alone for `retrySeconds` (60). Loaded scouts walk to the nearest field of own land and drop the
good there. Decision point "Errands". Risky against an AI: it becomes suspicious as soon as
a scout stands on its land, and sends a soldier, who recognises the scout from close by.

### Gold

See [Gold as a lever](#gold-als-hebel). Three traits, each at a decision point of its own.

**`goldRace` – Secure gold early** · M
Gold mountains (Egyptians: gems) count as an expansion target from the start, not only after the
build order (`lateOres`). Gold mine and gold smelter move forward in the build order as soon as food
and tools are running. Values: from which step of the build order, weight of contested gold mountains.
Decision points: tower spots, building.

**`goldGuard` – Protect gold** · M
Land on which own gold lies (storehouse, output pile of the smelter) counts as front – even in the
hinterland: military buildings there fully garrisoned, with bowmen, guards by the amount of gold instead of
only by threat. A new storehouse only on land covered by several own towers. Optionally distribute gold over
several storehouses so that one hit does not cost everything (trade-off: more places to defend).
Decision points: garrison, guarding, storehouse building spot.
Recommended on Hard in every character – without it a single conquest can finish off the AI.

**`goldRaid` – Capture gold** · M
Attack targets whose land carries opposing gold get the highest rating: the conquest
takes combat strength away from the opponent and gives it to the attacker. Worthwhile even with a narrower superiority
(own value for `attack.ratio`), because the gain shifts the balance of power immediately.
Decision point: attack targets.

### Timing and strategy

**`earlyArmy` – Early army** · S · **implemented**
The first entries for iron smelter, weaponsmith and barracks move together directly behind the
first `after` entries (wood and stone). The starting goods (iron ore, coal at medium and high
start level) become soldiers immediately instead of waiting for mines and food. Only worthwhile for a character that actually
uses the early soldiers: with the cautious Skirmisher it cost economy (against Standard
0.43 instead of 0.48), with the Hothead it brought 0.56 instead of 0.52. `forwardBase` also weakened the
Skirmisher (0.39): its soldiers stood at the front within reach of the enemy guerrilla. In return a smelter may
also come without an own mine if its ore lies on own land (applies to everyone).

**`earlyRush` – Early rush** · M
Attack a nearby opposing tower early with the starting soldiers, before the opponent has barracks and
weapons. Normal economy afterwards.

**`powerSpike` – Attack after an upgrade** · S
Hold back attacks until an upgrade is finished or enough gold for high combat strength is in storage,
then strike in a concentrated blow. Collect gold specifically for this.

**`waves` – Phases of tension** · S · **implemented** (character `legion`)
Alternation between calm build-up phases and attack waves instead of steady pressure. Values: `calmSeconds`,
`waveSeconds`, `jitter` (spread). Defence and guerrilla continue during the calm phase.

**`multiFront` – Pressure at several points** · M
Two or three troops threaten different border sections at the same time. The defender has to
split up; the attack comes where they are thinnest.

**`jitter` – Spread thresholds** · S
Fixed ratios (`attack.ratio`, `strikeRatio` …) are rolled within a range at every decision
(e.g. 1.3–1.8 instead of 1.5). The behaviour becomes harder to predict.

### Rejected

**Focus fire** (several soldiers on the same target, wounded and bowmen first): the player
cannot order individual units to attack a specific target. The AI should not get any control that
the human lacks.

## Adjustments without a trait

Many differences need no trait of their own, only different values in the character:

| Value | Effect |
|---|---|
| `attack.ratio` | superiority at the target from which a campaign starts |
| `attack.armyRatio` | superiority of the whole army (default: `ratio`; 0 = irrelevant → small campaigns against weak towers) |
| `attack.commit` | share of the remaining free soldiers that additionally joins (1 = full strength) |
| `attack.abortRatio` | after gathering: break-off below this ratio (default: `ratio`; 0 = never) |
| `attack.maxDistance` | target at most this far from own land (0 = irrelevant; a border tower stands a good 30 fields behind its land border) |
| `attack.minSoldiers`, `attack.defenseRadius` | smallest troop; how far around the target enemies count towards the defence |
| `military.strikeRange`, `military.strikeRatio` | guerrilla: how far out, from which superiority |
| `military.retreatRatio` | retreat below this (0 = never) |
| `buildOrderMoves` | move buildings in the people's build order, e.g. `{ "barrack": -6 }` |
| `maxSites` | simultaneous construction sites |

To try things out without an entry in `ai.json`: `pincer+waves` (only these traits) and
`@path=value` (`general@attack.ratio=1.3@buildOrderMoves.barrack=-5`). The tournament
(`tools/trait-tournament.py`) varies values with `--vary attack.ratio=1.2/1.5/2`.

## People characters

Goal: one character per people that feels distinctive as an opponent. Defined in `ai.json`, assigned per people via
`characterWeights` (under `civilizations`).

| People | Character | Idea | Implementation |
|---|---|---|---|
| Egyptians | Hothead (`hothead`) | hot-tempered, always attacks at full strength | earlyArmy, frontGarrison, thief; `ratio` 1.2, `commit` 1, guerrilla at 1.2:1 up to 120 fields (with earlyArmy against Standard 0.56 instead of 0.52, decides more matches) |
| Asians | Unyielding (`unyielding`) | never gives up, never retreats | guardByThreat; `abortRatio` 0.5, `retreatRatio` 0.5 (at 0 it ran itself to death: 0.35 points against Standard, with 0.5: 0.50), `ratio` 1.4 |
| Romans | Legion (`legion`) | economically strong, attacks in concentrated waves | waves, pincer, guardByThreat, summoner; `minSoldiers` 8, `commit` 0.6; gold 10 places earlier; 5 construction sites |
| Amazons | Skirmisher (`skirmisher`) | plays it safe, weakens the opponent with small guerrillas and theft at the border | guardByThreat, thief; campaign only at 2:1, but without the army comparison from 2 soldiers and only up to 50 fields from own land (`maxDistance`; measured from the nearest tower it never attacked, because two towers on a shared border already stand a good 60 fields apart; without it, it attacked a tower right across the map in minute 1 and then lost everything); guerrilla up to 150 fields at 2:1 (at 1.3:1 with retreat below 1.2 the small troops turned back at the first sign of danger), retreat below 1:1 |

Guideline: play style, variety and fun before exact balance. Every people should play recognisably
differently as an opponent, with strengths and exploitable weaknesses. Observed (Arena, Amazons against
Egyptians, 10 matches): the Hothead overruns small maps in 20–30 minutes; if the rush fizzles out,
it bleeds to death (in the end 3–33 against 100–220 soldiers). Against Egyptians, then, the task is to survive early; against
Amazons, attack early or prepare for a hard late game.

## Characters (draft)

| Character (id) | Idea | Traits |
|---|---|---|
| Standard (`standard`) | built-in behaviour | – (guerrilla, campaign, full garrison are built in) |
| Rusher (`rusher`) | attack early and often, a smaller superiority is enough | earlyRush, frontGarrison, waves |
| General (`general`) | orderly battles | pincer, formation, guardByThreat (implemented); planned: spellTiming, reinforceThreatened |
| Trickster (`trickster`) | strike where nobody is looking | flanking, exposedTowers, goldRaid, frontGarrison, jitter |
| Land taker (`landTaker`) | grows across borders and takes land without a fight | landGrab, frontGarrison, economicTargets |
| Merchant (`merchant`) | build up calmly, then strike with strong soldiers | goldRace, goldGuard, powerSpike, spellTiming |

## Difficulty and character

Two independent axes:

- **Character** = *how* the AI plays (style, traits).
- **Difficulty** = *how well* it does so (perception, reaction, quality of decisions).

A Rusher can be easy or hard. The best AI (Hard) is built; easier levels
get targeted cutbacks.

### Adjustments per level

| Adjustment | Easy | Medium | Hard |
|---|---|---|---|
| Perception | own land, scouts, geologists | what it sees, plus the location of mountains and stones | everything |
| Scouts/geologists | yes, to find raw materials | yes, to find opponents | no |
| Reaction | only to attacks on own land | to discovered enemies nearby | to all enemies nearby, anticipating |
| Positioning soldiers | anywhere in the land | at border towers facing enemy land | at border towers facing enemy troops |
| Guerrilla | no | yes | yes |
| Traits | few, simple ones | those for combat | all of the character's |
| Thresholds | rather hesitant or reckless | standard | precisely calculated |
| Thinking pause | long | medium | short |

Traits can themselves weaken by level (e.g. `flanking` on Easy without regard for
sight range), instead of only being switched on or off.

### Hard must win, not just hold

An impenetrable defence makes the AI hard to *beat*, but not hard to *play against*:
the human only has to bunker down too, and the match ends in stalemate. Hard therefore also needs
a path to victory:

- **Play out the economy:** distribute better, gold earlier, secure raw materials faster. Against an
  equally strong defence, whoever sends in more and stronger soldiers wins.
- **Overstretch the defender:** pressure at two or three points at once. Even a human cannot
  be everywhere; the AI attacks where they are not defending at the moment.
- **Patience, then one blow:** wait until combat strength (gold) and upgrades are clearly ahead
  (`powerSpike`), then attack in a concentrated blow where a conquest drags the economy down with it
  (`economicTargets`).
- **Magic as a breakthrough:** freeze, scatter, convert deliberately when storming a tower.

<a id="gold-als-hebel"></a>
## Gold as a lever

In the game, gold exists precisely to break the defender's advantage and to force the offensive.

- **Rule:** in enemy land a soldier fights at 100 % / number of players. Gold bars on own land
  raise this without an upper limit, ever more expensive per step. With two players: 115 bars for 100 %,
  305 for 130 %, after that 10 per point, 1005 for 200 % (Egyptians: twice as many bars or half as
  many gems; `combatStrength` in `military.json`). Above 100 % a soldier hits harder away
  than at home – whoever is rich is pushed into attacking.
- **Defending needs no gold, attacking does.** Whoever has more gold can attack; whoever has none
  can only hold.
- **Gold belongs to the land:** what counts are the piles on a player's land, no matter who
  put them there. If a player conquers the land around an opposing storehouse, the gold there belongs to them
  (intended). The opponent loses combat strength, the victor gains it – the match can thereby be
  decided at once.
- **Consequences for the AI:**
  - Gold is a strategic target from the start (`goldRace`), not an afterthought at the end of the build order.
  - Own gold is the most valuable possession and is protected like the front (`goldGuard`).
  - Opposing gold is the best attack target (`goldRaid`).
- **Risk:** the avalanche effect is strongest here. It is intended that a successful blow against the
  gold turns the match; it would be bad if that happened without a real chance to fight back. Therefore
  gold must lie visibly and defensibly – the same for human and AI.

## Dynamics and stalemate

### Why matches end in stalemate today

- **Defender's advantage:** 100 % combat strength on own land, 100 % / number of players in enemy land. With
  two players an attacker needs roughly twice the army; the AI only attacks at 1.5:1.
- **Omniscient AI that reacts instantly:** guards stand according to threat. Whoever gathers is
  seen before attacking.
- **The same calculation on both sides:** two identical AIs hold each other back.
- **Hardly any weaknesses:** the AI rarely stalls (smelter only with an own mine, material for the
  stonecutter held back …). In the original it was precisely such hang-ups that decided matches.

### What can turn a match (intended)

- **Gold race:** gold raises combat strength in enemy land – the built-in way out of the stalemate.
  Whoever first gets close to 100 % can attack; above that they have to. Gold mountains become a contested target.
- **Raw materials run out** (`depletion`): whoever does not reach new raw materials slowly loses. If
  they lie between the players, combat becomes necessary.
- **Conquest with consequences:** if a tower falls, the buildings on its land fall. A hit on a
  mine, smelter or weaponsmith paralyses a whole chain.
- **Magic:** turns the balance of power without a doubled army.
- **Different characters:** a Rusher against a Merchant takes a risk; one of them
  wins early or burns out.
- **Intended weaknesses per character:** the Land taker is thinly garrisoned everywhere, the Rusher
  neglects the economy. Something to exploit – recognisable rather than random.

### What can spoil a match

- **Too fast an avalanche effect:** an early conquest topples half the economy; the match is decided in
  minute 15 but still lasts another hour.
- **Bunkering:** if defending becomes even cheaper, both only build towers and nothing happens.
- **Omniscience feels unfair:** a human's ambush never succeeds. Remedy via the
  perception of the difficulty levels.
- **Random hang-ups:** if the AI stalls because of a bug, luck decides instead of play.
- **The map decides:** if gold is the only lever, whoever happens to have the gold mountain wins.

### First steps

1. **Measure:** Arena, 5–10 matches AI against AI of 60 minutes each; record combat strength, gold, conquests and
   losses. How often does it end in stalemate?
2. **Gold earlier and deliberately:** head for gold mountains (`goldRace`), protect gold (`goldGuard`),
   hoard gold for combat strength and then strike (`powerSpike`, `goldRaid`).
3. **Limit perception** (levels Easy/Medium): reaction only to troops that have been seen.
4. **Defender's advantage:** only touch it with Arena numbers, e.g. the penalty only in full deep in enemy land,
   weaker at the border.

## Procedure

1. ~~Framework for traits~~ **implemented**: characters in `ai.json`, base class `AiTrait` (decision points
   as overridable methods, so far `LeadCampaign`), today's behaviour = character `standard`
   without traits. More decision points come with the traits that need them.
2. Extend the Arena with stalemate metrics (combat strength, gold, conquests, losses over time).
3. Implement traits one at a time, each with a test and an Arena comparison against Standard; first those that
   break the stalemate (`goldRace`, `goldGuard`, `goldRaid`, `powerSpike`, `economicTargets`, `multiFront`).
4. Difficulty as the second axis: perception, reaction, thresholds, permitted traits.
5. Assign characters to the peoples.
