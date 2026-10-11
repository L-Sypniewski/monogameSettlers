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
- `src/Homestead.Client` – MonoGame-Spiel. Szenen: `--scene map` (Standard, Gelände einer Originalkarte,
  `--map <name>`) und `--scene walk` (Lauf-PoC). `--capture <dir>` rendert offscreen und speichert PNGs
  (zum Prüfen ohne Fenster; nur in temporäre Verzeichnisse).
- `tools/Homestead.AssetTool` – CLI: `scan` (alle Bilder testweise dekodieren), `export`, `sheet` (PNG).

## Grafik-Konventionen
- Raster → Pixel: `x*16 - y*8`, `y*9 - höhe*2` (Zeilen halb versetzt), 6 Nachbarrichtungen.
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
