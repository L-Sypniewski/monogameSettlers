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
   - [x] Kartenobjekte: Bäume (animiert), Steine, Pflanzen, Wracks …, tiefensortiert mit Figuren
   - [x] Objekte optisch mit dem Original abgeglichen
   - [ ] Siedler/Gebäude/Waren aus der Karte
   - [x] Unbekannte Landschaftswerte 66–71 geklärt (nur Karte „Vulkanauge"); 112 offen
   - [x] Begehbarkeit in der Simulation (Regeln, mit Kartendaten abgeglichen), Debug-Overlay (Taste B)
   - [x] A*-Wegsuche mit Regionen für unerreichbare Ziele, Testprojekt (Referenzvergleich, Originalkarten)
   - [x] Wegsuche im Client: Figur anklicken/Ziel anklicken, Figur läuft und folgt den Geländehöhen
   - [x] Mehrere Figuren gleichzeitig: ein Feld je Figur, Tauschen bei Gegenverkehr, Ausweichen,
     Umweg nach kurzer Wartezeit, Watchdog lässt nach 2 s durchlaufen (nie festlaufen; Stresstests)
   - [ ] Durchlauf-Quote bei sehr dichtem Verkehr senken (Tuning, sobald echte Träger laufen)
3. **Gebäude & Träger:** ← *in Arbeit*
   - [x] Gebäudedaten aller Völker als JSON (`data/buildings`, aus JSettlers konvertiert)
   - [x] Gebäude, Warenstapel und Siedler aus Karten laden und darstellen
   - [x] Logistik: Anfragen/Angebote, Träger-Aufträge mit Reservierung, Watchdog (Ware ablegen),
     Lager nimmt alles, Produktionsbedarf vor Lager; Tests inkl. Warenerhaltung
   - [x] Bauen: Baustellen mit Platzierungsprüfung, Planierer ebnen ein (Gelände ändert sich live),
     Bauarbeiter verbauen gelieferte Materialien, Baufortschritt sichtbar; Platzier-Modus im Client (P)
   - [x] Lager als Quelle (Waren aus dem Lager an Baustellen und Gebäude); Reihenfolge Baustelle → Produktion → Lager
   - [x] Weitere Festlauf-Fälle gefunden und behoben (gegenseitiges Wegschieben an Stapeln, Kreisen um Gedränge)
   - [x] Untätige Träger verteilen sich (verlassen Lagerhof, Gebäudeumfeld und Gedränge; bleiben sofort verfügbar)
   - [x] Planierer/Bauarbeiter entstehen aus Trägern + Werkzeug (Schaufel/Hammer)
   - [x] Gebäude anklicken: Zustand (wartet auf Ware/Werkzeug, nichts in Reichweite, …) und Arbeitsbereich
     (Original-Markierung, vier Kreise); Fähnchen auf besetzten Gebäuden
   - [ ] Arbeitsbereich verschieben
   - [x] Wohnhäuser entlassen Träger (klein 10, mittel 30, groß 100; einer pro Sekunde, einstellbar)
   - [x] Startausstattung auf Mehrspieler-Karten: Wachturm, Waren, Siedler je Startstufe der Karte, gleich für alle
     Spieler (`data/start.json`); Einzelspieler-Karten bringen ihre Gebäude selbst mit
   - [ ] Auf- und Ablegen animieren; Wege (beschleunigen das Laufen)
   - [x] Baustellen: Tür und Stapelplätze müssen frei sein (sonst nie belieferbar); Lager hält eine Ware zusammen
   - [x] Lager nimmt beliebig viele Warenarten (öffnet bei Bedarf weitere Stapelplätze ringsum)
   - [ ] Fußabdrücke: JSettlers-Daten sind etwas großzügiger als die Begehbarkeit der Originalkarten
4. **Wirtschaft:** Warenketten, Produktion, Rohstoffe. ← *begonnen*
   - [x] Produktions-Balancing als Datei (`data/production.json`, eigene Werte)
   - [x] Werkstätten (Sägewerk, Mühle, Bäcker, Schlachter, Schmelzen, Köhler, Schweinezucht)
   - [x] Holzfäller (Baumstümpfe bleiben), Steinmetz (Felsen schrumpfen bis zum Geröll, Ertrag je Größe
     einstellbar), Förster (Setzlinge wachsen nach)
   - [x] Arbeiter werden aus Trägern rekrutiert; Watchdog für Arbeiter
   - [x] Kreislauf getestet: Wirtschaft baut ein Gebäude nur aus eigener Produktion
   - [x] Bodenschätze aus den Originalkarten (Fisch, Kohle, Eisen, Gold, Edelsteine, Schwefel)
   - [x] Minen: je Erz eine Einheit Nahrung (welche, bestimmen die Eingangsstapel je Volk); Ertrag je
     Mengenstufe (`orePerAmount`) und Regeneration (`regenerateSeconds`) einstellbar → Ziel 3
   - [x] Bauernhof (Felder wachsen in Stufen), Fischer (Fischbestand nimmt ab), Wasserwerk
   - [x] Kreislauf getestet: Bauernhof + Wasserwerk → Mühle → Bäcker → Kohlemine
   - [ ] Winzer, Imker, Reisbauer, Eselzucht
   - [ ] Minen-Anzeige (Vorrat/erschöpft) im Spiel
   - [x] Werkzeuge: Träger werden nur mit Werkzeug zu Spezialisten (Tabelle in `production.json`);
     wird ihr Gebäude abgerissen, behalten sie Beruf und Werkzeug und übernehmen das nächste Gebäude ihres Berufs
   - [x] Werkzeug- und Waffenschmied: fehlende Werkzeuge zuerst, sonst kleiner Vorrat je Ware
   - [ ] Schmied-Auswahl durch den Spieler (Wunschliste/Anteile)
5. **Kampf** ← *begonnen*
   - [x] Soldaten: Kaserne macht aus Träger + Waffe Schwertkämpfer/Pikenier/Bogenschütze; Kartensoldaten mit Stufe
   - [x] Militärgebäude (Turm, großer Turm, Burg) werden besetzt, Soldaten stehen sichtbar auf den Plätzen
   - [x] Gebiet: besetzte Gebäude beanspruchen Land (Radien aus den Originalkarten, 98,8 % deckungsgleich),
     Grenzpfähle in Spielerfarbe, Bauen nur auf eigenem Land
   - [x] Spielertrennung in Logistik, Rekrutierung, Baustellen
   - [x] Angriff (Soldaten aus Gebäuden in Reichweite, einer bleibt), Nahkampf, Bogenschützen (auch vom Turm),
     Verteidiger an der Tür, Eroberung mit Gebietswechsel; Gebäude auf verlorenem Land gehen unter
   - [ ] Kampfwerte an Originalverhalten angleichen (derzeit eigene Werte in `data/military.json`)
   - [x] Soldaten wechseln auf nähere Gegner (Nachbarfeld sofort); Rahmenauswahl, Laufbefehl mit Vorrang für Feinde
   - [x] Kampf-Wegsuche im Umkreis (`combatPathRadius`) um stehende/kämpfende Figuren herum; Laufende greifen in Sichtweite an
   - [ ] Angriffs-Auswahl wie im Original (Anzahl/Typen wählen), Sterbe-Animation
   - [ ] Soldaten-Stufen durch Gold/Tempel, Heiler
6. **KI** ← *begonnen*
   - [x] Gerüst: `Ai/AiPlayer` je Computerspieler, gleiche Befehle wie ein Mensch, kennt die ganze Karte
     (kein Erkunden, wie im Original); Werte und Bauliste in `data/ai.json`
   - [x] Wirtschaft: Bauliste plus Bedarf (Wohnhaus bei Trägermangel, Werkzeugschmied bei fehlendem Werkzeug,
     Nahrung für hungrige Minen, neuer Steinmetz/Holzfäller, wenn die alten nichts mehr finden); Standort
     am Rohstoff bzw. nahe der Heimat; Vorrat für einen Steinmetz bleibt immer übrig; Abriss erschöpfter Gebäude
   - [x] Strategische Expansion (nicht im Original): Türme gezielt Richtung fehlender Steine, Erze, Bäume,
     Wasser oder Platz – damit kein Spiel mangels Gebirge/Steinen im Startgebiet stehen bleibt
   - [x] Lagerhaus erst, wenn ein Schmied/die Goldschmelze in Menge produziert (eingelagert werden nur Werkzeuge,
     Waffen, Gold – einstellbar in `production.json`; Lagerstapel fassen 8)
   - [x] `tools/Homestead.Arena`: KI gegen KI ohne Fenster auf allen Mehrspieler-Karten, Kennzahlen und
     hängende Baustellen
   - [ ] Militär: verteidigen, angreifen, wenn überlegen
   - [ ] Feinere Wirtschaftssteuerung (Anteile statt fester Liste, Waffen/Gold, Mehrzweck-Lager)
