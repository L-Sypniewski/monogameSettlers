# Client (`src/Homestead.Client`)

MonoGame game. Capture options: [capture.md](capture.md).

- **Title screen** (`TitleScreen`, shown first without `--map`/`--capture`): main menu image of the Gold Edition (`titleScreen` in `ui.json`, file 61 GUI 1,
  smooth gold next to it), on the left engraved buttons as in the original (sizes/colours from the original menu): at the very top Continue (loads `SaveSlots.Latest`, pale without a save game), set apart Campaign, Mission CD, Amazon campaign (each → campaign),
  set apart Single player (→ start screen), Multiplayer (→ `MultiplayerScreen`, see `docs/MULTIPLAYER.md`), Settings (→ settings), Load game
  (list of the 8 slots as a `ListScreen`: name, date, people; empty ones pale; a click loads, load errors above the title), set apart Readme/Contribution/Licenses (popup on the
  original message box file 02 GUI 16, `popup` in `titleScreen`: "Im Browser öffnen" → `README.md`/`CONTRIBUTING.md`/`THIRD_PARTY_NOTICES.md` on GitHub, Enter/Esc),
  set apart at the bottom Quit; Esc/"Zum Hauptmenü" of the statistics/Esc in the start screen lead here.
- **Loading screen** (`LoadingScreen`, `MapGame.ShowLoading`): before every loading of a match (start screen, mission, save game) the three helmets,
  at least 1 s (real time, from the first draw); the loaded game is already drawn hidden underneath (`WarmFrames`, 3 – the first frames fill atlases, otherwise the fade stutters), then 0.3 s fade-out over the running game (from the first blend frame) (`loadingScreen` in `ui.json`, 1024 × 768, edge columns stretched to the sides): campaign orange (file 02 GUI 28), Mission CD green
  (60/0), Amazons purple with torso (48/0), otherwise gold (61/0); theme other than Normal: always its image; captures load immediately.
- **Campaign** (`CampaignScreen`, above the title screen): choice of side per campaign (`campaignScreens` in `ui.json`: image and name field
  per side, heading; a click chooses, "Zurück"/Esc → title – not present in the original). Settlers (`settlers`): per people, file 02 GUI 7, gold frame under the
  mouse. Mission CD (`missionCd`): file 60 GUI 4 (castaways in a storm), name fields with difficulty (`label`, wrapped),
  brighter figures file 60 sequence 0. Amazons (`amazons`): file 48 GUI 1, on the left the Amazons (`amazons`), on the right the army of the three peoples (`peoples`); under the mouse brighter
  figures (file 48, sequence 2, placed by correlation, `lit`). → intro video of the side → (end or click) description of the first mission
  (`BriefingScreen` on the start-screen background GUI 8: mission text at the top with scrolling, map in the slanted window with abbreviations
  of the players at their starting points – own one green, otherwise red –, hints on the right, voice-over with it; "Zurück"/Esc → people selection,
  "Weiter"/Enter → mission with the map's peoples/buildings). `data/campaign.json`: per campaign `name` (text key, in the save game name) and `sides` with `name` (text key), `intro`,
  `outro` (victory video) and `missions` (`map`, `text`/`hint` = entries of the text table in GFX file 07, German/English/Polish by language (`Lang.TextIndex`: column 0/1/4 of the text table, Polish in code page 1250), `voice`,
  `labels` per map player – so far only mission 1, from the mission text – or `preview` [file, sequence, image] of a finished original preview image
  instead of the rendered map: Amazons file 48 sequence 3, Z01–Z12 = 0–11, G01–G12 = 12–23, matched by water mask); paths in the
  installation, `GameInstallation.FindPath` case-insensitive. Mission CD: `S3MCD/MAP/MIS_M/R|E|A` (texts 24–31 Romans, 32–39 Egyptians, 40–47 Asians, preview images file 60 sequence 1 = 0–23), without videos and voice-overs
  (`intro`/`outro`/`voice` omitted: skipped, not in the mission selection). Amazon campaign: `S3QOTA/MIS_M/Z` (Amazons, texts 48–59) and `G`
  (the three peoples, player 1 changes, texts 60–71), hints +72, videos `S3QOTA/ADD/1/DATAP0_N` (intro Amazons 1, three peoples 2; victory 3 or 5).
  Progress (global save state): `CampaignProgress` (`campaign.json` next to the save games, per campaign/side the number of missions accomplished);
  people selection with progress → mission selection (`MissionSelectScreen` via `ListScreen` – a list shared with "Spiel laden" –, file 02 GUI 18, `missionScreen` in `ui.json`: first the intro,
  all accomplished ones and the next one, names from the mission texts, after all of them the finale; a longer list scrolls (wheel, arrows, scroll bar), opens at the newest; click → description, whose "Zurück" leads back
  here, or video and then back; Esc/"Zurück" → people selection). Videos via `MapGame.PlayVideo(path, after)`. The running mission `MissionRef` (`settlers/Roman/0`, `amazons/peoples/3`) is stored in the
  save game (client value `mission`, header `SaveInfo.Mission` – lists show "Kampagne Römer, Mission 1").
  Victory/defeat: `World.Goals` (`MissionGoals`, only in missions, saved along) checks every second for player 1 (`MissionGoals.Player`): no own building
  (construction sites included) any more after there was one, or none and no settlers (G06 starts without buildings) → lost; no foreign one that is not allied any more (destroyed/conquered) → won; once decided, it stays that way. Mission-specific conditions
  from the texts (hold the outpost, amounts of gold …) not yet.
  Free matches (start screen, old save games too): `ConquestGoals` (`World.Goals` is `IGameGoals`) – whoever no longer has a finished military building
  (tower, large tower, castle) after having had one is finally defeated (`IsDefeated`); their AI stops thinking (`World` skips it),
  settlers/soldiers only act on their own. Per player (`OutcomeOf`, `DecidedAt`): defeated → lost, all enemies defeated → won; the client shows the local player's outcome (banner with `system.wonHint`/`lostHint`, statistics without "Weiter"). Won: white "Spiel gewonnen" at the top, progress saved immediately, playing on
  possible; Quit game → statistics ("Weiter") → next mission description via mission selection and people selection (after the last one the victory video, then the selection).
  Winning lifts the fog of war (`FogOn`; the cheat choice `_fogChosen` stays for the next match).
  Lost: "Spiel verloren", no more commands (selection, building, menus, keys except Esc/F11/F3/T/space bar); camera, minimap, events and computer menu
  keep working; Esc → statistics without playing on → the same mission description.
  Cheat menu: "Mission geschafft"/"Mission verloren".
  `VideoPlayer`: `AviFile` (Homestead.Assets, `docs/formate/avi.md`) supplies the header and PCM audio, the video (Indeo 5) is decoded by an
  installed ffmpeg in a separate process (`HOMESTEAD_FFMPEG`, next to the program, PATH; otherwise skipped); frames follow the clock from the
  first frame/audio start, volume = effects; Esc/space bar/Enter/click cancels.
- **Settings** (`SettingsScreen`): background file 02 GUI 14 (`settingsScreen` in `ui.json`, marble next to it); at the top, in the four
  boxes, tabs with icons of the original statistics menus (file 03, dark = selected, pressed in, otherwise light; Tab/Shift+Tab): Resources,
  Economy (still empty), Military, Display; one row of values per setting (Resources: `depletion` stones/mountains/fish
  100/75/50/25/10 %/∞; Military: spell cost increase constant/+10/+25/+50 % → `MagicConfig.CostIncrease`; Display: hardware cursor, full screen, language Deutsch/English/Polski (default English, independent of the system language; also applies to the installation dialog),
  theme Normal/Gold/Classic/Amazons (title image and loading screen; `titleScreen.themes` in `ui.json` with engraving colours, Normal = Gold; `HOMESTEAD_THEME` overrides), music/effects 100/75/50/25 %/off, music per people, preferred colour (`DisplaySettings.PreferredColor`, colour swatches, stored as an id), autosave
(yes/no, default no: every 15 min of real time in the game and on "Spiel beenden" into slot 8, not after a defeat, not in captures), intro video (yes/no, default yes: `MapGame.IntroVideo` = `S3/ADD/001/01_00_00.DAT` on every start before the title screen, end/click → title) – music without a choice by system language; `Lang.Use` also sets the culture;
title image of the Gold Edition per language, `background` `{de, en}`; Polish without its own title image (the English one), `HOMESTEAD_LANGUAGE=de|en|pl` overrides, e.g. for captures).
  OK/Enter saves `GameSettings` (section `game`) and `DisplaySettings` (client, section `display`, applied immediately) in
  `~/.config/homestead/settings.json` (`SettingsFile`: per section, the rest of the file stays); Cancel/Esc discards, Defaults resets the open
  tab (game values: `production.json`). Hardware cursor off: our own golden arrow (`SoftwareCursor`, in `EndDraw`, not in captures).
  New matches: `GameSettings.Load(…)` → `GameSetup.Settings` → `Apply(…)` in `OriginalScenario.Load(production:, magic:)`; save games keep their values (Arena/tests: only `production.json`).
- **Start screen** (`StartScreen`): on the original's map selection background (`startScreen` in `ui.json`, file 02 GUI 8,
  scaled to the window size on the 800×600 grid, marble next to it; oval stone buttons, stone bar below the selection, preview in the slanted window): map list with preview, tabs Single player/Multiplayer/Own maps (Tab key),
  sorted by number of players (`OriginalMap.ReadPlayerCount` only reads MapInfo). Tab "Zufallsgenerator": settings (players, symmetry, rivers, resources, stones, forests,
  treasures, King of the Hill; decoration not selectable, always medium) – a change regenerates with the same seed, "Neu würfeln"/R a new seed. Per player (table with fixed row height, more players scroll): people (or random; map defaults apply per position),
  colour (`PlayerColors.Palette`, sorted by hue, ids `Choices`; a click opens a selection below with "?" and all colours, taken ones
  struck through; player 1 preset with the "Bevorzugte Farbe" of the settings, default red; "Zufall" → `PlayerColors.Assign`), team (a click opens the selection 1–4/"-", default "-" = none; `World.Teams`,
  set after loading; `TeamOf`, `AreAllies`), Hardcore.
  Multiplayer starting positions drawn by lot (preview black "?", a click assigns/unassigns; `GameSetup.Positions` → `GameSetup.ResolvePositions`).
  Every match (start screen, `--map`, Page Up/Down, missions) starts via a `GameSetup` (`MapGame.SoloSetup` → `StartGame`, see `docs/MULTIPLAYER.md`).
  Hardcore (`World.HardcorePlayers`, `Hardcore`): +10 % combat strength flat (also above 100 %), +30 % residents per residence, +20 % soldier HP
  (`World.MaxHealth`), −25 % building material per pile (rounded, at least 1, `World.ConstructionCost`), working times × 0.9,
  start +2 swordsmen, +5 planks, +5 logs (`OriginalScenario.Load(hardcore:)`).
- **Side panel** (`SidePanel`, large variant, scaled to the window height; menus draw/check clicks in panel space, `SidePanel.ToPanel`):
  - Minimap at the top in the original frame (black behind it; on the right a narrow gold border with a tendril, file 4 sequence 3): explored terrain (`Minimap`; no longer in sight: half as bright, without foreign things),
    own always white, allies turquoise (`Minimap.AllyColor`, like own also under fog; border posts on the map stay in player colour), opponents always red – territory borders and dots (`MinimapDot`):
    soldiers, priests, geologists, pioneers, scouts outside (foreign ones under fog only in sight, scouts only once unmasked), buildings/construction sites as larger black squares (occupied military buildings white/red inside), the visible section as a white frame. Click: camera flight 0.35 s; dragging moves the frame; scrolling cancels the flight;
    right-click/Alt+right-click with a selection: command to that spot as on the map.
  - Event buttons to the right of the computer button (`EventButtons`: info, combat, resources, trade; position/images in `events.json` `categories`):
    a new event → the button blinks for `blinkSeconds` at 1 Hz, then stays lit until seen. Click (newest unseen of that kind, otherwise newest) or
    space bar (newest unseen; held, after 1 s at 10 Hz) → event page (`PanelTab.Event`: category, text white), camera flight,
    four arrows for `arrowSeconds` at the building or living figure. `dates`: greetings on calendar days (MM-dd), once a year
    (`~/.config/homestead/events-shown.json`; `HOMESTEAD_EVENT_DATE=12-25` fakes it).
  - Info feed (`InfoFeed`): white notes at the bottom left of the game area, new ones slide in from below and push older ones up (at most 6), 60 s each;
    with a player reference, a round dot in player colour in front (`InfoFeed.Add(…, dot)`);
    texts `info.*`: AI player defeated (`ConquestGoals.IsDefeated`, not those defeated before loading; their colour), demolition confirmation at the own
    last finished military building "Dies ist der letzte Turm" (own colour), saved/automatically/failed,
    "Seit n Minuten nicht gespeichert" every 30 min of real time since saving/start/loading (`MapGame.NoteNews`).
    Chat (`ChatInput`, also after a defeat): Enter opens an input line at the bottom ("> ", caret), then terminal view: the last 12 lines (faded ones
    too, history 100) on a dark background; keys only go into the line (no hotkeys/camera/F12, the mouse keeps working); Enter says it as
    player 1 in `World.Chat` (`ChatLog`, saved), Esc discards, ↑/↓ earlier lines, Ctrl+V pastes (`SdlClipboard`). New chat lines of all
    players go into the info feed as `chat.line` ("Spieler n: …", dot in player colour); loaded ones do not.
    Commands (`/…`, not in the chat, `MapGame.RunCommand`) only with `"cheats": true` in the `display` section of `settings.json` (`DisplaySettings.Cheats`,
    not in the settings screen); otherwise a random computer opponent (not allied, not defeated) says `chat.noCheats` in the chat.
    `/cheats` opens/closes the cheat menu (`CheatMenu`: soldiers per type/level, priests, geologists, scouts, pioneers, carriers,
    war machines, +50 mana, mission accomplished/lost, fog of war on/off); `/attackme`: all undefeated computer opponents `AiPlayer.Assault(0)` (info `chat.attackMe`); anything unknown: `chat.unknown` with the list.
    AI chat (`AiChat`, only with a key: section `aiChat` of `settings.json` with `apiKey`, `model` (default `claude-haiku-5-5`, without thinking;
    other models always think), `effort` (`low`), otherwise `ANTHROPIC_API_KEY`; tokens per request on the console): every own line → a request to the Claude API (Anthropic SDK) in the background, one at a time
    (anything said in between goes with the next one). Prompt and character voices in `data/ai-chat.json` (system prompt cached), situation `AiSituation`.
    Claude speaks for all computer opponents via the tools `say` (→ `World.Say`), `attack` (→ `AiPlayer.Challenge`, `all_in` → `Assault`;
    per opponent at most every `attackCooldownSeconds`), `truce` (→ `Truce`, up to `truceMaxMinutes`) and `defend` (→ `Help`, up to `helpMaxMinutes`); applied in the game thread, answers belonging to another game discarded; errors `chat.aiFailed`;
    unprompted (`AiChat.Reactions`, `reactions` in ai-chat.json): goods that own donkeys unload on a computer opponent's land
    (`World.GoodsHandedOver`), summed up by `giftValues` (gold 10 … food 2, the rest 0.2, log −1), at `giftPleased` (60) or
    `giftInsulted` (−8) a hidden request "das ist passiert"; captured military buildings (`buildingCaptured`/`buildingLost`, `captureChance` 15 %) and big
    battles (≥ `bigFightSoldiers` deaths per statistics sample, `chance` 30 %), per opponent at most every `cooldownSeconds` (90);
    own army advancing on an opponent (`army`, not allies: ≥ 20 own soldiers outside in their land or ≤ 20 fields in front of their military buildings, counted every
    second, on reaching it) with 30 %, per opponent at most every 10 min; prompt: alliances hold for the whole game, protect allied soldiers in the land; attack orders with their result on the console; umlauts written as `\u00e4` converted back.
    Multiplayer later: only the server asks and distributes the results as commands.
  - Computer button at the bottom left (`SystemMenu`, `PanelTab.System`): "Spiel speichern" (8 slots with map, date, people; empty ones dashed;
    a click saves, overwriting too), "Spiel beenden" with confirmation (yes at the bottom left → statistics, no at the bottom right).
  - Main tabs Build/Goods/Settlers (`PanelTabs`: original buttons file 03, the active one pressed; a click clears the selection; submenus `panelTabs` in `ui.json`, read via the `UiLayout` loaded once in `MapGame.LoadContent`, which all menus/screens receive in their constructor; the 8 submenus implement `IPanelMenu` (uniform Draw/Update/Hover, dispatched via `MapGame`), shared helpers `PanelUi`, layout via `PanelLayout`).
    - Build: build menu (categories from `data/ui.json`, original buttons; click on the icon → placing; own people; number of finished (white) and under construction (orange); tooltip with the name after hovering 1 s over an icon, gone on leaving – `BuildMenu.DrawTooltip`, in screen space, may extend over the map). The same box next to the cursor (`UiTooltip`, likewise after 1 s) also names the pure icon controls of the submenus (Stock, Manufacture, Transport, Distribution, Settler statistics, Professions, Warriors, Production).
    - Goods › Stock (`StockMenu`, `World.InStock`: storehouse, output and loose piles; not input/construction sites).
    - Goods › Manufacture (`ManufactureMenu`): per product an order with up/down (`Production.Ordered`/`ChangeOrdered`, made first) and
      share bar (`ShareBar`: a click sets, dragging, 5 % steps, 0/100 % near the ends).
    - Goods › Distribution (`DistributionMenu`): goods with ≥ 2 consumers (order `distributionMenu`), on the right per consumer share + `ShareBar`.
    - Goods › Transport (`TransportMenu`): transport order in rows of six (offset); dot green = store, red = do not; selected
      good: store/do not, to the very top, up, down, to the very bottom (images `transportMenu`); icons glide.
    - Settlers › Settler statistics (`SettlersOverview`): housing, demand, all settlers, soldiers, per kind portrait + count (donkey 0; without a portrait a figure in a frame).
    - Settlers › Professions (`ProfessionsMenu`): shares ±5 % (carriers at least, diggers/builders at most; `Recruitment.ShareOf`/`SetShare`,
      defaults `keptCarriers`/`maxDiggers`/`maxBuilders` 30/25/25 %, sum ≤ 100 %, the locked increase half grey); special professions geologists, pioneers, scouts, pioneers→carriers (carrier portrait)
      with search buttons (all on the map, all on the screen, next one in turn with camera flight) and convert 5/1
      (`Geology.ConvertCarriers` with `among`: only carriers on the screen, those closest to the screen centre first – none on the screen: a hint; `Geology.PioneersToCarriers`); converted ones are selected immediately (the menu stays open, `_menuOverSelection`), those still delivering join after the conversion.
    - Settlers › Warriors (`SoldiersMenu`): combat strength, divine upgrade (mana progress, a weapon button per type, ornamented from level 2),
      automatic/manual recruitment with free carriers; manual: per type weapons in stock and planned recruits (⇈ +5, ↑ +1, ↓ −1, ⇊ −5;
      `Military.SetManualRecruitment`/`ChangePlanned`).
    - Settlers › Production (`StatisticsMenu`): chart of the last 60 min of one good (units/min, 2-min average, the scale adapts);
      good selection `GoodsGrid` (shared with Stock: icons `goodsGrid` in `ui.json`, indices from JSettlers' EMaterialType, not producible ones dark);
      counted in `World.Statistics` (`ProductionStatistics`, 30-s sections).
  - Clicking a building replaces the build menu (without its own background): name, image (bottom-centred, scaled down to the box – never cropped –
    `EconomyRenderer.DrawBuildingImage`; when space is tight, first the goods go, then the image gets lower), for own buildings the status, at the top right the demolish icon as in the original (file 03 GUI 199, `buildingInfo` in `ui.json`) with a confirmation as in the original (only the building image stays in its place, question at the bottom, yes/no as light texts; any other action – a click beside it on release, another selection, Esc – cancels);
    at the top left, as in the original, "Arbeit einstellen" (house, GUI 395)/"Arbeit fortsetzen" (hand, 194) for production and construction sites (`World.SetPaused`:
    work finished, then inside; construction site: no deliveries/workers, those present free after their cycle), for construction sites next to it "Höchste Priorität"
    (cogwheels 197, pressed with "!" 380; `World.SetPriority`), for finished own buildings with a work area the button for it in the horizontal middle
    (own icon: concentric rings in spectral colours, generated graphic `data/ui-icons/work-area.png` (regenerable via `tools/gen-ui-icons.py`), no atlas image; a click frames it, the next left-click moves the work area like the right-click, which/Esc cancels; `BuildingInfo.WorkArea`),
    the name then below the icons, hint text above them under the mouse; military: garrison per type (present/wanted, +/−);
    storehouse: all goods (`GoodsGrid`; dot = general rule, a click specialises – red frame –, again clears it);
    marketplace (`BuildingInfo.Market`, "Handel – Land", without an image; harbour "Handel – Meer": bar GUI 377 `seaRoute`, ship before ×1 sets the dock like
    the shipyard's hammer, route points onto the water, route from the berth): bar ×1 ×2 ×3 package (file 03 GUI 374, `market` in `ui.json`; not set
    dark): choose a button (white frame; stops only with a destination; selecting the chosen stop again = delete), then a left-click on the map sets
    it (flag as a preview at the mouse pointer, only where donkeys can get; right-click/Esc cancels), route like the barracks' gathering point; goods with amount below, ∞ (386)/0 (389)/arrows ±5/±1 for the selected good; goods/building material,
    mine success chance (goods block centred below the label in the free area, one good horizontal); work area (right-click moves it, `World.SetWorkCenter`, at most `MaxWorkCenterDistance` = 20, otherwise the nearest permitted
    field; gatherers take the target nearest to the centre; barracks: gathering point anywhere, `Building.HasGatheringPoint`, path with flag).
    Shipyard (`BuildingInfo.Dockyard`, `dockyard` in `ui.json`): on the left ferry and trade ship (last 12 GUI images of the people's file; with a dock
    clickable → order, the button stays pressed in (dark image), both locked until the launch; at the bottom building material delivered/needed),
    on the right the hammer (file 03 GUI 392) → the next left-click into the water sets the dock (pale preview at the mouse pointer; right-click/Esc cancels;
    locked during construction).
    German names/states in `data/ui.json`; text via FontStashSharp with a system font.
- **Statistics** (`StatisticsScreen`; "Spiel beenden", Esc after a lost game; the game stands still): blue marble (`statisticsBackground`, file 02 GUI 29),
  pages Overview (table, share of the land), Economy, Military, Goods (every good per minute), Society – charts `Charts` (lines with values
  under the mouse, shares, bars) from `GameHistory`/`ProductionStatistics`; ←/→/Tab page through; "Weiterspielen" (Esc; not after "Spiel beenden"),
  "Zum Hauptmenü".
- **Player names** in the game (statistics, chat, defeat, trait hints): `PlayerNames.Of` → `start.playerPeople` ("Spieler 2 (Römer)"),
  never the AI character (only on the console at the start now). In the `InfoFeed` (chat, defeat) the name is in the player colour
  (`Add(…, names)`; very dark colours lightened), the rest white. Notes wider than the box (at most 40 × font size) wrap at
  spaces (words that are too long are split), continuation lines indented further (`Hang`, 1.25 × line height); wrapping cached per note.
- **Text buttons** (`TextButton`): stone buttons from the pre-game menus (`textButton` in `ui.json`, file 02 GUI 0, 9-slice), e.g. demolish,
  stop, yes/no, recruitment, statistics. Garrison ±: up/down spinner as in Manufacture.
- **Commands to the world** (lockstep preparation): the client changes the simulation only via `GameCommand`s – `MapGame.Issue(command, then)`
  (the result after applying goes to `then`: message, sound, selection; `TakeCommandResults` after every `Step`), menus/panel/AI chat via the delegate
  `IssueCommand` (`IssueReported`: result → message). Exceptions: game setup, capture/demo code, own AI (key I, single player only),
  statistics sample on opening and calendar events (the game logic reads neither).
- **Selection/commands**: a box or a click selects own figures, priority soldiers > war machines (never together) > priests > geologists > scouts > pioneers,
  carriers and other professions never (a box without such figures: the building nearest to its centre; buildings: white corner frame 3 px). Shift adds,
  Ctrl+click all own ones of this profession on the screen (soldiers: all), Alt+click those of this type on the screen (soldiers: type, rank irrelevant). Selected ones as in the original: two white corner brackets above the head, between them
  a round dot with a black edge, green→yellow→red by HP (`DrawSelectionMarker`).
  `SelectionPanel` per kind (type/level and people – converted ones show theirs) portrait (`Portraits`, `ui.json` `portraits`; other peoples `MapGame.PortraitsOf`) + count (too many: compressed, more columns or smaller), combat strength at the bottom – like the spell menu
  in the content area (`PanelLayout.Content`). Ctrl+1–9 stores a group (ships too; every figure/ship in at most one – it leaves its old one; portrait of the first member as in the `SelectionPanel` or the ship's button image, the same height, wider images (war machine, ship) wider, the following ones move along, at the top right, the selected one framed in yellow; a click or 1–9 selects; if the selection is exactly one group, what is added via Shift also joins the group; twice within 0.5 s: camera flight to the centre; the dead, those entering buildings, the converted drop out; in the save game as the client value `groups`).
  Soldiers: right-click sends/attacks, Ctrl+right-click forced without combat, Shift+right-click waypoint (`Military.AddWaypoint`, forced with Ctrl),
  Alt+right-click patrol (`Military.Patrol`); Tab: only wounded soldiers remain in the selection (HP < `World.MaxHealth`; control groups unchanged). Ships (`_ships`, instead of settlers; click, Ctrl: all on the screen,
  a box without settlers in it): right-click sails there; panel: per kind button image, name, count, ferries "An Bord: n/7", trade ships "Ladung: n", at the bottom "Schiff entladen" (`BuildingInfo.DrawShips`/`UnloadAt`).
  Selected units: right-click on an own ferry near land → board; drawn standing on deck (`ShipRenderer`: whole ship, passengers behind
  the mast, front part = the sequence before, sail, passengers in front of the mast). Geologists: right-click there, search on mountains.
  Priests: spell menu as in the original instead of a selection (`SpellMenu`: 8 spells per people, `menu` in `magic.json` – implemented ones and placeholders with
  guessed name/cost, not castable; symbols `spellMenu` in `ui.json`; charge bar mana/cost: below 1 a gradient, then one stripe per charge,
  alternating light/dark, each 1.35× as wide as the previous one, the whole bar = all mana (measured in the original); at the top the name/cost under the mouse or a target hint, at the bottom mana), choose a spell, then the target
  (target ring red: affects nobody); right-click there.
  Converting carriers only via Settlers › Professions. A left-click into empty space deselects (figures, buildings, menus) and clears the panel as in the original
  (`PanelTab.None`, no tab pressed); dragging with the right button moves the map.
- **Mouse** (`SdlMouse`, everywhere instead of `Mouse.GetState()`; `NextFrame()` first thing in `Update`): buttons from SDL events (event watch) instead of
  from MonoGame's per-frame polling – a button counts as pressed in a frame if it is down or was pressed since the previous frame (taps on the
  trackpad are not lost; on macOS the two-finger click thus arrives as the right button, issue #6). Position from `SDL_GetMouseState` (relative to the window; MonoGame computes global minus
  window position, which is off by the window offset under XWayland/COSMIC). Without SDL: MonoGame's state.
  SDL library for our own DllImports: `SdlLibrary` (also in the platform folder such as `osx-arm64/`).
- **Keys**: Enter chat (see info feed), Esc cancels placing/aiming, otherwise pause like P; no cheat keys (only `/cheats`, see info feed);
  F attack on the enemy military building under the mouse, fog of war (vision 1×/s, `FogLayer` fades softly,
  tapering off over `SoftFields` (3); unexplored black, explored dark, foreign figures only in sight; on/off only in the cheat menu), F11 full screen (for the session only; default in the settings),
  F2 scaling sharp/linear/Catmull-Rom (default)/Lanczos2 (world 1:1 into a small RenderTarget, enlarged by the zoom; markers/panel sharp;
  `Shaders/Upscale.fx` → embedded `.mgfx`, after a change `tools/compile-shaders.sh`, needs Wine),
  F3 performance display (FPS, ms simulation/update/draw, longest frame, draw calls; only drawing what lies in the visible rows and
  horizontally on the screen – margin 96 px, building strips by pixel width up to 320 px, ships 160 px), F12 held 10× speed, T game time, I AI plays for you
  (other players always AI), L AI decisions to the console,
  P/Esc pause (banner like "Spiel gewonnen"; the world stands still, camera/panel/commands work), placing via the build menu (dots at suitable building spots, green→red by levelling effort – `World.LevellingEffort` / `levelling.maxWork`; at most 6 ms per frame,
  `World.BuildSpotEffort`; preview at the nearest building spot; Q/E building; ends after placing, right-click cancels).
- **Options**: `--scene map` (default) / `--scene walk` (walking PoC); `--load <slot>` (1–8; terrain/objects from the map, then field by field up to date);
  `--map <name>` (default: first multiplayer map; `random[:players[:none|mirror|repeat[:seed]]]`); `--peoples amazon,roman,…` (open peoples,
  player i: entry i mod n; otherwise random; map defaults 0–3 apply); `--characters ,general` (AI character per player, entry i → player i+1, empty = drawn by lot);
  `--player <n>` (played player of free matches, 1–8, default 1; all others AI).
- **Local player** (`MapGame._local`, from 0): the player on this computer – commands, selection, fog of war (`Vision`), minimap (white), menus,
  building panel, control groups, events, victory/defeat (`Goals.OutcomeOf`), AI chat (`AiChat.Player`), start screen ("Du", preferred colour), capture scenarios.
  Missions always `MissionGoals.Player` (0); save games store it (client value `player`, older ones: 0). For multiplayer later from the server.
- **Sound** (`GameAudio`, not in captures): effects from `SND/Siedler3_00.dat` per `SoundCue` (numbers in `data/sound.json`, recognised in the original:
  sword 30, bow 33 – Egyptians 32 –, spear 34, death 35, Amazon 113, tree falls 36, building falls 93, casting 75, healing 21, pioneer 25/1, geologist from
  `minAmount` 24/1–3, farmer sowing 8; table `docs/formate/snd.md`; `SoundRef`: number or `{sound, variants}`), variant random, only in sight (fog), full on screen, outside quieter up to `hearing` half screen widths,
  panned left/right, at most `maxVoices` at once and `perFrame` per sound and frame. From the screen centre: command to controllable units (walking/attack, searching, stealing, claiming land, casting; `command` 57),
  event message (`events`: combat 109, otherwise 110), panel via `UiSounds` (`ui`: order/share/garrison/transport order up 47/down 48,
  switches such as recruitment, stop, storing, storehouse specialisation 54, work area moved 22, click on the demolish icon 51). `WorkSounds`: outdoor work per `Activity` (`work`: felling 0, levelling 2, building 1, stonecutter 3,
  fishing 16) in time with the work animation (moment `at` per round, `SettlerRenderer.WorkRoundFrames`); workshops (`workshops`: sawmill 5,
  toolsmith/weaponsmith 6/7, iron/gold smelter 37/38) roughly every `everySeconds` as long as `Production.IsProducing`. `Ambience` (`ambience` in `sound.json`): terrain on the screen sampled every 0.1 s (every `sampleStep`-th field, fading out `margin` beyond
  the edge, unexplored silent), per kind loudness by share (full from `fullShare`, glides in `fadeSeconds`): sea 68, river 71, desert 67, wind 73/74
  (mountains, snow) as variants back to back without a pause (`overlap`), panned to the side of the terrain; bees 117/0 by beehives on the screen (`objects`, every field counted,
  full from `fullCount` 6), quiet and only one at a time (`overlap` 0; in the original one per hive, clipping); birds 69/70 on grass individually every `everySeconds`
  from a field on the screen; keeps running during the pause, silent outside the game. `SwampBubbles` (display only, not saved): bubbles on green swamp (`Moor`, not brown
  mud `Mud`) on the screen (per field one every `bubbles.perFieldSeconds`, only in sight): file 1 sequence 106 (grows), then 107 (sinks) or 108 (bursts, bubbling 72 – a quiet recording, `gain` amplifies it on loading). `MusicPlayer`: `MUSIC/TrackNN.ogg` (streamed with NVorbis)
  from `Tracks` (`SoundConfig.TracksFor`: list of the own people, `music.tracks`, assigned by ear; "Musik je Volk" off: all, own ones first),
  a new list starts with its first piece, then shuffled, a pause in between, a new list without the running piece fades out; silent in the pre-game menus. Music/music per people/effects in the settings
  (Display, `DisplaySettings`).
- **War machines** (`WarMachineRenderer`, `warMachines` in `ui.json`, finds in `docs/formate/gfx.md`): body per direction, rolling wheels
  above it, shot per direction, projectiles in an arc; the gong moves the way a settler walks. Impact: small grey explosion (file 13 seq. 49);
  damaged buildings smoke (42, from half 43 dark) and burn (41 from ¼, 40 from ½, 39 from ¾ damage) until they are repaired. Selection together with soldiers (`SelectionKind`, portrait
  `WAR_MACHINE`), right-click: go there or bombard a military building; ammunition count above the dot; "Munition laden" in the selection panel.
  Works panel (`BuildingInfo.WarWorks`): machine/ammunition (click +1, Ctrl+click back, ×n ordered), loads, material of the
  current order. Sound `machineShot` (guessed), hit `machineHit` 88/0. Cheat menu: 2 war machines.
- **Rendering**: ships (`ShipRenderer`, `ships` in `ui.json`): file 36, 4 sequences per ship (deck, shadow, front part, whole without sail)
  of 6 directions each, sails 28–34 with player colour; small ship = ferry, large = trade ship (Romans checked in the game), cargo as piles on deck; under construction the hull grows from below. Waves (`Terrain/WaveLayer`): a long straight swell runs diagonally (towards the bottom left), water corners rise by up to 4 height steps (water triangles on the screen redrawn, lighting like slopes), crests blended with a light water tile (image 16), the front side glistens, above it the original glitter (file 01 seq. 26) as overlapping tiles (spacing ¾ image, every second row offset by half a step, not raised with the waves), image by the phase of the swell at the tile centre – plays while the front passes through, several one behind the other at once (swell 18 fields long, 1.8 fields/s); strength by distance from the shore (1 field calm, then full over 14, soft); ships rock along (display only); stand still during the pause. Figures by people; the dead: ghost in player colour (file 12 seq. 27, `World.Ghosts`); spell effects as original status symbols above the head (`StatusIcons`, `statusIcons` in `ui.json`: file 1 sequences 128–133 – samurai sword, strengthen, fear, shield, cursed arrows, frozen –, several side by side; 134/135 levelling uneven/very uneven, 136–138 info signs, unused so far); those standing without a task change their viewing direction every 2–30 s
  (display only); tower bowmen with the shooting animation in rhythm, without shadow (as in the original), infantry in the tower as a window image; buildings: layer 0, garrison, then further layers (window frames/parapets in front); drawn in 8-px strips, each in the frontmost blocked-field row of its column (`EconomyRenderer.Strip`, otherwise a tall building hides what stands diagonally in front of it); arrows in a flat arc
  (`docs/formate/gfx.md`). `SettlerAnimations` resolves type/action/good/direction via `data/settlers.json` (generated with
  `tools/data-import/convert_jsettlers_movables.py`, do not edit by hand). Construction sites show a sign and posts.
