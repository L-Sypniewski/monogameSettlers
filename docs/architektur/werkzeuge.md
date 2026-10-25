# Werkzeuge (`tools/`)

- `tools/Homestead.Arena` – KI gegen KI ohne Fenster: `dotnet tools/Homestead.Arena/bin/Release/net9.0/Homestead.Arena.dll
  [karte… | S3QOTA/MIS_M/Z/Z10.MAP | random:4:mirror:7] [--all] [--minutes 30] [--every 5] [--peoples amazon,asian] [--characters ,general] [--seed 1] [--log] [--notes] [--profile]
  [--until-decided] [--csv <datei>]`. Karten nach Name (unter `Map/`) oder Pfad in der Installation (Missionen);
  Charakterzug-Entscheidungen sofort (alle mit `--notes`); `--profile`: ms je Tick und Teil (`World.StepProfile`);
  `--seed` gibt Völkern und Welt (`World.Seed`) den Zufall: gleicher Seed, gleiche Partie. Ausgabe je Spieler: Land (frei, Wasseranteil), Gebäude,
  Mana, Priester, Siedler, Wohnraum/Streikende, Vorräte, hängende Baustellen. `--until-decided`: Ende, wenn nur einer Militär hat; `--csv` hängt Ergebnisse an.
- `tools/trait-tournament.py run|report` – Turnier der KI-Charaktere/-Züge über die Arena (jede Paarung, Karte, Volk, beide Startpositionen;
  parallel, fortsetzbar); `--preset traits|peoples`, `egyptian:hothead` nur mit seinem Volk, `--against`, `--vary pfad=a/b/c`;
  Ergebnisse `tournament-results/<preset>.csv` (nicht im Repo).
- `tools/Homestead.AssetTool` – `scan`, `export`, `sheet` (PNG), `frames` (alle Bilder einiger Sequenzen, je Zeile eine), `maps`, `resources`, `minimap`.
