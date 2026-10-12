# Homestead

Privater Nachbau von „Die Siedler 3" in MonoGame/C#. Vorhaben, Ziele und Roadmap: [docs/PROJEKT.md](docs/PROJEKT.md).
Kommunikation auf Deutsch; Code und Code-Kommentare auf Englisch.

## Verbindliche Regeln
- Keine Original-Assets (GFX, SND, Karten, Musik, daraus exportierte PNGs) ins Repo oder den Build.
  Exporte zum Prüfen nur in temporäre Verzeichnisse.
- Original-EXE/-DLLs (auch `GFX/Siedler3_18…dat`, eine DLL) nie dekompilieren, disassemblieren oder analysieren.
  Nur Datenformate analysieren.
- Kein Kopierschutz/DRM umgehen.
- Code aus JSettlers (MIT) darf übernommen werden → Hinweis in `THIRD_PARTY_NOTICES.md` pflegen.
- Kein „Siedler"/„Settlers" in Projekt- oder Typnamen.

## Aufbau
- `src/Homestead.Assets` – Parser für Originaldateien (GFX, Karten), ohne MonoGame-Abhängigkeit. Formatdoku in `docs/formate/`.
- `src/Homestead.AssetViewer` – MonoGame-Viewer (DesktopGL), keine Content-Pipeline.
- `src/Homestead.Simulation` – Spiellogik, ohne MonoGame; feste Tick-Rate (`World.TicksPerSecond`).
  `Map/GameMap` (Gelände, Objekte, Begehbarkeit), `Pathfinding/PathFinder` (A*), `World`/`Settler`
  (Bewegung: ein Feld je Figur, Tauschen, Ausweichen, Umweg, Watchdog-Durchlauf – siehe Kommentar in `World`).
  `Buildings/` (Definitionen aus `data/buildings/*.json`, Gebäude, `Construction`: Planierer/Bauarbeiter),
  `Economy/` (Waren, Stapel, `Logistics` mit Watchdog; `Problems` zählt Abbruchgründe; `Production`:
  Werkstätten, Minen, Schmiede und Sammler, Werte aus `data/production.json`; `Housing`: Wohnhäuser
  entlassen Träger; `Recruitment`: Träger
  holen ihr Werkzeug und werden Spezialisten – Tests ohne Werkzeugthema nutzen `TestConfig.NoTools`;
  `Idling`: untätige Träger gehen aus dem Weg),
  `Military/` (Kaserne bildet Soldaten aus, Militärgebäude rufen Soldaten, besetzte beanspruchen Land;
  `MilitaryCombat`: Angriff, Kampf, Verteidigung, Eroberung; nahe am Feind lokale Wegsuche (Radius 10) um stehende/kämpfende Figuren herum;
  Werte in `data/military.json`, Gebietsradien aus den Originalkarten gemessen). Siedler und Gebäude haben einen
  Spieler; Logistik/Rekrutierung bedienen nur den eigenen Spieler, `World.TerritoryRules` erzwingt eigenes Land.
  `OriginalScenario` (Karte → Welt mit Gebäuden, Stapeln, Siedlern; Mehrspieler-Karten ohne Gebäude bekommen
  über `StartSetup` für jeden Spieler die gleiche Startausstattung aus `data/start.json`).
- `data/` – Spieldaten (keine Original-Assets), wird neben jedes Programm kopiert. `production.json` ist
  unser eigenes Balancing (Zeiten, Rezepte, Steine je Felsgröße, Erz je Mengenstufe, Regeneration). Gebäudedaten sind aus
  JSettlers generiert (`tools/data-import/`); nicht von Hand im generierten JSON ändern.
- `tests/Homestead.Simulation.Tests` – xUnit; Tests gegen Originalkarten laufen nur mit lokaler Installation
  (sonst bestehen sie ohne Prüfung). `dotnet test`
- `src/Homestead.Client` – MonoGame-Spiel. Szenen: `--scene map` (Standard, Originalkarte mit Figuren:
  Klick wählt/schickt Figuren, Klick auf ein Gebäude zeigt Zustand + Arbeitsbereich, N neuer Träger, M 50 Wanderer, K Testsoldaten (Shift: Spieler 2), F Angriff auf das feindliche
  Militärgebäude unter dem Mauszeiger, Rahmen ziehen wählt eigene Soldaten (Klick: hinschicken/angreifen), G Demo-Wirtschaft, P Platzier-Modus (Q/E Gebäude, Shift+Klick sofort fertig – Wohnhäuser
  entlassen dann Träger);
  `--map <name>`, Standard: erste Mehrspieler-Karte). Capture-Läufe ignorieren Eingaben (Tastatur/Maus sind global) und laufen ohne VSync mit 4-facher Geschwindigkeit (verdecktes Fenster wird sonst gedrosselt). und `--scene walk` (Lauf-PoC). `--capture <dir>` rendert offscreen und speichert PNGs
  (zum Prüfen ohne Fenster; nur in temporäre Verzeichnisse). `HOMESTEAD_CAPTURE_ZOOM=4` und
  `HOMESTEAD_CAPTURE_FOCUS=<gebäude>` für Nahaufnahmen, `HOMESTEAD_CAPTURE_BATTLE=1` lässt Feinde den Startturm angreifen; am Ende gibt der Lauf Zähler und Gebäudezustände aus.
- `tools/Homestead.AssetTool` – CLI: `scan` (alle Bilder testweise dekodieren), `export`, `sheet` (PNG),
  `maps`, `resources` (Statistik der Bodenschätze), `minimap`.

## Kartenobjekte
`GameMap` speichert je Feld den Original-Objektcode (`MapObjectCodes`); Regeln (Begehbarkeit) und Grafik
leiten sich beide daraus ab. Laufzeitänderungen: Baumstumpf 41, Setzling 84, Felsgrößen 115→127 (Geröll).
Getreide nutzt eigene Codes 200–207 (Wachstumsstufen, 207 reif), die in Originalkarten nicht vorkommen.
Bodenschätze (Typ + Menge je Feld) liegen ebenfalls in `GameMap` (`ResourceAt`).

## Grafik-Konventionen
- Raster → Pixel: `x*16 - y*8`, `y*9 - höhe*2` (Zeilen halb versetzt), 6 Nachbarrichtungen.
- Gelände-Atlas: Texturkoordinaten immer einen halben Texel innerhalb der Atlas-Zelle halten,
  sonst entstehen Linien aus Nachbarzellen.
- Lauf-Sequenzen: 72 Frames = 6 Richtungsblöcke à 12 (SW, W, NW, NE, O, SO); Frame 0 im Block = stehen,
  1–11 = Laufzyklus (ein Zyklus pro Rasterschritt).

## Installation des Originals
Pfad über `HOMESTEAD_GAME_DIR` oder `~/.config/homestead/settings.json` (`{"gameDirectory": "..."}`).
Lokal: GOG Ultimate Collection unter `~/Games/gog/the-settlers-iii-ultimate-collection/drive_c/GOG Games/Settlers 3 Ultimate`.

## Bauen/Starten
- `dotnet build Homestead.sln`
- Programme mit `dotnet <pfad>.dll` bzw. `dotnet run --project …` starten – der native Apphost findet
  .NET in der Flatpak-Umgebung von VS Code nicht.
