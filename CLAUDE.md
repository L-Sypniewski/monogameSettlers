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
  `Map/GameMap` (Gelände, Objekte, Begehbarkeit), `Pathfinding/PathFinder` (A*; Erreichbarkeit über `Regions`, die Begehbarkeitsänderungen aus `GameMap` nachführen statt die Karte neu einzuteilen), `World`/`Settler`
  (Bewegung: ein Feld je Figur, Tauschen, Ausweichen, Umweg, Watchdog-Durchlauf – siehe Kommentar in `World`).
  `Buildings/` (Definitionen aus `data/buildings/*.json`, Gebäude, `Construction`: Planierer/Bauarbeiter; `World.RemoveBuilding`: Abriss und Verlust mit Staubwolke (`DustClouds`),
  lässt den Anteil `demolition.payback` des Baumaterials liegen; das Land eines abgerissenen Militärgebäudes bleibt beim Spieler, aber ungeschützt: beansprucht es ein feindliches Militärgebäude (Besetzen, Eroberung), fallen die Gebäude darauf),
  `Economy/` (Waren, Stapel, `Logistics` mit Watchdog – je Ware erst die Zielart (Baustelle, Gebäude, Lager;
  Gebäudetyp nach `distribution`-Gewichten, z. B. Nahrung an Minen 90:10, Getreide/Wasser auf Mühle, Bäcker, Schweinefarm, Brauerei), dann das nächste Ziel mit Platz; ins Lager kommen nur die Waren aus `storage.materials`
  in `production.json` (Werkzeuge, Waffen, Gold), alles andere bleibt am Ausgabestapel des Erzeugers (8; voll → er wartet); `Problems` zählt Abbruchgründe; `Production`:
  Werkstätten, Minen (je Nahrungsstapel höchstens `mineFood.pileCapacity`, 4), Schmiede und Sammler (Wasserwerke schöpfen nur aus Flüssen; Bauer, Weinbauer, Reisbauer, Imker säen/ernten ihren Anbau aus `crops` – Wein wächst an Hängen schneller, Reis nur auf Sumpf, Reben und Bienenstöcke wachsen nach der Ernte neu), Werte aus `data/production.json`; `Housing`: Wohnhäuser
  entlassen Träger und geben so viel Wohnraum, wie sie entlassen haben (dazu die Startbevölkerung, `SettleStartPopulation`); Träger und Arbeiter (auch Planierer/Bauarbeiter) brauchen Platz, Soldaten und Geologen nicht; fehlt Platz, streiken entsprechend viele (`Settler.IsStriking`): erst untätige Träger (keine Aufträge), dann Arbeiter (bleiben im Gebäude); `Recruitment`: Träger
  holen ihr Werkzeug und werden Spezialisten – Tests ohne Werkzeugthema nutzen `TestConfig.NoTools`;
  `Idling`: untätige Träger, Soldaten, Planierer, Bauarbeiter, Geologen und arbeitslose Fachleute gehen aus dem Weg;
  `Geology`: Geologen laufen wie Soldaten zu einem Punkt (verteilt auf freie Felder); auf ein Gebirge geschickt, untersuchen sie Gebirgsfelder im Umkreis `geologists.searchRadius` um den Zielpunkt (nächstes freies Feld, bei Gleichstand zufällig) und setzen Schilder mit Rohstoff und Menge;
  wird ein Gebäude abgerissen, behält der Arbeiter Beruf und Werkzeug und übernimmt das nächste Gebäude seines Berufs),
  `Military/` (Kaserne bildet Soldaten aus, Militärgebäude rufen Soldaten nach ihrer Soll-Besatzung je Typ (`ChangeWanted`: neu 1 Schwertkämpfer; nie 0, nie mehr als Infanterie-/Bogenplätze; „+“ bei vollen Infanterieplätzen ersetzt die andere Infanterieart; überzählige kehren um oder kommen heraus, nie der letzte – ein Ersatz tauscht dann an der Tür), besetzte beanspruchen Land;
  `MilitaryCombat`: Angriff, Kampf, Verteidigung, Eroberung; nahe am Feind lokale Wegsuche (Radius `combatPathRadius`) um stehende/kämpfende Figuren herum;
  Kampfkraft (`CombatStrength`): Schaden im eigenen Land 100 %, sonst 100 % / Spielerzahl, steigt durch Goldbarren im eigenen Land – gezählt nach Landbesitz, erobertes Gold zählt also für den Sieger (Ägypter: Gold halb, Edelsteine doppelt) progressiv teurer (`unitsPerPercent` je `stepPercent`), höchstens 100 %; die KI kennt die Kampfkraft aller Spieler und bewertet beide Seiten am Kampfort (eigenes, neutrales, feindliches Land);
  Werte in `data/military.json`, Gebietsradien aus den Originalkarten gemessen). Siedler und Gebäude haben einen
  Spieler; Logistik/Rekrutierung bedienen nur den eigenen Spieler, `World.TerritoryRules` erzwingt eigenes Land (Bauplatz samt Tür, Stapeln, Bauarbeiterplätzen; Wege aller außer Soldaten und Geologen,
  Erreichbarkeit über Landgebiete je Besitzer in `PathFinder`; Gestrandete gehen heim).
  `Ai/` (`AiPlayer`: Computerspieler mit denselben Befehlen wie ein Mensch, kennt die ganze Karte; baut nach
  `data/ai.json` und Bedarf (Wohnhaus, solange Träger ≤ 3 × Produktionsgebäude; Größe nach fertigen Sägewerken/Steinmetzen je Volk; kein Material wird für Türme zurückgehalten; kein weiteres Gebäude einer Art, solange eines davon auf seinen Arbeiter wartet; Rohstoffgebäude ohne Platz an der besten Zelle probieren weitere, ein Wasserwerk ohne Platz lässt zum Fluss expandieren; reißt ein Wohnhaus ab, wenn danach noch `spareRoom` Plätze frei wären), expandiert mit Türmen gezielt zu fehlenden Rohstoffen, meidet Turmplätze mit mehr als `maxWaterShare` Wasser im neuen Land; `AiPlayer.Military`: setzt die Soll-Besatzung ihrer Militärgebäude auf voll (`Staff`), kennt alle feindlichen Soldaten,
  Verteidigung zuerst, greift kleinere Trupps an, wenn deutlich überlegen (laufend neu bewertet; Guerilla vor Zurückhalten: auch die Verteidiger dürfen angreifen, solange genug gegen die übrigen Trupps bleiben), die übrigen verteilt der Charakterzug `guardByThreat`
  zur Bewachung nach Bedrohung (ohne ihn, z. B. beim Charakter `standard`, bleiben sie stehen); `AiPlayer.Campaign`: sammelt Soldaten und erobert feindliche Militärgebäude bei 1,5:1;
  Charaktere und Charakterzüge (Begriffe und Konzept: `docs/KI-TACTICS.md`; Kennungen englisch, deutsche Namen in `ui.json` `aiCharacters`/`aiTraits`): ein Charakter (`AiCharacter`) in `ai.json` (`characters`) nennt Charakterzüge (`AiTrait`, `Ai/Traits/<Kennung>Trait`; Vorgaben unter `traits`, je Charakter überschreibbar) und darf allgemeine Werte überschreiben, `characterWeights` (je Volk) losen ihn je Partie aus; Charakterzüge hängen an Entscheidungspunkten: „Truppenführung“ (`LeadCampaign`: einer führt einen Feldzug) und „Bewachung“ (`Guard`: untätige Soldaten aufstellen); `pincer` (Charakter `general`, „Feldherr“): Finte vom Sammelpunkt, Hauptgruppe im Bogen zur Flanke, Seitenangriff; Notizen eines Charakterzugs beginnen mit seiner Kennung und erscheinen live (`AiPlayer.Noted`, `TraitOfNote`); Werte in `ai.json`, je Volk überschreibbar (`civilizations`, dort auch die Build-Order je Volk); `MapSurvey`: Kartenüberblick in Zellen;
  nach der Nahrungskette großer Tempel, Mana-Kette und kleiner Tempel; spät Goldmine/-schmelze (Ägypter: Edelsteinmine), Expansion zu Gold (`lateOres`) erst, wenn die Build-Order bis zur Mine erfüllt ist; Schmelzen erst mit eigener Mine ihres Erzes; Gebäude ohne Platz am Anker kommen in die geräumigste eigene Zelle; `AiPlayer.Magic`: Priester folgen Angriffen mit Abstand, ein Zauber, wo mindestens `magic.minTargets` getroffen werden; Aufwertungen sofort, wenn genug Mana erworben ist (Reihenfolge `magic.upgradeOrder`: Schwertkämpfer, Bogenschützen, Pikeniere); `World.AiPlayers` werden in jedem Tick angestoßen).
  `Magic/` (`Magic`: Mana je Spieler; kleiner Tempel macht aus Wein/Met/Bier/Schnaps Mana, großer Tempel schickt sofort und alle `priestSeconds` einen Priester seines Volks; `Cast`: Priester läuft bis Bogenschützen-Reichweite und zaubert, nur wo Soldaten zweier Spieler im Radius stehen, Kosten konstant; Zauber je Volk: Bekehren (Seite wechseln, Volk bleibt – `Settler.People`), Samuraischwert (schneller zuschlagen), Einfrieren, Zerstreuen; Priester sind angreifbar; Mana zahlt auf zwei Konten ein: Zauber verbrauchen `Mana`, Aufwertungen fragen nur `ManaEarned` (insgesamt erworben, `upgrades.manaEarned` je n-ter Aufwertung, nichts wird verbraucht); `Upgrade`: Soldatentypen am fertigen kleinen Tempel aufwerten, erst alle drei auf Stufe 2 (Feldwebel), dann auf 3 (Hauptmann); nur danach ausgebildete Soldaten bekommen die Stufe; Werte in `data/magic.json`).
  `OriginalScenario` (Karte → Welt mit Gebäuden, Stapeln, Siedlern; Mehrspieler-Karten ohne Gebäude bekommen
  über `StartSetup` für jeden Spieler die gleiche Startausstattung aus `data/start.json`).
- `data/` – Spieldaten (keine Original-Assets), wird neben jedes Programm kopiert. `production.json` ist
  unser eigenes Balancing (Zeiten, Rezepte, Steine je Felsgröße, Erz je Mengenstufe, Regeneration, `depletion`: wie viel Prozent der Ausbeute Steine, Berge, Fisch verbrauchen – 0 = unendlich). Gebäudedaten sind aus
  JSettlers generiert (`tools/data-import/`); nicht von Hand im generierten JSON ändern.
- `tests/Homestead.Simulation.Tests` – xUnit; Tests gegen Originalkarten laufen nur mit lokaler Installation
  (sonst bestehen sie ohne Prüfung). `dotnet test`
- `src/Homestead.Client` – MonoGame-Spiel. Szenen: `--scene map` (Standard, Originalkarte mit Figuren:
  Baumenü links (Kategorien wie im Original aus `data/ui.json`, Icon-Klick startet das Platzieren; Gebäude des eigenen Volks), Klick wählt/schickt Figuren, Klick auf ein Gebäude zeigt links statt des Baumenüs Name, Bild und (bei eigenen) Status, unten „Abreißen“ mit Rückfrage, bei Militärgebäuden darüber die Besatzung je Soldatentyp (da/Soll, +/−), beim kleinen Tempel darüber die Aufwertung je Soldatentyp (Bild der aktuellen Stufe, Rang, nötiges Mana insgesamt), Waren bzw. Baumaterial und bei Minen die Trefferquote der letzten 10 Gänge (deutsche Namen/Zustände in `data/ui.json`, Text über FontStashSharp mit einer Systemschrift) sowie den Arbeitsbereich (Rechtsklick versetzt ihn, `World.SetWorkCenter`: höchstens `MaxWorkCenterDistance` = 20 Felder vom Gebäude, weiter entfernt → nächstes erlaubtes Feld), N neuer Träger, M 50 Wanderer, K Testsoldaten (Shift: Spieler 2), F Angriff auf das feindliche
  Militärgebäude unter dem Mauszeiger, Rahmen ziehen oder Klick wählt eigene Figuren mit Vorrang Soldaten > Priester > Geologen > untätige Träger, andere Berufe nie (beim Rahmen geht ein Gebäude – das der Rahmenmitte nächste – den Trägern vor; ein gewähltes Gebäude bekommt einen weißen Eckrahmen, 3 px), Shift fügt hinzu; links zeigt dann `SelectionPanel` je Art (Typ/Stufe) Bild und Anzahl, unten „Kampfkraft: xx%“; Strg+1–9 speichert die Auswahl als Gruppe (Icon oben rechts, Klick darauf oder 1–9 wählt sie wieder) (Soldaten: Rechtsklick hinschicken/angreifen, Strg+Rechtsklick erzwungener Laufbefehl ohne Kämpfen; Geologen: Rechtsklick schickt sie hin wie Soldaten, auf ein Gebirge suchen sie dort; Priester: links Zaubermenü (Name, Kosten, Mana), Klick darauf und dann auf einen Kampf zaubert, Rechtsklick schickt sie hin; Träger: links Umwandlungsmenü statt Baumenü, wandelt bis zur Trägerreserve um, beschäftigte nach ihrer Lieferung; Linksklick: abwählen; rechts ziehen verschiebt die Karte), F11 Vollbild an/aus, F3 Leistungsanzeige (FPS, ms für Simulation/Update/Zeichnen, Draw-Calls; Capture-Läufe geben sie mit den PNGs aus), F12 gedrückt halten: 10-fache Geschwindigkeit, I KI spielt für dich (an/aus; die anderen Spieler sind immer KI), L KI-Entscheidungen in die Konsole, G Demo-Wirtschaft, P Platzier-Modus (Q/E Gebäude, Shift+Klick sofort fertig – Wohnhäuser
  entlassen dann Träger; nach dem Platzieren endet er, Rechtsklick bricht ab; Baustellen zeigen Schild und Pfosten);
  `--map <name>`, Standard: erste Mehrspieler-Karte; `--peoples amazon,roman,…`: Völker für Spieler, deren Volk die Karte offen lässt (Spieler i: Eintrag i mod Anzahl; ohne Angabe zufällig, Kartenvorgaben 0–3 gelten immer); `--characters ,general`: KI-Charakter je Spieler (Eintrag i für Spieler i+1, leer: ausgelost), Entscheidungen der Charakterzüge erscheinen mit deutschem Namen unten als Meldung und in der Konsole).
  Figuren sehen aus wie ihr Volk: `SettlerAnimations` löst Typ/Aktion/Ware/Richtung über `data/settlers.json` auf
  (aus JSettlers' `movables*.txt` generiert, `tools/data-import/convert_jsettlers_movables.py`; nicht von Hand ändern). Capture-Läufe ignorieren Eingaben (Tastatur/Maus sind global) und laufen ohne VSync mit 4-facher Geschwindigkeit (verdecktes Fenster wird sonst gedrosselt). und `--scene walk` (Lauf-PoC). `--capture <dir>` rendert offscreen und speichert PNGs
  (zum Prüfen ohne Fenster; nur in temporäre Verzeichnisse). `HOMESTEAD_CAPTURE_ZOOM=4` und
  `HOMESTEAD_CAPTURE_FOCUS=<gebäude>` (oder `site`: eine frische Baustelle; `temple` setzt einen kleinen Tempel mit Mana) für Nahaufnahmen, `HOMESTEAD_CAPTURE_BATTLE=1` lässt Feinde den Startturm angreifen, `HOMESTEAD_CAPTURE_STRIKE=1` reißt das fokussierte Gebäude ab und setzt 10 Träger ohne Wohnraum dazu, `HOMESTEAD_CAPTURE_GEOLOGISTS=1` wandelt Träger um und schickt Geologen ins nächste eigene Gebirge, `HOMESTEAD_CAPTURE_SELECT=1` wählt eigene Soldaten aller Typen und Stufen aus, `HOMESTEAD_CAPTURE_MAGIC=1` baut großen Tempel und Mana-Kette, gibt Mana und lässt den Priester in einen Kampf zaubern; am Ende gibt der Lauf Zähler und Gebäudezustände aus.
- `tools/Homestead.Arena` – KI gegen KI ohne Fenster: `dotnet tools/Homestead.Arena/bin/Release/net9.0/Homestead.Arena.dll
  [karte…] [--all] [--minutes 30] [--every 5] [--peoples amazon,asian] [--characters ,general] [--seed 1] [--log] [--notes] [--profile]`; Entscheidungen der Charakterzüge (mit `--notes` alle) werden sofort ausgegeben, `--profile` misst ms je Tick und Teil der Simulation (`World.StepProfile`, mit längstem Tick); offene Völker zufällig mit festem Seed; gibt je Spieler Land (frei, Wasseranteil), Gebäude, Mana, Priester, Siedler, Wohnraum/Streikende, Vorräte und
  hängende Baustellen aus.
- `tools/Homestead.AssetTool` – CLI: `scan` (alle Bilder testweise dekodieren), `export`, `sheet` (PNG),
  `maps`, `resources` (Statistik der Bodenschätze), `minimap`.

## Kartenobjekte
`GameMap` speichert je Feld den Original-Objektcode (`MapObjectCodes`); Regeln (Begehbarkeit) und Grafik
leiten sich beide daraus ab. Laufzeitänderungen: Baumstumpf 41 (verschwindet nach `trees.stumpSeconds`), Setzling 84, Felsgrößen 115→127 (Geröll).
Anbau nutzt eigene Codes, die in Originalkarten nicht vorkommen: Getreide 200–207 (207 reif), Wein 208–210, Reis 211–214, Bienenstöcke 215–218 (Bild: vorerst Blumen).
Bodenschätze (Typ + Menge je Feld) liegen ebenfalls in `GameMap` (`ResourceAt`).

## Grafik-Konventionen
- Raster → Pixel: `x*16 - y*8`, `y*9 - höhe*HeightStep` (Zeilen halb versetzt), 6 Nachbarrichtungen.
  Das Original nutzt HeightStep 2; wir halbieren das Relief (`ScreenProjection.HeightStep = 1`).
- Gelände-Atlas: Texturkoordinaten immer einen halben Texel innerhalb der Atlas-Zelle halten,
  sonst entstehen Linien aus Nachbarzellen.
- Sprites (Figuren, Gebäude, Kartenobjekte) über `SpriteLibrary` laden: alle Bilder in gemeinsamen Atlas-Seiten (`SpriteAtlas`),
  sonst kostet jedes Sprite einen eigenen Draw-Call. Keine eigenen `Texture2D` je Bild in der Welt.
- Lauf-Sequenzen: 72 Frames = 6 Richtungsblöcke à 12 (SW, W, NW, NE, O, SO); Frame 0 im Block = stehen,
  1–11 = Laufzyklus (ein Zyklus pro Rasterschritt).

## Installation des Originals
Pfad über `HOMESTEAD_GAME_DIR` oder `~/.config/homestead/settings.json` (`{"gameDirectory": "..."}`).
Lokal: GOG Ultimate Collection unter `~/Games/gog/the-settlers-iii-ultimate-collection/drive_c/GOG Games/Settlers 3 Ultimate`.

## Bauen/Starten
- `dotnet build Homestead.sln`
- Programme mit `dotnet <pfad>.dll` bzw. `dotnet run --project …` starten – der native Apphost findet
  .NET in der Flatpak-Umgebung von VS Code nicht.
