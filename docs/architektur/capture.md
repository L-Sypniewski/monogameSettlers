# Capture (`--capture <dir>`)

Offscreen PNGs for inspection, only into temporary directories (no original assets in the repo).

- **Capture** (`--capture <dir>`, offscreen PNGs, temporary directories only): ignores input, without VSync, 4× speed; prints counters and
  building states at the end. Environment variables `HOMESTEAD_CAPTURE_…`:
  `ZOOM=4`; `EVERY=<n>` (PNG every n frames, otherwise 120); `SIZE=1920x1080` (otherwise 960×600); `SCALING=<Sharp|Linear|CatmullRom|Lanczos2>`;
  `FOCUS=<building>` (`site`: fresh construction site; `temple`: small temple with mana; military buildings like `castle`: fully garrisoned); `FOG=1`;
  `EVENT=1` (jumps to events); `BATTLE=1` (level-3 enemies attack the focused military building, otherwise the start tower; `=big`: two armies of 30 each meet south of the start; `=open`: 40 against 40 on open ground; `=town[:n]`: n (90) against n in the middle of the village; with `ZOOM` the camera on it); `COLORS=2,4` (palette colours per player); `STRIKE=1` (demolition of the focused building + 10 carriers without housing);
  `GEOLOGISTS=1`; `PIONEERS=1`; `DOCK=1` (shipyard at the nearest open sea, the land there owned, with a dock; `=none` without; `=build`: ferry under construction, material next to it; `=ships`: ferry and
  trade ship finished, selected, as group 4, sent off (with `FOCUS=none`); `=ferry`: ferry at the dock, 8 soldiers, 7 board, ferry selected; `=harbor` (with `FOCUS=harbor`): harbor with dock, trade ship, goods, target; otherwise with `FOCUS=dockyard`); `MACHINES=1` (own workshop with 2 loads and an order, material next to it; enemy tower, one machine per people firing at it, selected, as group 3;
  with `FOCUS=<workshop>` its panel, otherwise `FOCUS=none`); `HIVES=1` (four beehives, one per level, next to the start, camera on them); `TRADE=1` (marketplace with the storehouse door as target, stop, planks ∞, stones 12, three donkeys, donkey farm; with `FOCUS=market_place`); `SELECT=1` (soldiers of all types/levels selected, as group 1, the bowmen among them as group 2; `=mixed`: also those of player 2 in the selection); `SAVE=<slot>` (after 300 frames; with `HOMESTEAD_SAVE_DIR` temporary!);
  `TAB=system[:save|quit]` or `goods|settlers[:<submenu>[:manual|<good>]]` (e.g. `settlers:soldiers:manual`, `goods:distribution:bread`);
  `STORE=<good>` (with `FOCUS=stock`); `PLACE=<building>`; `START=<tab>[:color]` (only `start.png` of the start screen, 3: Random generator; `:color` with player 1's colour picker open, `:team` with the team picker open);
  `MAGIC=1` (large temple, mana chain, a priest casting into a battle; `=summon`: Heavenly gifts; `=<SpellKind>` that spell of your own people,
  terrain spells onto the freest grass area, the console shows the terrain there);
  `MINUTES=<n>` (benchmark: AI for player 1 too, n min fast-forwarded without rendering, then a camera pan across the map, PNG every 1200 frames);
  `STATS=<frame>` (statistics opens in this frame, all pages as `stats_N.png`, then the end);
  `CHAT=<text>` (players 1–3 say something, player 2 long enough to wrap, the chat line open with the text);
  `VIEW=economy|battle` (instead of the pan: camera on player 1 or following the biggest battle; `battle` fast-forwards up to 20 min more with `MINUTES`, until ≥ 20 soldiers are in one battle).


## Screens before the game
- **Intro video**: `HOMESTEAD_CAPTURE_START=intro` → `video.png` (100th frame).
- **Title screen**: `HOMESTEAD_CAPTURE_START=title` → `title.png` (`title:readme`/`title:contribution`/`title:licenses` with popup, `title:load` → `load.png` (list of save games), `title:loadError` with a load error).
- **Loading screen**: `HOMESTEAD_CAPTURE_START=loading[:settlers|missionCd|amazons]` → `loading.png`.
- **Campaign**: `HOMESTEAD_CAPTURE_START=campaign` (Mission CD: `missionCd`; Amazons: `amazons`, sides `amazons`/`peoples`; `HOMESTEAD_CAPTURE_HOVER=<side>` as under the mouse) → `campaign.png`, `campaign:<people>` → `video.png` (100th frame), `campaign:<people>:briefing` → `briefing.png`, `campaign:<people>:choose` → people selected (with progress `missions.png`), `campaign:<people>:won|lost` → mission 1 started, decided immediately, game running (banner); `…:won:next`/`…:lost:next` → leaving the statistics, `briefing.png` of the second or back to the first mission; a number at the end takes that mission (`won:next:8` → victory video, `video.png`) (with `HOMESTEAD_SAVE_DIR` temporary!).
- **Multiplayer**: `HOMESTEAD_CAPTURE_START=multiplayer` → `multiplayer.png` (`multiplayer:create`: start screen for creating one; `multiplayer:lobby`: sample lobby without a server); `HOMESTEAD_CAPTURE_SERVER=ws://…/ws`: connects, creates a game (player 1 human, player 2 AI, random map), starts and plays it via the server (without the demo economy).
- **Settings**: `HOMESTEAD_CAPTURE_START=settings[:resources|economy|military|display]` → `settings.png`.
- **Installation dialog**: `HOMESTEAD_CAPTURE_DIALOG=<png>` (see `CLAUDE.md`, Installing the original).
