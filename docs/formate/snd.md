# SND-Format (`SND/Siedler3_00.dat`) und Musik

Stand: an der GOG Ultimate Collection verifiziert, abgeglichen mit JSettlers (`jsettlers.graphics/.../sound/SoundManager`).
Alle Werte little endian. Parser: `src/Homestead.Assets/Sound/SoundFile.cs`.

## Dateien
- `Siedler3_00.dat` (~11,9 MB): alle Geräusche. Groß-/Kleinschreibung uneinheitlich.
- `siedler3_01.dat` (~160 KB): gleicher Kopf, enthält nur wenige Aufnahmen. Nicht genutzt.

## Kopf
| Offset | Inhalt |
|--------|--------|
| 0x00 | Signatur `44 15 01 00 02 00 00 00 00 00 00 00 1C 00 00 00` |
| 0x10 | Dateigröße |
| 0x14 | `FF FF FF FF` |
| 0x18 | `0x1C` |
| 0x1C, 0x20 | unbekannt |
| 0x24 | Tabelle: je Geräusch ein Offset auf seine Variantenliste |

Die Tabelle endet, wo die erste Variantenliste beginnt: (erster Offset − 0x24) / 4 = **119** Geräusche
(JSettlers liest 118).

## Variantenliste
`int32 Anzahl`, dann je Variante `int32 Offset` der Aufnahme. Das Spiel wählt eine zufällig.

## Aufnahme
| Offset | Inhalt |
|--------|--------|
| 0 | Länge des ganzen Eintrags (Kopf + Daten; die nächste Aufnahme folgt direkt) |
| 4 | WAVEFORMAT: Format 1 (PCM, `uint16`), Kanäle 1 (`uint16`) |
| 8 | Abtastrate 22050 |
| 12 | Bytes je Sekunde 44100 |
| 16 | Blockgröße 2 (`uint16`), Bits 16 (`uint16`) |
| 20 | Samples, signed 16 bit; (Länge − 20) / 2 Stück (ungerade Längen: letztes Byte übrig) |

## Belegung
Fett: im Spiel genutzt (`data/sound.json`). „Gehört“: vom Projektinhaber am Original erkannt, sonst aus JSettlers.

| Nr. | Geräusch | Nr. | Geräusch |
|-----|----------|-----|----------|
| **0** | **Holzfäller** | **36** | **fallender Baum** |
| **1** | **Bauarbeiter** | 37/38 | flüssiges Metall |
| **2** | **Planierer** | 39 | Schweine |
| **3** | **Steinmetz** | 40/41 | Esel |
| **5** | **Sägewerk** | 42 | Mühle |
| **6/7** | **Schmied** (Werkzeug/Waffen) | 45 | Köhler hustet |
| 8/9, 12 | Bauer | **47/48** | **Auftrag/Anteil hoch/runter** (gehört) |
| 13 | Feuer | **54** | **Schalter an/aus** (gehört) |
| 14 | Schlachter | **57** | **Befehl an Soldaten** (gehört; 58/59 gleiche Aufnahme) |
| 15–17 | Fischer (**16** genutzt) | 62 | Klick |
| 20 | Werft | 67 | Wüste |
| 21 | Heiler | 68 | Meer |
| **22** | **Arbeitsbereich versetzt** (gehört) | 69/70 | Vögel |
| 24 | Geologe | 71 | Fluss |
| 25 | Bogenschütze | 72 | Sumpf |
| **30** | **Schwertkämpfer** (Schlag) | 73/74 | Wind |
| 31 | ? | **75** | **Zaubern** (gehört; JSettlers: Donner) |
| **32** | **Bogenschuss der Ägypter** (gehört) | 76 | Regen |
| **33** | **Bogenschütze** (Schuss) | 80 | „Ihr werdet angegriffen“ |
| **34** | **Pikenier** (Stoß) | 85 | Pfeilschuss |
| **35** | **Tod** (9 Varianten) | 86–90 | Kanone? (87: kurzes dumpfes „klonk“) |
| **113** | **Tod einer Amazone** (4 Varianten) | **93** | **Gebäude abgerissen/zerstört** (gehört) |
| 117 | Bienen | 106 | Werkzeug fehlt |
| | | **109** | **Event: Angriff** (gehört) |
| | | **110** | **Event: alle anderen** (gehört) |

## Musik
`MUSIC/Track02.ogg` … `Track14.ogg` (Ogg Vorbis, die früheren CD-Audiospuren; nicht in jeder Ausgabe vorhanden).
Welche Spur zu welchem Volk gehört, ist nirgends dokumentiert – Zuordnung in `data/sound.json` (`music.tracks`).
