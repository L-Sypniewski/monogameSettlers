# Kartenformat (`Map/**/*.map`)

Stand: alle 198 Karten der GOG Ultimate Collection lesbar. Abgeglichen mit JSettlers
(`jsettlers.logic/.../map/loading/original`). Alle Werte little endian.

## Aufbau
| Offset | Inhalt |
|--------|--------|
| 0 | Prüfsumme (u32) |
| 4 | Version: `0x0A` Original, `0x0B` Amazonen-Karten |
| 8 | Chunks bis Typ 0: `u32 typ` (untere 16 Bit), `u32 länge` inkl. 8-Byte-Kopf, Daten |

Chunk-Inhalte sind mit einem rollenden XOR-Schlüssel verschleiert (Startwert = Chunk-Typ):
`klar = byte ^ key; key = (key << 1) ^ klar` (nur die unteren 8 Bit zählen).
Das ist eine Verschleierung des Datenformats, kein Kopierschutz: Es gibt keinen Schlüssel und keine
Lizenzprüfung, das Spiel und der Editor lesen die Karten ohne Weiteres.

## Chunks
| Typ | Inhalt |
|-----|--------|
| 1 | Karteninfo: `u32 einzelspieler (1/0), u32 spielerAnzahl, u32 startRessourcen` |
| 2 | Spieler: je `u32 volk, u32 startX, u32 startY, char[33] name` |
| 3 | Teams (noch nicht gelesen) |
| 4 | Vorschaubild: `u16 größe, u16 ?`, dann RGB565 |
| 6 | Gelände: `u32 größe` (Karten sind quadratisch), dann je Punkt 6 Byte |
| 7 | Siedler: `u32 anzahl`, je `u8 partei, u8 typ, u16 x, u16 y` |
| 8 | Gebäude: `u32 anzahl`, je 12 Byte (Partei, Typ, x, y, Soldaten als Nibbles) |
| 9 | Warenstapel: `u32 anzahl`, je `u16 x, u16 y, u8 typ, u8 menge, 2 Byte ?` |
| 10 | Siegbedingungen |
| 11/12 | Questtext / Questtipp (C-Strings, Latin-1) |

## Geländepunkt (Chunk 6)
`höhe, landschaft, objekt, besitzer, begehbarkeit, ressource` (letzte: oberes Nibble Typ, unteres Menge).
Reihenfolge zeilenweise, `index = y * größe + x`, y = 0 ist der obere Kartenrand.

Landschaftswerte (Gruppe = oberes Nibble): 0–7 Wasser (Tiefen), 16 Gras, 17 Gebirgsrand außen,
20/21/23 Wüsten-/Moor-/Schlammrand außen, 32/33 Gebirge/-rand, 35 Schneerand außen, 48 Sand,
64/65 Wüste/-rand, 80/81 Moor/-rand, 96–99 Fluss, 128/129 Schnee/-rand, 144/145 Schlamm/-rand.
In JSettlers fehlen 66–71 und 112 – wir behandeln sie nach Gruppe. 66–71 kommen nur in
„384-2-Vulkanauge" vor, immer zusammen mit Objektcode 192 (Sonderterrain dieser einen Karte, vermutlich Lava).

## Objekte (Byte 3 des Geländepunkts)
Grafiken in GFX-Datei 01 (`MapObjectCatalog`). Namen aus JSettlers' `EOriginalMapObjectType`.

| Codes | Objekt | Sequenz / Bild | Status |
|-------|--------|----------------|--------|
| 1–8, 9–12, 13–20 | große Steine, Steine, Grenzsteine | 29 / Code−1 | bestätigt (8 Grenzsteine = Bilder 12–19) |
| 21–28 | kleine Steine | 30 / Code−21 | plausibel |
| 29–33, 34 | Wracks, Grab | 28 / Code−29 | bestätigt |
| 35–67 | Pflanzen, Pilze, Stümpfe, tote Bäume, Kakteen, Knochen, Blumen, Büsche, Schilf | 27 / Code−35 | bestätigt (33 Bilder, Reihenfolge passt) |
| 68–81 | Bäume (7 Arten × 2 Varianten) | 1 + (Code−68)/2·3 + (Code−68)%2, animiert | bestätigt (78/79 „Arecaceae" = Palmen 16/17) |
| 84 | kleiner Baum | 22 / 0 | plausibel |
| 111–114 | Riffe | 30 / Code−103 | unsicher |
| 115–127 | Steinbruch-Steine, 13 Größen | 31 / Code−115 | Richtung unsicher (JSettlers: 115 = voll, 127 = leer) |
| 192 | nur „Vulkanauge" | – | unbekannt |

Bäume: Je Art drei Sequenzen – zwei Wiegen-Animationen (17 Bilder) und eine für Fällen/Stumpf/Wachsen (13 Bilder).

## Darstellung
Siehe `Homestead.Client/Terrain`: zwei Dreiecke pro Rasterpunkt, Höhe hebt den Punkt um 2 px je Einheit.
Texturen aus einem Atlas der 235 Landschaftsbilder (GFX 00): durchgehende Texturen, wenn alle drei Ecken
gleich sind, sonst Übergangskacheln je Geländepaar. Tabellen aus JSettlers' `Background` übernommen.
