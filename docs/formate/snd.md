# SND format (`SND/Siedler3_00.dat`) and music

Status: verified against the GOG Ultimate Collection, cross-checked against JSettlers (`jsettlers.graphics/.../sound/SoundManager`).
All values little endian. Parser: `src/Homestead.Assets/Sound/SoundFile.cs`.

## Files
- `Siedler3_00.dat` (~11.9 MB): all sounds. Upper/lower case inconsistent.
- `siedler3_01.dat` (~160 KB): same header, contains only a few recordings. Not used.

## Header
| Offset | Content |
|--------|--------|
| 0x00 | Signature `44 15 01 00 02 00 00 00 00 00 00 00 1C 00 00 00` |
| 0x10 | File size |
| 0x14 | `FF FF FF FF` |
| 0x18 | `0x1C` |
| 0x1C, 0x20 | unknown |
| 0x24 | Table: one offset per sound to its variant list |

The table ends where the first variant list begins: (first offset − 0x24) / 4 = **119** sounds
(JSettlers reads 118).

## Variant list
`int32 count`, then per variant the `int32 offset` of the recording. The game picks one at random.

## Recording
| Offset | Content |
|--------|--------|
| 0 | Length of the whole entry (header + data; the next recording follows directly) |
| 4 | WAVEFORMAT: format 1 (PCM, `uint16`), channels 1 (`uint16`) |
| 8 | Sample rate 22050 |
| 12 | Bytes per second 44100 |
| 16 | Block size 2 (`uint16`), bits 16 (`uint16`) |
| 20 | Samples, signed 16 bit; (length − 20) / 2 of them (odd lengths: last byte left over) |

## Assignment
Bold: used in the game (`data/sound.json`). "Heard": recognised in the original by the project owner, otherwise from JSettlers.

| No. | Sound | No. | Sound |
|-----|----------|-----|----------|
| **0** | **Woodcutter** | **36** | **Falling tree** |
| **1** | **Builder** | **37/38** | **Iron smelter** (heard), also gold smelter |
| **2** | **Digger** | 39 | Pigs |
| **3** | **Stonecutter** | 40/41 | Donkey |
| **5** | **Sawmill** | 42 | Mill |
| **6/7** | **Smith** (tools/weapons) | 45 | Charcoal burner coughs |
| **8** | **Farmer sowing** (heard); 9, 12 farmer | **47/48** | **Order/share up/down** (heard) |
| 13 | Fire | **54** | **Switch on/off** (heard) |
| 14 | Butcher | **57** | **Command to soldiers** (heard; 58/59 same recording) |
| 15–17 | Fisherman (**16** used) | 62 | Click |
| 20 | Shipyard | **67** | **Desert** (ambience) |
| **21** | **Hospital: healing** (heard) | **68** | **Sea** (ambience) |
| **22** | **Work area moved** (heard) | **69/70** | **Birds** (ambience) |
| **24** | Geologist; **variants 1–3: large deposit** (heard) | **71** | **River** (ambience) |
| **25** | **Variant 1: pioneer sets a border post** (heard; JSettlers: bowman) | **72** | **Swamp** (bursting bubble) |
| **51** | **Click on the demolish icon** ("crack", heard) | | |
| **30** | **Swordsman** (blow) | **73/74** | **Wind** (ambience, mountains) |
| 31 | ? | **75** | **Casting a spell** (heard; JSettlers: thunder) |
| **32** | **Egyptian bow shot** (heard) | 76 | Rain |
| **33** | **Bowman** (shot, except Egyptians; heard) | 80 | "Ihr werdet angegriffen" |
| **34** | **Pikeman** (thrust) | 85 | Arrow shot |
| **35** | **Death** (9 variants) | 86–90 | Cannon? (87: short dull "clonk") |
| **113** | **Death of an Amazon** (4 variants) | **93** | **Building demolished/destroyed** (heard) |
| 117 | Bees | 106 | Tool missing |
| | | **109** | **Event: attack** (heard) |
| | | **110** | **Event: all others** (heard) |

## Music
`MUSIC/Track02.ogg` … `Track14.ogg` (Ogg Vorbis, the former CD audio tracks; not present in every edition).
Which track belongs to which people is documented nowhere – the assignment is in `data/sound.json` (`music.tracks`).
