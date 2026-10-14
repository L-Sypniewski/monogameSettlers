# GFX-Format (`GFX/siedler3_NN.<maske>.dat`)

Stand: an der GOG Ultimate Collection verifiziert, abgeglichen mit JSettlers
(`jsettlers.graphics/.../image/reader`). Alle Werte little endian.

## Dateien
- Jede Nummer liegt in zwei Farbvarianten vor: `.7c003e01f.dat` = RGB555, `.f8007e01f.dat` = RGB565
  (Suffix = Farbmasken R/G/B). Inhalt sonst gleich. Wir nutzen 565.
- Groß-/Kleinschreibung der Namen ist uneinheitlich (`Siedler3_` vs. `siedler3_`).
- **Nr. 18 ist keine Grafik**, sondern eine Windows-DLL (`MZ`). Wird übersprungen, nicht analysiert.
- **Nr. 07/08/09** haben keine Bilder, aber einen großen Block `0x1904` direkt hinter dem Header
  (z. B. 399 × 8 Einträge) – vermutlich Texttabellen in 8 Sprachen. Noch nicht untersucht.

Grobe Belegung (visuell bestätigt: 00, 12, 13; Rest Vermutung anhand der Tabellengrößen und JSettlers):

| Nr. | Inhalt |
|-----|--------|
| 00 | Landschaftstexturen (235) |
| 01 | Siedler-Animationen allgemein, mit Schatten |
| 02, 03, 14, 24, 34, 44 | GUI-Grafiken |
| 10/20/30/40 | Siedler je Volk (Römer/Ägypter/Asiaten/Amazonen), mit Torso und Schatten |
| 11/21/31/41 | weitere Siedler-Animationen je Volk |
| 12/22/32/42 | Militär je Volk: Kriegsmaschine, Soldaten (JSettlers: Sequenz 14–29) |
| 13/23/33/43 | Gebäude je Volk (Römer: Sequenz 0–45, Frame 0 = fertig, 1 = Baustelle), danach Effekte (Feuer, Rauch, Magie) |
| 15/25/35/45 | nur Animationstabellen (`0x21702`) |

## Header
| Offset | Inhalt |
|--------|--------|
| 0 | Signatur `04 13 04 00` |
| 33 / 37 | Farbmasken (`7c`/`03` = 555, `f8`/`07` = 565) |
| 48 | Dateilänge (u32), muss der echten Länge entsprechen |
| 52 | 8 × u32 Offsets auf Blöcke |

## Blöcke
`u32 typ, u16 byteCount, u16 anzahl, u32[anzahl] offsets`, bei Offset-Tabellen gilt
`byteCount == anzahl*4 + 8`.

| Typ | Inhalt | Eintrag zeigt auf |
|-----|--------|-------------------|
| `0x1904` | Header-Ende / Texttabelle (07–09) | – |
| `0x2412` | Landschaft | Bitmap (Landschafts-Header) |
| `0x2607` | Palette? (immer leer, 4 Byte extra) | – |
| `0x106` | Siedler/Gebäude (Farbe) | Sequenz |
| `0x3112` | Torso (Spielerfarbe) | Sequenz |
| `0x5982` | Schatten | Sequenz |
| `0x11306` | GUI | Bitmap (GUI-Header) |
| `0x21702` | Animationstabellen | noch unbekannt |

Torso- und Schatten-Tabellen sind oft kürzer als die Siedler-Tabelle und **nicht** per Index zugeordnet.
Regeln aus JSettlers (`AdvancedDatFileReader.initialize`) übernommen, siehe `GfxFile.AlignLayers`:
- Torso: fehlende Einträge liegen am Anfang (Torso `i - (siedler - torsos)`).
- Schatten: bei Differenz 7 oder 8 fehlen sie am Anfang, sonst am Ende.
- Sonderfälle, erkannt an den Tabellengrößen: Gebäude (Differenz 26, Dateien 13/43), Schiffe (28, Datei 36),
  römische Spezialisten (239 Einträge, Datei 11).
- Offen: in Datei 12 wirken die Schatten der Sequenzen 26–33 nicht stimmig.

Einzelne Sequenzen (visuell bestätigt):
- Datei 01, Sequenz 100–105: fliegender Pfeil je Richtung (SW, W, NW, NE, O, SO), 3 Frames: steigend, waagrecht, fallend.
- Dateien 12/22/32/42: hinter `BOWMAN_L3`/`ACTION1` folgt eine Sequenz mit 22 Frames (Farbebene leer), dann 6 Sequenzen
  à 2 Frames: Infanterie im Turmfenster (Schwertkämpfer, dann Pikeniere, je Stufe 1–3; Frame 0 blickt nach links,
  1 nach rechts). Römer/Asiaten 28–33, Ägypter 33–38, Amazonen 26–31.
- Bogenschützen `ACTION1` (24 Frames je Richtung): der Pfeil verlässt den Bogen nach Frame 17.

## Sequenz
`02 14 00 00 08 00 00`, `u8 frameAnzahl`, dann `u32[frameAnzahl]` Offsets **relativ zum Sequenzanfang**.

## Bitmap-Header
- Siedler/Torso/Schatten: `u32 0x0c, u16 breite, u16 höhe, s16 offsetX, s16 offsetY`
- Landschaft: `u16 breite, u16 höhe, u16 ?`
- GUI: `u16 breite, u16 höhe, u16 ?, u16 ?`

Danach auf gerade **absolute** Dateiposition auffüllen (1 Byte Padding).

## Pixeldaten
Zeilenweise, pro Zeile eine Folge von Runs mit u16-Steuerwort:
- Bit 0–7: Anzahl folgender Pixel
- Bit 8–14: davor zu überspringende transparente Pixel
- Bit 15: letzter Run der Zeile

Pixel: Farbe = u16 (555/565), Torso = u8 (untere 5 Bit = Helligkeit, mit Spielerfarbe multipliziert),
Schatten = keine Daten (halbtransparentes Schwarz).
Kleine Landschaftskacheln nutzen Cyan als Hintergrund/Schlüsselfarbe.
