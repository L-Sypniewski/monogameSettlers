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

Ressourcen-Typen (oberes Nibble), per Statistik über alle Karten bestätigt (`AssetTool resources`):
0 Fisch (nur Wasser), 1 Kohle, 2 Eisenerz, 3 Golderz, 4 Edelsteine, 5 Schwefel (Gebirge; Edelsteine
vereinzelt auch Wüste). Menge 0–15, ganz überwiegend 8 (Standard des Editors), danach 15.
Wert 0 = nichts.

Besitzer-Byte (Index 3): Spielernummer ab 0, 255 = niemand. Der Rand des Landes um Militärgebäude liegt
(im Kreismaß dx²+dy²−dx·dy) beim Turm bei 931, beim großen Turm bei 967, bei der Burg bei 1101 –
gemessen über alle Karten; manche Karten vergeben zusätzlich Land ohne Militärgebäude (Editor).

Siedlertypen (Chunk 7) nach JSettlers' `EOriginalMapSettlersType`: 0 Träger, 1 Planierer, 2 Bauarbeiter,
5/32/35 Schwertkämpfer Stufe 1/2/3, 8/30/33 Bogenschütze, 15/31/34 Pikenier, 23 Dieb/Späher (aus den Kampagnen:
Missions-CD J1 vier einzeln über die Karte verteilt – „Unsere Kundschafter“ –, Amazonen-Kampagne G11 57 zum Stehlen).
Reihenfolge zeilenweise, `index = y * größe + x`, y = 0 ist der obere Kartenrand.

Landschaftswerte (Gruppe = oberes Nibble): 0–7 Wasser (Tiefen), 16 Gras, 17 Gebirgsrand außen,
20/21/23 Wüsten-/Moor-/Schlammrand außen, 32/33 Gebirge/-rand, 35 Schneerand außen, 48 Sand,
64/65 Wüste/-rand, 80/81 Moor/-rand, 96–99 Fluss, 128/129 Schnee/-rand, 144/145 Schlamm/-rand.
In JSettlers fehlen 66–71 und 112 – wir behandeln sie nach Gruppe. 66–71 kommen nur in
„384-2-Vulkanauge" vor, immer zusammen mit Objektcode 192 (Sonderterrain dieser einen Karte, vermutlich Lava).

## Objekte (Byte 3 des Geländepunkts)
Grafiken in GFX-Datei 01 (`MapObjectCatalog`). Namen aus JSettlers' `EOriginalMapObjectType`.
Am 11.10.2026 optisch mit dem Original abgeglichen – keine Abweichung gefunden (auch Steinbruch-Steine).

| Codes | Objekt | Sequenz / Bild | Status |
|-------|--------|----------------|--------|
| 1–8, 9–12, 13–20 | große Steine, Steine, Grenzsteine | 29 / Code−1 | bestätigt (8 Grenzsteine = Bilder 12–19) |
| 21–28 | kleine Steine | 30 / Code−21 | plausibel |
| 29–33, 34 | Wracks, Grab | 28 / Code−29 | bestätigt |
| 35–67 | Pflanzen, Pilze, Stümpfe, tote Bäume, Kakteen, Knochen, Blumen, Büsche, Schilf | 27 / Code−35 | bestätigt (33 Bilder, Reihenfolge passt) |
| 68–81 | Bäume (7 Arten × 2 Varianten) | 1 + (Code−68)/2·3 + (Code−68)%2, animiert | bestätigt (78/79 „Arecaceae" = Palmen 16/17) |
| 84 | kleiner Baum | 22 / 0 | plausibel |
| 111–114 | Riffe | 30 / Code−103 | unsicher |
| 115–127 | Steinbruch-Steine, 13 Größen | 31 / Code−115 | optisch bestätigt (115 = voll, 127 = leer); 127 blockiert laut Karten nicht |
| 192 | nur „Vulkanauge" | – | unbekannt |

Bäume: Je Art drei Sequenzen – zwei Wiegen-Animationen (17 Bilder) und eine für Fällen/Stumpf/Wachsen (13 Bilder).

## Begehbarkeit
Byte 5 des Geländepunkts, Bit 0: 1 = nicht begehbar (Werte 8/9 selten, Bedeutung von Bit 3 offen).
Wir berechnen Begehbarkeit selbst aus Regeln (`GameMap`), weil sie sich im Spiel ändert; das Byte dient
als Kontrolle (`OriginalMapTests`, 99,8 % Übereinstimmung über alle Karten):
- Gelände: Wasser, Moor (+ innerer Rand), Schnee (+ innerer Rand) blockieren; Flüsse, Schlamm, äußere Ränder nicht.
- Objekte: Bäume blockieren ihr Feld, Steinbruch-Steine (115–126) ihr Feld plus die sechs Nachbarn,
  Code 127 (leerer Stein) und Dekoration blockieren nicht.
- Restabweichungen: Gelände um vorplatzierte Gebäude (kommt mit dem Laden der Gebäude),
  Palmen auf Sand (im Original begehbar, bei uns blockierend), Karte „Vulkanauge".

## Darstellung
Siehe `Homestead.Client/Terrain`: zwei Dreiecke pro Rasterpunkt, Höhe hebt den Punkt um 2 px je Einheit.
Texturen aus einem Atlas der 235 Landschaftsbilder (GFX 00): durchgehende Texturen, wenn alle drei Ecken
gleich sind, sonst Übergangskacheln je Geländepaar. Tabellen aus JSettlers' `Background` übernommen.
