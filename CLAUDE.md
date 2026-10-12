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
  `Economy/` (Waren, Stapel, `Logistics` mit Watchdog – je Ware erst die Zielart (Baustelle, Gebäude, Lager;
  Gebäudetyp nach `distribution`-Gewichten, z. B. Nahrung an Minen 90:10), dann das nächste Ziel mit Platz; ins Lager kommen nur die Waren aus `storage.materials`
  in `production.json` (Werkzeuge, Waffen, Gold), alles andere bleibt am Ausgabestapel des Erzeugers (8; voll → er wartet); `Problems` zählt Abbruchgründe; `Production`:
  Werkstätten, Minen, Schmiede und Sammler, Werte aus `data/production.json`; `Housing`: Wohnhäuser
  entlassen Träger; `Recruitment`: Träger
  holen ihr Werkzeug und werden Spezialisten – Tests ohne Werkzeugthema nutzen `TestConfig.NoTools`;
  `Idling`: untätige Träger, Soldaten, Planierer, Bauarbeiter, Geologen und arbeitslose Fachleute gehen aus dem Weg;
  `Geology`: Geologen untersuchen Gebirgsfelder im Umkreis `geologists.searchRadius` um den Zielpunkt (nächstes freies Feld, bei Gleichstand zufällig) und setzen Schilder mit Rohstoff und Menge;
  wird ein Gebäude abgerissen, behält der Arbeiter Beruf und Werkzeug und übernimmt das nächste Gebäude seines Berufs),
  `Military/` (Kaserne bildet Soldaten aus, Militärgebäude rufen Soldaten, besetzte beanspruchen Land;
  `MilitaryCombat`: Angriff, Kampf, Verteidigung, Eroberung; nahe am Feind lokale Wegsuche (Radius `combatPathRadius`) um stehende/kämpfende Figuren herum;
  Werte in `data/military.json`, Gebietsradien aus den Originalkarten gemessen). Siedler und Gebäude haben einen
  Spieler; Logistik/Rekrutierung bedienen nur den eigenen Spieler, `World.TerritoryRules` erzwingt eigenes Land (Bauplatz samt Tür, Stapeln, Bauarbeiterplätzen; Wege aller außer Soldaten und Geologen,
  Erreichbarkeit über Landgebiete je Besitzer in `PathFinder`; Gestrandete gehen heim).
  `Ai/` (`AiPlayer`: Computerspieler mit denselben Befehlen wie ein Mensch, kennt die ganze Karte; baut nach
  `data/ai.json` und Bedarf (Wohnhaus, solange Träger ≤ 2 × Produktionsgebäude; das größte, dessen Material doppelt vorrätig ist), expandiert mit Türmen gezielt zu fehlenden Rohstoffen; `AiPlayer.Military`: kennt alle feindlichen Soldaten,
  Verteidigung zuerst, greift kleinere Trupps an, wenn deutlich überlegen (laufend neu bewertet), verteilt die übrigen
  zur Bewachung nach Bedrohung; `AiPlayer.Campaign`: sammelt Soldaten und erobert feindliche Militärgebäude bei 1,5:1;
  Werte in `ai.json`, je Volk überschreibbar (`civilizations`, dort auch die Build-Order je Volk); `MapSurvey`: Kartenüberblick in Zellen;
  `World.AiPlayers` werden in jedem Tick angestoßen).
  `OriginalScenario` (Karte → Welt mit Gebäuden, Stapeln, Siedlern; Mehrspieler-Karten ohne Gebäude bekommen
  über `StartSetup` für jeden Spieler die gleiche Startausstattung aus `data/start.json`).
- `data/` – Spieldaten (keine Original-Assets), wird neben jedes Programm kopiert. `production.json` ist
  unser eigenes Balancing (Zeiten, Rezepte, Steine je Felsgröße, Erz je Mengenstufe, Regeneration, `depletion`: wie viel Prozent der Ausbeute Steine, Berge, Fisch verbrauchen – 0 = unendlich). Gebäudedaten sind aus
  JSettlers generiert (`tools/data-import/`); nicht von Hand im generierten JSON ändern.
- `tests/Homestead.Simulation.Tests` – xUnit; Tests gegen Originalkarten laufen nur mit lokaler Installation
  (sonst bestehen sie ohne Prüfung). `dotnet test`
- `src/Homestead.Client` – MonoGame-Spiel. Szenen: `--scene map` (Standard, Originalkarte mit Figuren:
  Baumenü links (Kategorien wie im Original aus `data/ui.json`, Icon-Klick startet das Platzieren; Gebäude des eigenen Volks), Klick wählt/schickt Figuren, Klick auf ein Gebäude zeigt links statt des Baumenüs Name, Bild und (bei eigenen) Status, Waren bzw. Baumaterial und bei Minen die Trefferquote der letzten 10 Gänge (deutsche Namen/Zustände in `data/ui.json`, Text über FontStashSharp mit einer Systemschrift) sowie den Arbeitsbereich, N neuer Träger, M 50 Wanderer, K Testsoldaten (Shift: Spieler 2), F Angriff auf das feindliche
  Militärgebäude unter dem Mauszeiger, Rahmen ziehen oder Klick wählt eigene Figuren mit Vorrang Soldaten > Geologen > untätige Träger, andere Berufe nie (Soldaten: Rechtsklick hinschicken/angreifen, Strg+Rechtsklick erzwungener Laufbefehl ohne Kämpfen; Geologen: Rechtsklick auf ein Gebirge sucht dort; Träger: links Umwandlungsmenü statt Baumenü, wandelt bis zur Trägerreserve um, beschäftigte nach ihrer Lieferung; Linksklick: abwählen; rechts ziehen verschiebt die Karte), F12 gedrückt halten: 10-fache Geschwindigkeit, I KI spielt für dich (an/aus; die anderen Spieler sind immer KI), L KI-Entscheidungen in die Konsole, G Demo-Wirtschaft, P Platzier-Modus (Q/E Gebäude, Shift+Klick sofort fertig – Wohnhäuser
  entlassen dann Träger; nach dem Platzieren endet er, Rechtsklick bricht ab; Baustellen zeigen Schild und Pfosten);
  `--map <name>`, Standard: erste Mehrspieler-Karte; `--peoples amazon,roman,…`: Völker für Spieler, deren Volk die Karte offen lässt (Spieler i: Eintrag i mod Anzahl; ohne Angabe zufällig, Kartenvorgaben 0–3 gelten immer)).
  Figuren sehen aus wie ihr Volk: `SettlerAnimations` löst Typ/Aktion/Ware/Richtung über `data/settlers.json` auf
  (aus JSettlers' `movables*.txt` generiert, `tools/data-import/convert_jsettlers_movables.py`; nicht von Hand ändern). Capture-Läufe ignorieren Eingaben (Tastatur/Maus sind global) und laufen ohne VSync mit 4-facher Geschwindigkeit (verdecktes Fenster wird sonst gedrosselt). und `--scene walk` (Lauf-PoC). `--capture <dir>` rendert offscreen und speichert PNGs
  (zum Prüfen ohne Fenster; nur in temporäre Verzeichnisse). `HOMESTEAD_CAPTURE_ZOOM=4` und
  `HOMESTEAD_CAPTURE_FOCUS=<gebäude>` (oder `site`: eine frische Baustelle) für Nahaufnahmen, `HOMESTEAD_CAPTURE_BATTLE=1` lässt Feinde den Startturm angreifen, `HOMESTEAD_CAPTURE_GEOLOGISTS=1` wandelt Träger um und schickt Geologen ins nächste eigene Gebirge; am Ende gibt der Lauf Zähler und Gebäudezustände aus.
- `tools/Homestead.Arena` – KI gegen KI ohne Fenster: `dotnet tools/Homestead.Arena/bin/Release/net9.0/Homestead.Arena.dll
  [karte…] [--all] [--minutes 30] [--every 5] [--peoples amazon,asian] [--seed 1] [--log]`; offene Völker zufällig mit festem Seed; gibt je Spieler Land, Gebäude, Siedler, Vorräte und
  hängende Baustellen aus.
- `tools/Homestead.AssetTool` – CLI: `scan` (alle Bilder testweise dekodieren), `export`, `sheet` (PNG),
  `maps`, `resources` (Statistik der Bodenschätze), `minimap`.

## Kartenobjekte
`GameMap` speichert je Feld den Original-Objektcode (`MapObjectCodes`); Regeln (Begehbarkeit) und Grafik
leiten sich beide daraus ab. Laufzeitänderungen: Baumstumpf 41 (verschwindet nach `trees.stumpSeconds`), Setzling 84, Felsgrößen 115→127 (Geröll).
Getreide nutzt eigene Codes 200–207 (Wachstumsstufen, 207 reif), die in Originalkarten nicht vorkommen.
Bodenschätze (Typ + Menge je Feld) liegen ebenfalls in `GameMap` (`ResourceAt`).

## Grafik-Konventionen
- Raster → Pixel: `x*16 - y*8`, `y*9 - höhe*HeightStep` (Zeilen halb versetzt), 6 Nachbarrichtungen.
  Das Original nutzt HeightStep 2; wir halbieren das Relief (`ScreenProjection.HeightStep = 1`).
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
