# Homestead – Nachbau von „Die Siedler 3" (Arbeitstitel)

## Vorhaben
Privater Nachbau von „Die Siedler 3" (Blue Byte, 1998), der die Original-Grafiken und -Sounds nutzt.
Anlass: Das Original ist verbugt und wird seit Langem nicht mehr weiterentwickelt. Das Spiel ist
mehrfach in meinem Besitz (Original, Collector's Edition, GOG u. a.).

## Rahmenbedingungen
- **Engine:** MonoGame (C#, .NET 9)
- **Nur Singleplayer**, kein Multiplayer
- **Closed Source**, rein privat, keine Veröffentlichung geplant
- **Hauptziele gegenüber dem Original:**
  1. Starke KI
  2. Keine festlaufenden Träger (insbesondere beim KI-Gegner)
  3. Gebirge, die nicht so schnell leer gehen

## Rechtliche Leitplanken (verbindlich)
- **Keine Original-Assets im Repo, im Build oder in Downloads.** Die Engine liest GFX/SND zur Laufzeit aus
  der lokalen Installation (Pfad konfigurierbar, siehe README).
- **Kein Dekompilieren oder Disassemblieren der Original-EXE/-DLLs**, kein Übernehmen von Originalcode.
  Die Spiellogik wird nach Beobachtung und Spielkenntnis neu geschrieben (Clean-Room-Ansatz).
- **Dateiformate analysieren ist okay** (GFX, SND, Karten): Reverse Engineering an Daten, nicht am Programm.
- **Keinen Kopierschutz/DRM umgehen.**
- Code aus JSettlers (MIT-Lizenz) darf als Referenz dienen oder übernommen werden; dann den
  MIT-Lizenzhinweis in `THIRD_PARTY_NOTICES.md` erhalten.
- Falls das Projekt je geteilt wird: kein „Siedler" im Namen (Marke von Ubisoft).

## Referenz: JSettlers
- Aktiver Fork: https://github.com/paulwedeck/settlers-remake (Java, MIT, Alpha-Status)
- Liest GFX/SND aus einer Original-Installation und enthält Parser für die Dateiformate –
  **dafür** als Referenz nutzen.
- Als Spiel nicht überzeugend. Positiv: Die KI verhält sich spürbar anders als im Original.

## Design-Ansätze für die Hauptziele
- **Träger-Logistik:** Träger planen ihren Weg neu, wenn er blockiert ist. Ein Watchdog erkennt Träger,
  die sich zu lange nicht bewegen, und löst den Fall auf (neu planen, Ware ablegen o. Ä.).
  Gilt für alle Spieler – stabile Logistik ist auch Voraussetzung für eine starke KI.
- **Rohstoffe im Gebirge:** Vorrat pro Feld ist ein Einstellwert (höher als im Original),
  optional mit langsamer Regeneration.
- **KI:** plant Warenketten und reagiert auf Engpässe, statt feste Bauabfolgen abzuspulen.
  Baut auf einer stabilen Wirtschaftssimulation auf und kommt daher zuletzt.

## Architektur-Grundsätze
- Spielsimulation strikt vom Rendering getrennt, feste Tick-Rate → KI und Logistik lassen sich
  ohne Grafik testen und debuggen.
- Balancing-Werte (Rohstoffmengen, Produktionszeiten usw.) datengetrieben in Konfigurationsdateien,
  nicht im Code.
- Asset-Zugriff hinter einer eigenen Schicht gekapselt (`Homestead.Assets`: Parser → eigene Typen,
  keine MonoGame-Abhängigkeit).

## Roadmap
1. **Asset-Viewer:** GFX-.dat-Dateien parsen und in MonoGame anzeigen. ✓ (Reste offen)
   - [x] Installation gefunden: GOG Ultimate Collection (Lutris)
   - [x] GFX-Format analysiert, mit JSettlers abgeglichen ([formate/gfx.md](formate/gfx.md))
   - [x] C#-Parser: alle 151.088 Bilder aller GFX-Dateien dekodieren fehlerfrei
   - [x] MonoGame-Viewer zum Durchblättern
   - [ ] Animationstabellen (Typ `0x21702`) verstehen
   - [ ] Zuordnung Sequenz-Nummer → Bedeutung (welcher Siedler, welches Gebäude …)
   - [x] Torso/Schatten korrekt zugeordnet (Soldaten vollständig)
   - [ ] SND-Format
   - [x] Mini-PoC Bewegung: Träger läuft ein Sechseck auf dem Raster, alle 6 Richtungen,
     feste Tick-Rate (20/s) mit Interpolation (`Homestead.Simulation` + `Homestead.Client`)
2. **Karte & Gelände:** Landschaft rendern, Kamera, Kartenformat. ← *in Arbeit*
   - [x] Kartenformat gelesen, alle 198 Karten laden ([formate/map.md](formate/map.md))
   - [x] Gelände mit Höhen, Texturen und Übergängen, Kamera (Scrollen, Zoom), Kartenwechsel
   - [ ] Kartenobjekte (Bäume, Steine …), Siedler/Gebäude/Waren aus der Karte
   - [ ] Unbekannte Landschaftswerte 66–71, 112 klären
   - [ ] Begehbarkeit → Simulation, Figuren folgen dem Gelände
3. **Gebäude & Träger:** Bauen, Wege, Wegfindung, Logistik inkl. Watchdog.
4. **Wirtschaft:** Warenketten, Produktion, Rohstoffe.
5. **Kampf**
6. **KI**
