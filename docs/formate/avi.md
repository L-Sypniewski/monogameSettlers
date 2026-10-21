# Videos (AVI)

Zwischensequenzen der Kampagnen liegen als `.DAT` in `S3/ADD/001` (Siedler 3), `S3QOTA/ADD/1` (Amazonen).
Es sind gewöhnliche RIFF-AVI-Dateien: 320 × 240, 25 fps, Bild **Intel Indeo 5** (`IV50`), Ton unkomprimiertes PCM
(16 Bit, Stereo, 22 050 Hz). Bild und Ton sind in `LIST rec `-Blöcken verschränkt (Bild `00dc`, Ton `01wb`).

Lesen: `Homestead.Assets.Video.AviFile` (Kopf `avih`: µs je Bild, Bildanzahl, Breite, Höhe; `strf` des Tonstroms:
`WAVEFORMATEX`; `ReadAudio` hängt alle `NNwb`-Chunks aneinander). Das Bild dekodiert der Client über ein
installiertes **ffmpeg** (eigener Prozess, `VideoPlayer`), ohne ffmpeg wird das Video übersprungen.

## Siedler 3 (`S3/ADD/001`)

Name `AA_VV_NN`: `01_00_00` Spielintro (260 s); `02_VV_NN` Kampagne, Volk `VV` (01 Asiaten, 02 Ägypter, 03 Römer),
`NN` 01 Intro, 02/03 Zwischensequenzen, 04 Abschluss (~90 s). `03_01_01–03` sind gleich groß wie `02_01_01/02/04`.
