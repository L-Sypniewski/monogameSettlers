# Homestead

Privater Nachbau von „Die Siedler 3" in MonoGame/C#. Ziele, Roadmap: [docs/PROJEKT.md](docs/PROJEKT.md).
Kommunikation Deutsch; Code und Kommentare Englisch.
Spieltexte nie im Code: `data/lang/de.json` und `en.json` (verschachtelt, Schlüssel mit Punkten, Platzhalter `{0}`), Zugriff `Lang.T("schlüssel", args)`
im Client; Namen aus Daten nach ID (`buildings.<name>`, `goods.<GOOD>`, `peoples.<People>`, `soldiers.types.*`, `spells.<Kind>`, `events.texts.<id>`,
`ai.characters/traits.*`, `panel.tabs/menus.*`, `buildingStatus.*`). Fehlt Englisch → Deutsch → Schlüssel (Konsole „Missing text“). `LanguageTests`:
gleiche Schlüssel/Platzhalter, jeder wörtliche Schlüssel im Client, jede Daten-ID. Entwickler-Hinweise (`_message`, Fenstertitel, Konsole) englisch, unübersetzt.

## Verbindliche Regeln
- Keine Original-Assets (GFX, SND, Karten, Musik, exportierte PNGs) in Repo oder Build. Prüf-Exporte nur in temporäre Verzeichnisse.
  Einzige Ausnahme: README-Screenshots in `docs/screenshots/` (in `.gitignore` freigegeben).
- Original-EXE/-DLLs (auch `GFX/Siedler3_18…dat`, eine DLL) nie dekompilieren, disassemblieren, analysieren. Nur Datenformate.
- Kein Kopierschutz/DRM umgehen.
- JSettlers-Code (MIT) darf übernommen werden → `THIRD_PARTY_NOTICES.md` pflegen.
- Open Source (MIT, `LICENSE.md`), Clean Room; Regeln für Mitwirkende: `CONTRIBUTING.md`, README auf Englisch.
- Kein „Siedler"/„Settlers" in Typnamen (Repo/README: „monoSettlers").

## Aufbau
- `src/Homestead.Assets` – Parser für Originaldateien (GFX, Karten), ohne MonoGame. Formatdoku: `docs/formate/`.
- `src/Homestead.AssetViewer` – MonoGame-Viewer (DesktopGL), ohne Content-Pipeline.
- `data/` – Spieldaten (keine Original-Assets), neben jedes Programm kopiert. `production.json`: eigenes Balancing
  (Zeiten, Rezepte, Steine je Felsgröße, Erz je Mengenstufe, Regeneration, `depletion`: Verbrauch von Steinen/Bergen/Fisch in %, 0 = unendlich;
  im Spiel über die Einstellungen überschreibbar).
  Gebäudedaten aus JSettlers generiert (`tools/data-import/`) – nie von Hand ändern, sondern `data/buildings/overrides.json`
  (je Volk/Gebäude, `all` für alle; z. B. römische Burg 4 Bogenschützen, Minen nur auf Gebirge).
- `tests/Homestead.Simulation.Tests` – xUnit (`dotnet test`); Tests mit Originalkarten bestehen ohne lokale Installation ungeprüft.

### `src/Homestead.Simulation` – Spiellogik ohne MonoGame, feste Tick-Rate (`World.TicksPerSecond`)
- `Map/GameMap`: Gelände, Objekte, Begehbarkeit, Bodenschätze (`ResourceAt`).
  `Map/Vision`: Fog of War je Spieler – eigenes Land, Sicht eigener Figuren (Soldaten `Sight`, Späher `scouts.sight` 20, sonst `SettlerSight`)
  und fertiger Gebäude (`buildingSight` in `military.json`, Aussichtsturm 45); Erkundetes bleibt erkundet; KI nutzt es nicht.
- `Pathfinding/PathFinder`: A*; Erreichbarkeit über `Regions` (führen Begehbarkeitsänderungen nach). Schrittkosten nach Laufzeit
  (`SetStepCosts`: Pfad 8, eben 10, Gebirge 13); Landgebiete je Besitzer.
- `World`/`Settler`: ein Feld je Figur, Tauschen, Ausweichen, Umweg, Watchdog-Durchlauf (Kommentar in `World`).
  `Settler.StepProgress` in `World.StepUnits`; Tempo je Zielfeld: Gebirge ×0,8, Trampelpfad ×1,2 (`walking` in `production.json`).
  Siedler und Gebäude haben einen Spieler; Logistik/Rekrutierung nur für den eigenen. `World.TerritoryRules` erzwingt eigenes Land
  (Bauplatz samt Tür, Stapel, Bauarbeiterplätze, Wege – außer Soldaten, Priester, Geologen, Späher, Pioniere); Gestrandete gehen heim.
- `Trails`: Träger mit Auftrag treten auf Gras Trampelpfade (`GroundCover.Trail`, nach `trailSteps`), verschwinden nach `trailFadeSeconds` (300).
  Ackerland und Pfade halten ein Feld Abstand zu allem außer Gras/planiertem Boden und zueinander (`GameMap.CoverFits`, sonst brechen
  Original-Übergänge). Getreide wird auch auf Pfade gesät und kürzt Pfade ringsum (`Trails.CutAround`); Säen macht Gras im Umkreis
  `farmlandRadius` (1) zu Ackerland, soweit es passt und im Arbeitsbereich liegt.
- `Buildings/`: Definitionen aus `data/buildings/*.json`.
  - `Construction`: Planieren (`World.PlanLevelling`, `levelling` in `production.json`): Grundfläche auf mittlere Höhe, ringsum Ring für Ring,
    bis kein Nachbar über `maxSlope` (8) abweicht (am Hang breiter). Aufwand = bewegte Höhe; ungültig über `maxWork` oder wenn ein zu
    änderndes Feld bebaut, anderweitig geplant, unbegehbar oder fremd ist. Planierer: je Gang `heightPerDig`, nie Steigung über `maxSlope`
    (am Hang oben zuerst). Sofort fertige Gebäude planieren sofort. Minen: nur Gebirge, kein Planieren (`levelling: false`, `NeedsLevelling`).
  - Bauarbeiter ohne Material werden frei (untätig, schiebbar, ein Schritt weg, wechseln Baustelle); Plätze nur, solange Material für einen weiteren reicht.
  - `World.RemoveBuilding`: Abriss/Verlust mit Staubwolke (`DustClouds`), Anteil `demolition.payback` des Baumaterials bleibt liegen.
    Land abgerissener Militärgebäude bleibt beim Spieler, aber ungeschützt (feindliches Militärgebäude nimmt es samt Gebäuden).
  - Abriss: Arbeiter behält Beruf und Werkzeug, übernimmt das nächste Gebäude seines Berufs.
- `Economy/`: Waren, Stapel.
  - `Logistics` mit Watchdog: je Ware unter den aktuellen Abnehmern – Baustellen als einer (`Logistics.Construction`), sonst je Gebäudetyp –
    einer gelost nach Spieler-Anteilen (`ShareOf`/`SetShare`; Vorgaben `distribution` in `production.json`, 0–1, z. B. Nahrung Minen 1 : 0,1,
    Bretter Baustelle 1, Köhlerei/Werft 0,5; gelistete Waren geben ungelisteten Abnehmern 0, ungelistete allen 1; Anteil 0 bekommt nichts,
    außer alle sind 0), dann das nächste Ziel mit Platz; Lager nur, wenn sonst niemand will.
  - Einlagern nur, was der Spieler einlagert (`IsStored`/`SetStored`); Lagerhaus auf eine Ware spezialisierbar
    (`SpecializationOf`/`SetSpecialization`, `StoresHere`, überstimmt die allgemeine Regel); Vorgabe `storage.materials`: Werkzeuge, Waffen, Gold, Edelsteine.
  - Angebote nach Transportreihenfolge (`TransportOrder`/`MoveInTransportOrder`, Vorgabe `storage.transportOrder`); lose Stapel auf niemandes
    Land: Spieler mit freien Trägern. Sonst bleibt Ware am Ausgabestapel (8; voll → Erzeuger wartet, Sammler auch).
  - `Problems` zählt Abbruchgründe.
  - `Production`: Werkstätten; Schmiede je Takt ein Produkt: Bestelltes, dann erwartetes Werkzeug, sonst gewichtet reihum nach Anteilen
    (`shares`: Werkzeugschmied 0 = nur Bedarf; Waffenschmied Schwert 100 %, Bogen 70 %, Speer 45 %), solange unter `keepInStock` (0 = ohne Grenze);
    Wahl erst mit Eisen und Kohle.
    Minen: Nahrungsstapel max. `mineFood.pileCapacity` (4); erste `mineFood.freeRuns` (8) Gänge je Gebäude gratis; Erfolgschance =
    Resterz im Umkreis gegen `resources.fullAmount` (8) je Feld (leere/fremde Felder = Nieten, jeder Fund senkt sie; `MineChance`); Niete kostet Zeit und Nahrung.
    Sammler: Wasserwerk nur an Flüssen; Bauer, Weinbauer, Reisbauer, Imker nach `crops` – Getreide macht Ackerland (`GroundCover.Farmland`,
    begehbar, bis Abriss/Verlust der Farm), wächst 200 s; Wein am Hang schneller; Reis nur auf Sumpf; Reben/Bienenstöcke wachsen nach.
  - `Healing`: Lazarett-Heiler ruft den nächsten verwundeten untätigen eigenen Soldaten im Umkreis `hospital.callRadius` (40) vor die Tür,
    heilt ihn (ab da nur durch Abriss unterbrechbar; volle LP nach `healSeconds`, wo immer er ist), geht hinein, `callPauseSeconds` Pause.
  - `Housing`: Wohnhäuser entlassen Träger und geben so viel Wohnraum (plus Startbevölkerung, `SettleStartPopulation`). Platz brauchen
    Träger und Arbeiter (auch Planierer/Bauarbeiter – Baustellen rekrutieren höchstens Anteil `Share.Diggers`/`Builders`, mind. je einer),
    nicht Soldaten, Geologen, Späher, Pioniere. Ohne Platz streiken (`Settler.IsStriking`): erst untätige Träger, dann Arbeiter (bleiben drin).
  - `Recruitment`: Träger holen Werkzeug, werden Spezialisten. Tests ohne Werkzeugthema: `TestConfig.NoTools`.
  - `Idling`: Untätige (Träger, Soldaten, Planierer, Bauarbeiter, Geologen, Späher, Pioniere, Arbeitslose) gehen aus dem Weg
    (nach `IdleTicks`; Soldaten sofort, jeden Tick, mit einem Feld Abstand).
  - `Geology`: Geologen laufen wie Soldaten (verteilt auf freie Felder); auf Gebirge suchen sie im Umkreis `geologists.searchRadius`
    (nächstes freies Feld, Gleichstand zufällig), setzen Schilder mit Rohstoff und Menge.
  - Späher (`Profession.Scout`, Grafik `THIEF`): umgewandelt/geschickt wie Geologen (`Geology.Convert(…, Profession.Scout)`), suchen nie.
    Für andere Spieler ein eigener Träger (`Settler.IsRevealedTo`), nicht angegriffen, bis ein feindlicher Soldat draußen auf
    `scouts.revealRadius` (10) herankommt (`Military.RevealScouts`, jeden Tick); erkannt bleibt er, solange der Spieler ihn sieht
    (`Military.Sees`: eigenes Land, Sicht eigener Figuren/Gebäude wie Fog of War), dann wieder getarnt. Erkannt: wie Geologe bekämpft
    (auch von Turm-Bogenschützen), KI zählt ihn zu Feindgruppen. KI-Verdacht (`AiPlayer.InspectScouts`): Späher auf ihrem Land → nächster
    untätiger Soldat folgt ihm (sonst ruft sie einen aus dem nächsten Militärgebäude mit mehr als einem Soldaten).
  - `Theft`: Späher stehlen (`Steal`; Alt+Rechtsklick: `StealAt`) die nächste Ware im Umkreis `scouts.stealRadius` (5) – unreservierte Einheit,
    nicht aus eigenen Gebäuden oder losen Waren auf eigenem Land; trägt sie (`Carrying`, geduckt), Tempo `scouts.loadedSpeed` (0,5);
    lässt sie still auf eigenem Land fallen oder wo er stirbt.
  - `Pioneering`: Pioniere (umgewandelt/geschickt wie Späher; `Claim`, Alt+Rechtsklick `ClaimAt`) nehmen Feld für Feld: nächstes freies oder
    fremdes, nicht geschütztes Feld (`Military.IsProtected`) im Umkreis `pioneers.searchRadius` (10), Gleichstand nächst dem Zielpunkt;
    `claimSeconds` (2) Pfosten, dann gehört ihm das Feld samt unbegehbaren Nachbarn (`Military.TakeLand`, ungeschützte fremde Gebäude fallen).
    Im geschützten Feindland Richtung freies Land, sonst Ende. Vollständig umschlossenes freies Land (nicht Kartenrand/fremdes Land) wird
    auf einmal genommen (bis 4096 Felder). Land ungeschützt.
- `Military/`: Kaserne bildet aus, Rekruten laufen zum Sammelpunkt. Soll-Besatzung je Typ (`ChangeWanted`: neu 1 Schwertkämpfer; nie 0,
  nie über Infanterie-/Bogenplätze; „+“ bei vollen Infanterieplätzen ersetzt die andere Infanterieart; Überzählige kehren um oder kommen
  heraus, nie der letzte – Ersatz tauscht an der Tür). Besetzte beanspruchen Land; immer ihren eigenen Grund (sonst fiele ein eroberter Turm gleich wieder).
  - `MilitaryCombat`: Untätige Soldaten und solche mit nicht erzwungenem Laufbefehl greifen feindliche Soldaten und besetzte Militärgebäude
    (Tür in Sicht) an, Näheres zuerst; Laufende gehen danach weiter; Besatzung bleibt drin. Nahkämpfer schlagen erst die Tür ein
    (`door`, HP wie Pikenier; 5 s ohne Feind im Umkreis 6 → Wächter geht rein, Tür regeneriert 0 → voll in 30 s).
    Dann je ein Verteidiger unbeweglich vor der Tür (`FightPoint`; Plätze je Volk/Gebäude als Versatz in `door.points`, sonst automatisch,
    vorn zuerst; erst Infanterie, dann Bogenschützen), nur von Nahkämpfern auf den Türplätzen angreifbar (`door.fighters`: Turm/großer Turm 2,
    Burg 1; Pikeniere wie Reichweite 1, Rest wartet, Bogenschützen mit Abstand). Wer den letzten Verteidiger tötet, ist drin.
    Bogenschützen treffen beim Schuss, Pfeil (`Military.Arrows`, `ArrowTicks`) nur Darstellung. Nahe am Feind lokale Wegsuche (`combatPathRadius`).
  - Kampfkraft (`CombatStrength`): eigenes Land 100 %, sonst 100 % / Spielerzahl; plus Gold im eigenen Land (nach Landbesitz; Ägypter: Gold
    halb, Edelsteine doppelt), progressiv (`unitsPerPercent` je `stepPercent`), ohne Obergrenze (200 % = doppelter Schaden außerhalb; zwingt Reiche zum Angriff). KI kennt alle Werte und bewertet den Kampfort.
  - Werte: `data/military.json`; Gebietsradien aus Originalkarten gemessen.
- `Ai/` – `AiPlayer`: gleiche Befehle wie ein Mensch, kennt die ganze Karte. `World.AiPlayers` jeden Tick angestoßen. `MapSurvey`: Kartenüberblick in Zellen.
  - Bauen nach `data/ai.json` und Bedarf: Wohnhaus, solange Träger ≤ 3 × Produktionsgebäude (Größe nach fertigen Sägewerken/Steinmetzen
    je Volk); kein Material für Türme zurückhalten; kein weiteres Gebäude einer Art, solange eins auf Arbeiter wartet; Rohstoffgebäude ohne
    Platz probieren weitere Zellen; Wasserwerk ohne Platz → Expansion zum Fluss. Gebäude ohne Platz am Anker → geräumigste eigene Zelle.
  - Minen nur ab Erfolgschance `mines.minChance` (0,3), Abriss unter `mines.abandonChance` (0,1); kein Platz → Erz fehlt → Expansion.
    Schmelzen erst mit eigener Mine ihres Erzes.
  - Vor dem Abriss erschöpfter Steinmetze/Fischer: Arbeitsbereich (bis `MaxWorkCenterDistance`) dorthin versetzen, wo mehr in Reichweite ist (`BetterWorkCenter`).
  - Wohnhaus-Abriss, wenn danach noch `spareRoom` frei. Förster alle `forester.checkSeconds` (10) angehalten ab `forester.maxTrees` (10) Bäumen/Setzlingen im Bereich.
  - Kein Bau auf Ackerland oder im Feldbereich eigener Farmen (Umkreis 4 ums Arbeitszentrum); Farm braucht 35 freie Grasfelder im Bereich.
  - Expansion: Türme gezielt zu fehlenden Rohstoffen (ferne Ziele Turm für Turm), meidet Plätze mit mehr als `maxWaterShare` Wasser.
  - Nach der Nahrungskette: großer Tempel, Mana-Kette, kleiner Tempel; spät Goldmine/-schmelze (Ägypter: Edelsteinmine);
    Expansion zu Gold (`lateOres`) erst, wenn die Build-Order bis zur Mine erfüllt ist.
  - `AiPlayer.Military`: Soll-Besatzung voll (`Staff`), kennt alle Feindsoldaten, wägt jede Sekunde neu. Untätig zählen auch Soldaten auf
    einfachem Laufbefehl und auf Rückzug (erzwungen) – Rückziehende sind sofort frei für Verteidigung/Guerilla/Feldzug, laufen sonst weiter
    (Bewachung schickt sie nicht um). Verteidigung zuerst; Guerilla gegen kleinere Trupps (auch Geologen, `geologistHealth`) bei deutlicher
    Überlegenheit; herumstehende Feinde nicht tiefer als `military.hinterlandDepth` in ihrem Land (Plänklerin 8);
    feindliche Soldaten in `military.reinforceRadius` (30) ums Ziel zählen mit (verstreute kommen zu Hilfe); Guerilla nicht, wo Turm-Bogenschützen
    mehr als `military.towerFireRatio` (0,5) × Zielstärke ausmachen – geprüft vom Mittelpunkt der Angreifer (laufende wird abgebrochen, lockt der Feind
    sie unter die Türme); Angriff aufs Land = kämpfen oder Laufziel außerhalb des eigenen Lands; nach Abbruch Ziel 30 s in Ruhe; Guerilla vor Zurückhalten
    (Verteidiger dürfen angreifen, solange genug gegen andere Trupps bleiben). Rest: Charakterzug `guardByThreat` bewacht nach Bedrohung
    (ohne: stehen bleiben); ohne Bedrohung Turmverteilung: `frontTowers` (Plänklerin), `everyOtherTower` (Feldherr, Legion),
    `rotatingTowers` (Unbeugsamer: 30 %, minütlich neu), Hitzkopf: ein Haufen.
  - `AiPlayer.Campaign`: sammelt Soldaten, erobert feindliche Militärgebäude bei 1,5:1 (Besatzung gewichtet `attack.garrisonWeight`, Burg doppelt).
    Werte: `attack.armyRatio` (0 = egal), `attack.commit` (Anteil freier Soldaten, 1 = alle), `attack.abortRatio` (0 = nie aufgeben),
    `attack.maxDistance` (0 = egal), `military.retreatRatio` (0 = nie zurück).
  - `AiPlayer.Magic`: Priester folgen Angriffen mit Abstand, zaubern ab `magic.minTargets` Getroffenen; Aufwertungen sofort bei genug Mana
    (`magic.upgradeOrder`: Schwertkämpfer, Bogenschützen, Pikeniere). KI zaubert nur den Volkszauber.
  - Charaktere/Charakterzüge (Konzept: `docs/KI-TACTICS.md`; Kennungen englisch, deutsche Namen in `ui.json` `aiCharacters`/`aiTraits`):
    Charakter (`AiCharacter`, `characters` in `ai.json`) nennt Züge (`AiTrait`, `Ai/Traits/<Kennung>Trait`; Vorgaben unter `traits`,
    je Charakter überschreibbar) und überschreibt Werte; `characterWeights` (je Volk) losen je Partie.
    Entscheidungspunkte: `LeadCampaign` (Truppenführung), `Guard` (Bewachung), `AllowCampaign` (Zeitpunkt), `GarrisonLimit` (Besatzung),
    `ArrangeBuildOrder` (Build-Order, einmal beim Start), `RunErrands` (Besorgungen, nach der Bewachung).
    Züge: `waves` (Ruhe/Wellen im Wechsel), `frontGarrison` (nur frontnahe Gebäude voll, sonst `backline`), `earlyArmy` (Schmelze,
    Waffenschmied, Kaserne direkt nach Holz/Stein; Schmelze auch ohne Mine bei Erz auf eigenem Land), `forwardBase` (Sammeln am frontnächsten
    Militärgebäude, Rückzug ohne Kampf vor Stärkeren), `pincer` (Feldherr: Finte vom Sammelpunkt, Hauptgruppe im Bogen zur Flanke),
    `thief` (Plänklerin, Hitzkopf: `scouts` Späher stehlen Stapel am Rand feindlichen Lands, `ranks` Gold/Waffen/Erze > Werkzeuge > Nahrung > Rest;
    nicht bei feindlichen Soldaten in `soldierRadius`, sonst heimgerufen, Stapel `retrySeconds` gemieden).
    `summoner` (Legion: Mana über `reserveSpells` × Volkszauber → „Waren herbeirufen“ durch einen freien Priester auf eigenem Land
    nahe ihm, `edgeDistance` vom Rand, ohne Feindsoldaten, mit `minFreeFields` freien Feldern).
    `buildOrderMoves` verschiebt Gebäude in der Build-Order.
    Volks-Charaktere (`standard`/`general` nur per `--characters`): `hothead` (Ägypter: `earlyArmy`, alles greift an, Hinterland dünn, stiehlt),
    `unyielding` (Asiaten: gibt erst unter halber Verteidigungsstärke auf), `legion` (Römer: frühes Gold, Wellen, Zange, Warenzauber),
    `skirmisher` (Amazonen: Feldzüge erst bei 2:1, kleine Trupps, nahe Ziele, stiehlt).
    Ausgedacht (Arena, Turnier): Züge mit `+` (`skirmisher+forwardBase`, angehängte vor den eigenen), Werte mit `@pfad=wert`
    (`general@attack.ratio=1.3@buildOrderMoves.barrack=-5`).
    Notizen eines Zugs beginnen mit seiner Kennung, erscheinen live (`AiPlayer.Noted`, `TraitOfNote`).
    Werte in `ai.json`, je Volk überschreibbar (`civilizations`, dort auch die Build-Order).
- `Magic/`: Mana je Spieler. Kleiner Tempel: Wein/Met/Bier/Schnaps → Mana; großer Tempel schickt sofort und alle `priestSeconds` einen Priester.
  `Cast`: Priester läuft auf Bogenreichweite × `rangeFactor` (1,5), zaubert nur, wo jemand Betroffenes steht (`Magic.HasTargets`:
  feindliche Soldaten; Einfrieren auch Priester; Samuraischwert eigene Soldaten), kein Kampf nötig, Kosten konstant.
  Volkszauber: Bekehren (Seite wechseln, Volk bleibt – `Settler.People`), Samuraischwert, Einfrieren, Zerstreuen. Für alle (`commonSpells`):
  „Waren herbeirufen“ (`SummonGoods`: 3–4 Stapel à 4–8 aus Nahrung, Waffen, Werkzeug, Gold, Eisen, Kohle, Erz; Ägypter 50 % zusätzlich Steine;
  Flammen zeitversetzt). Priester angreifbar. Zwei Konten: Zauber verbrauchen `Mana`, Aufwertungen prüfen `ManaEarned`
  (`upgrades.manaEarned` je n-ter Aufwertung). `Upgrade`: braucht fertigen kleinen Tempel; erst alle drei Typen Stufe 2, dann 3;
  gilt nur für danach Ausgebildete. Werte: `data/magic.json`.
- `Persistence/`: `Snapshot` schreibt den ganzen Objektgraphen per Reflection (alle Felder, auch private, Objektidentität; Typtabelle mit
  Feldnamen – fehlende übersprungen, neue: Sammlungen/Klassen mit parameterlosem Konstruktor leer, sonst Standardwert (nicht der Initialisierer!); Dictionary/HashSet mit innerem Aufbau, Hashes beim Laden neu → Partie läuft
  tickgenau weiter, `SaveTests`). Delegates nur mit Zielen in eigenen Assemblies (Client-Abos werden nach dem Laden neu gesetzt);
  `[NonSerialized]` lässt weg; fremde Typen brechen mit Pfad ab, `JsonElement` als Text. Neue Felder brauchen keinen Speichercode.
  Typnamen ohne Assembly-Version aufgelöst; fehlender Typ/Methode → `SaveIncompatibleException`. `SaveLoadFailure.Of`: Art (inkompatibel/beschädigt/unlesbar)
  und Fehlercode `HS-<Art>-<FNV-Hash>`; Titelbildschirm zeigt ihn in der Meldungsbox (`ShowLoadFailure`), Konsole die Ausnahme.
  `SaveGame`: Welt, Kataloge, Karte, Sicht, Client-Werte. `SaveSlots`: 8 Slots `slot N.hsav` in `~/.config/homestead/saves`
  (`HOMESTEAD_SAVE_DIR`), Kopf `SaveInfo` (Karte, Volk, Zeit, Tick) unkomprimiert, Rest gzip, über temporäre Datei.
- `Events/` (`GameEvents`, `World.Events`): Meldungen je Spieler aus `data/events.json` – Kategorie Info/Kampf/Rohstoffe/Handel,
  `cooldownSeconds` je Spieler/Event (0 = immer), Text (`{building}`), an/aus. `Raise(id, spieler, ort, settler/building)`.
  Kampf: `attacked`, `buildingAttacked` (Tür/Wächter), `buildingLost`/`buildingCaptured`. Zustände fertiger Gebäude prüft `Tick` sekündlich,
  meldet nach `afterSeconds` einmal bis Ende: `noStaff`, `noTool`, `noResources`, `mineExhausted` (Chance ≤ `maxChance` %). `Announce`: ohne Ort.
- `GameHistory` (`World.History`): alle 30 s (`SampleTicks`) je Spieler eine `HistorySample` (Land, Siedler nach Art, Soldaten nach Typ,
  Gebäude, Baustellen, Militärgebäude, Wohnraum, Streikende, Lagerbestand, Produziertes, Kampfkraft, Mana) plus laufende Summen
  (getötet/gefallen, Gebäude erobert/verloren, fertiggestellt/zerstört); für den Statistik-Bildschirm.
- `OriginalScenario`: Karte → Welt (Gebäude, Stapel, Siedler). Mehrspieler-Karten ohne Gebäude: `StartSetup` gibt jedem Spieler die Ausstattung aus `data/start.json`.
- `Map/Generation/RandomMapGenerator`: Zufallskarten als `OriginalMap` (`OriginalMap.Create`), gleiche `RandomMapSettings` + Seed = gleiche Karte,
  Werte `data/randommap.json`, Größe nach Spielerzahl. Insel in der Mitte (in Rastergeometrie ein Kreis). Jeder Start gleich: flache Startfläche,
  Berg mit Kohle/Eisen/Gold, Teich mit Bach (Wasserwerke brauchen Flüsse), Wald, Steine. Dazu Gebirge, Seen, Flüsse zum Meer, Sümpfe (mind. einer),
  Wälder, Steinfelder, Erzadern, Fisch, Deko, Schätze (Warenstapel ≥ 32 Felder von Starts). Symmetrie: `Mirror` (Keilfläche; exakt bei 2/3/4/6,
  5 gerundet), `Repeat` (Startkachel `homeRadius` kopiert). Landschaftsbytes zuletzt aus Bodenklassen (Wassertiefe, Strand, Ränder je ein Feld,
  damit jedes Dreieck eine Original-Übergangskachel hat – Merkmale halten Abstand). Starts ohne Verbindung → neu würfeln.

### `src/Homestead.Client` – MonoGame-Spiel
- **Titelbildschirm** (`TitleScreen`, zuerst ohne `--map`/`--capture`): Hauptmenü-Bild der Gold Edition (`titleScreen` in `ui.json`, Datei 61 GUI 1,
  daneben glattes Gold), links eingravierte Knöpfe wie im Original (Maße/Farben vom Original-Menü): Einzelspieler (→ Startscreen), Spiel laden
  (Slots darunter, Klick lädt; offen = gedrückter Knopf), Einstellungen (→ Einstellungen), abgesetzt Readme/Contribution/Lizenzen (Popup auf der
  Original-Meldungsbox Datei 02 GUI 16, `popup` in `titleScreen`: „Im Browser öffnen“ → `README.md`/`CONTRIBUTING.md`/`THIRD_PARTY_NOTICES.md` auf GitHub, Enter/Esc),
  unten abgesetzt Beenden; Esc/„Zum Hauptmenü“ der Statistik/Esc im Startscreen führen hierher.
  Capture: `HOMESTEAD_CAPTURE_START=title` → `title.png` (`title:readme`/`title:contribution`/`title:licenses` mit Popup, `title:load` mit Slots, `title:loadError` mit Ladefehler).
- **Einstellungen** (`SettingsScreen`): Hintergrund Datei 02 GUI 14 (`settingsScreen` in `ui.json`, Marmor daneben); oben in den vier
  Kästchen Reiter mit Icons der Original-Statistikmenüs (Datei 03, hell gewählt/unter der Maus, sonst dunkel; Tab/Shift+Tab): Rohstoffe,
  Wirtschaft, Militär (beide noch leer), Darstellung; je Einstellung eine Reihe Werte (Rohstoffe: `depletion` Steine/Berge/Fisch
  100/75/50/25/10 %/∞; Darstellung: Hardware-Cursor, Vollbild, Sprache Deutsch/English – ohne Wahl nach Systemsprache; `Lang.Use` setzt auch die Kultur;
Titelbild der Gold Edition je Sprache, `background` `{de, en}`; `HOMESTEAD_LANGUAGE=de|en` überstimmt, z. B. für Captures).
  Ok/Enter speichert `GameSettings` (Abschnitt `game`) und `DisplaySettings` (Client, Abschnitt `display`, sofort angewandt) in
  `~/.config/homestead/settings.json` (`SettingsFile`: je Abschnitt, Rest der Datei bleibt); Abbrechen/Esc verwirft, Standard setzt den offenen
  Reiter zurück (Spielwerte: `production.json`). Hardware-Cursor aus: eigener goldener Pfeil (`SoftwareCursor`, in `EndDraw`, nicht in Captures).
  Neue Partien: `GameSettings.Load(…).Apply(…)` → `OriginalScenario.Load(production:)`; Spielstände behalten ihre Werte (Arena/Tests: nur `production.json`).
  Capture: `HOMESTEAD_CAPTURE_START=settings[:resources|economy|military|display]` → `settings.png`.
- **Startscreen** (`StartScreen`): auf dem Kartenwahl-Hintergrund des Originals (`startScreen` in `ui.json`, Datei 02 GUI 8,
  im 800×600-Raster auf Fenstergröße skaliert, daneben Marmor; ovale Steinknöpfe, Steinleiste unter der Auswahl, Vorschau im schrägen Fenster): Kartenliste mit Vorschau, Tabs Einzelspieler/Mehrspieler/Eigene (Tab-Taste),
  sortiert nach Spielerzahl (`OriginalMap.ReadPlayerCount` liest nur MapInfo). Tab „Zufallsgenerator“: letzte 5 Zufallskarten
  (`RandomMapHistory`, `~/.config/homestead/random-maps.json`) und Einstellungen (Spieler, Symmetrie, Flüsse, Rohstoffe, Steine, Wälder, Deko,
  Schätze) – Änderung erzeugt mit gleichem Seed neu, „Neu würfeln“/R neuer Seed. Je Spieler: Volk (oder Zufall; Kartenvorgaben gelten je Position),
  Farbe (`PlayerColors.Palette`; Klick nächste freie, Rechtsklick vorige; „Zufall“ → `PlayerColors.Assign`), Hardcore.
  Mehrspieler-Startpositionen gelost (Vorschau schwarz „?“, Klick weist zu/zurück; `StartScreen.ResolvePositions` → `OriginalScenario.Load(positions:)`).
  Hardcore (`World.HardcorePlayers`, `Hardcore`): +10 % Kampfkraft flach (auch über 100 %), +30 % Bewohner je Wohnhaus, +20 % Soldaten-LP
  (`World.MaxHealth`), −25 % Baumaterial je Stapel (gerundet, mind. 1, `World.ConstructionCost`), Arbeitszeiten × 0,9,
  Start +2 Schwertkämpfer, +5 Bretter, +5 Stämme (`OriginalScenario.Load(hardcore:)`).
- **Seitenpanel** (`SidePanel`, große Variante, auf Fensterhöhe skaliert; Menüs zeichnen/prüfen Klicks im Panelraum, `SidePanel.ToPanel`):
  - Minimap oben im Originalrahmen (dahinter Schwarz; rechts schmaler Goldrand mit Ranke, Datei 4 Sequenz 3): nur erkundetes Gelände (`Minimap`),
    sichtbarer Ausschnitt als weißer Rahmen. Klick: Kameraflug 0,35 s; Ziehen verschiebt den Rahmen; Scrollen bricht den Flug ab;
    Rechtsklick/Alt+Rechtsklick mit Auswahl: Befehl dorthin wie auf der Karte.
  - Event-Buttons rechts neben dem Computer-Button (`EventButtons`: Info, Kampf, Rohstoffe, Handel; Platz/Bilder in `events.json` `categories`):
    neues Event → Button blinkt `blinkSeconds` mit 1 Hz, leuchtet dann bis gesehen. Klick (neuestes ungesehenes der Art, sonst neuestes) bzw.
    Leertaste (neuestes ungesehenes; gehalten nach 1 s mit 10 Hz) → Event-Seite (`PanelTab.Event`: Kategorie, Text weiß), Kameraflug,
    vier Pfeile `arrowSeconds` auf Gebäude bzw. lebende Figur. `dates`: Grüße an Kalendertagen (MM-dd), einmal im Jahr
    (`~/.config/homestead/events-shown.json`; `HOMESTEAD_EVENT_DATE=12-25` täuscht vor).
  - Computer-Button unten links (`SystemMenu`, `PanelTab.System`): „Spiel speichern“ (8 Slots mit Karte, Datum, Volk; leere gestrichelt;
    Klick speichert, auch überschreibend), „Spiel beenden“ mit Rückfrage (Ja unten links → Statistik, Nein unten rechts).
  - Hauptreiter Bauen/Waren/Siedler (`PanelTabs`: Original-Buttons Datei 03, aktiver gedrückt; Klick hebt Auswahl auf; Untermenüs `panelTabs` in `ui.json`).
    - Bauen: Baumenü (Kategorien aus `data/ui.json`, Original-Buttons; Klick aufs Icon → Platzieren; eigenes Volk; Zahl fertiger (weiß) und im Bau (orange)).
    - Waren › Lagerbestand (`StockMenu`, `World.InStock`: Lager-, Ausgabe-, lose Stapel; nicht Eingang/Baustellen).
    - Waren › Herstellung (`ManufactureMenu`): je Produkt Bestellung mit Auf/Ab (`Production.Ordered`/`ChangeOrdered`, zuerst gemacht) und
      Anteilsbalken (`ShareBar`: Klick setzt, Ziehen, 5-%-Schritte, nahe Enden 0/100 %).
    - Waren › Verteilung (`DistributionMenu`): Waren mit ≥ 2 Abnehmern (Reihenfolge `distributionMenu`), rechts je Abnehmer Anteil + `ShareBar`.
    - Waren › Warentransport (`TransportMenu`): Transportreihenfolge in Sechserreihen (versetzt); Punkt grün = einlagern, rot = nicht; gewählte
      Ware: einlagern/nicht, ganz hoch, hoch, runter, ganz runter (Bilder `transportMenu`); Icons gleiten.
    - Siedler › Siedlerstatistik (`SettlersOverview`): Wohnraum, Bedarf, alle Siedler, Soldaten, je Art Porträt + Anzahl (Esel 0; ohne Porträt Figur im Rahmen).
    - Siedler › Berufe (`ProfessionsMenu`): Anteile ±5 % (Träger mindestens, Planierer/Bauarbeiter höchstens; `Recruitment.ShareOf`/`SetShare`,
      Vorgaben `keptCarriers`/`maxDiggers`/`maxBuilders` 30/25/25 %); Spezialberufe Geologen, Pioniere, Späher, Pioniere→Träger (Träger-Porträt)
      mit Such-Buttons (alle auf Karte, alle im Bild, nächster reihum mit Kameraflug) und 5/1 umwandeln
      (`Geology.ConvertCarriers`, `Geology.PioneersToCarriers`).
    - Siedler › Krieger (`SoldiersMenu`): Kampfkraft, göttliche Aufwertung (Mana-Fortschritt, je Typ Waffen-Button, verziert ab Stufe 2),
      automatische/manuelle Rekrutierung mit freien Trägern; manuell je Typ Waffen im Vorrat und geplante Rekruten (⇈ +5, ↑ +1, ↓ −1, ⇊ −5;
      `Military.SetManualRecruitment`/`ChangePlanned`).
    - Siedler › Produktion (`StatisticsMenu`): Diagramm letzte 60 min einer Ware (Einheiten/min, 2-min-Mittel, Skala passt sich an);
      Warenauswahl `GoodsGrid` (mit Lagerbestand geteilt: Icons `goodsGrid` in `ui.json`, Indizes aus JSettlers' EMaterialType, nicht herstellbar dunkel);
      gezählt in `World.Statistics` (`ProductionStatistics`, 30-s-Abschnitte).
  - Gebäude-Klick ersetzt das Baumenü (ohne eigenen Hintergrund): Name, Bild (unskaliert, unten zentriert, beschnitten –
    `EconomyRenderer.DrawBuildingImage`; bei Enge erst Waren weg, dann Bild niedriger), bei eigenen Status, „Abreißen“ mit Rückfrage;
    Produktion: „Anhalten“/„Fortsetzen“ (`World.SetPaused`: Arbeit fertig, dann drinnen); Militär: Besatzung je Typ (da/Soll, +/−);
    Lagerhaus: alle Waren (`GoodsGrid`; Punkt = allgemeine Regel, Klick spezialisiert – roter Rahmen –, nochmal hebt auf); Waren/Baumaterial,
    Minen-Erfolgschance; Arbeitsbereich (Rechtsklick versetzt, `World.SetWorkCenter`, max. `MaxWorkCenterDistance` = 20, sonst nächstes erlaubtes
    Feld; Sammler nehmen das Ziel nächst der Mitte; Kaserne: Sammelpunkt überall, `Building.HasGatheringPoint`, Pfad mit Fahne).
    Deutsche Namen/Zustände in `data/ui.json`; Text per FontStashSharp mit Systemschrift.
- **Statistik** (`StatisticsScreen`; Esc oder „Spiel beenden“; Spiel steht still): blauer Marmor (`statisticsBackground`, Datei 02 GUI 29),
  Seiten Übersicht (Tabelle, Landanteil), Wirtschaft, Militär, Waren (jede Ware je Minute), Gesellschaft – Diagramme `Charts` (Linien mit Werten
  unter der Maus, Anteile, Balken) aus `GameHistory`/`ProductionStatistics`; ←/→/Tab blättern; „Weiterspielen“ (Esc; nicht nach „Spiel beenden“),
  „Zum Hauptmenü“.
- **Textbuttons** (`TextButton`): Steinknöpfe aus den Menüs vor dem Spiel (`textButton` in `ui.json`, Datei 02 GUI 0, 9-Slice), z. B. Abreißen,
  Anhalten, Ja/Nein, Rekrutierung, Statistik. Besatzung ±: Auf/Ab-Spinner wie in der Herstellung.
- **Auswahl/Befehle**: Rahmen oder Klick wählt eigene Figuren, Vorrang Soldaten > Priester > Geologen > Späher > Pioniere > untätige Träger,
  andere Berufe nie (beim Rahmen geht das der Mitte nächste Gebäude den Trägern vor; Gebäude: weißer Eckrahmen 3 px). Shift fügt hinzu,
  Strg+Klick alle eigenen dieses Typs (Soldaten: Typ, Rang egal). Ausgewählte: runder Punkt grün→gelb→rot nach LP.
  `SelectionPanel` je Art (Typ/Stufe) Porträt (`Portraits`, `ui.json` `portraits`) + Anzahl, unten Kampfkraft – wie Zauber-/Umwandlungsmenü
  im Inhaltsbereich (`BuildMenu.Content`). Strg+1–9 speichert Gruppe (Icon oben rechts; Klick oder 1–9 wählt; Tote, Gebäude Betretende, Bekehrte fallen raus).
  Soldaten: Rechtsklick hinschicken/angreifen, Strg+Rechtsklick erzwungen ohne Kampf. Geologen: Rechtsklick hin, auf Gebirge suchen.
  Priester: Zaubermenü (Name, Kosten, Mana), Zauber wählen, dann Ziel (Zielring rot: wirkt auf niemanden); Rechtsklick hin.
  Träger: Umwandlungsmenü (bis Trägerreserve; beschäftigte nach Lieferung). Linksklick abwählen; rechts ziehen verschiebt die Karte.
- **Tasten**: Esc bricht Platzieren/Zielen ab, sonst Statistik; N Träger, M 50 Wanderer, K Testsoldaten (Shift: Spieler 2), C Cheat-Menü (`CheatMenu`: Soldaten je Typ/Stufe, Priester, Geologen,
  Späher, Pioniere, Träger, +50 Mana), F Angriff aufs feindliche Militärgebäude unter der Maus, V Fog of War (Sicht 1×/s, `FogLayer` blendet weich,
  klingt über `SoftFields` (3) aus; unerkundet schwarz, erkundet dunkel, fremde Figuren nur in Sicht), F11 Vollbild (nur für die Sitzung; Vorgabe in den Einstellungen),
  F2 Skalierung scharf/linear/Catmull-Rom (Standard)/Lanczos2 (Welt 1:1 in kleine RenderTarget, um den Zoom vergrößert; Markierungen/Panel scharf;
  `Shaders/Upscale.fx` → eingebettetes `.mgfx`, nach Änderung `tools/compile-shaders.sh`, braucht Wine),
  F3 Leistungsanzeige (FPS, ms Simulation/Update/Zeichnen, längster Frame, Draw-Calls), F12 gehalten 10× Tempo, T Spielzeit, I KI spielt für dich
  (andere Spieler immer KI), L KI-Entscheidungen in die Konsole, G Demo-Wirtschaft,
  P Platzier-Modus (Punkte an passenden Bauplätzen, grün→rot nach Planieraufwand – `World.LevellingEffort` / `levelling.maxWork`; max. 6 ms je Frame,
  `World.BuildSpotEffort`; Vorschau am nächsten Bauplatz; Q/E Gebäude, Shift+Klick sofort fertig; endet nach Platzieren, Rechtsklick bricht ab).
- **Optionen**: `--scene map` (Standard) / `--scene walk` (Lauf-PoC); `--load <slot>` (1–8; Gelände/Objekte aus der Karte, dann Feld für Feld auf Stand);
  `--map <name>` (Standard: erste Mehrspieler-Karte; `random[:spieler[:none|mirror|repeat[:seed]]]`); `--peoples amazon,roman,…` (offene Völker,
  Spieler i: Eintrag i mod n; sonst zufällig; Kartenvorgaben 0–3 gelten); `--characters ,general` (KI-Charakter je Spieler, Eintrag i → Spieler i+1, leer = gelost).
- **Darstellung**: Figuren nach Volk; Tote: Geist in Spielerfarbe (Datei 12 Seq. 27, `World.Ghosts`); Stehende ohne Aufgabe wechseln alle 2–30 s
  die Blickrichtung (nur Darstellung); Turm-Bogenschützen mit Schussanimation im Takt, Infanterie im Turm als Fensterbild; Pfeile im flachen Bogen
  (`docs/formate/gfx.md`). `SettlerAnimations` löst Typ/Aktion/Ware/Richtung über `data/settlers.json` (generiert mit
  `tools/data-import/convert_jsettlers_movables.py`, nicht von Hand ändern). Baustellen zeigen Schild und Pfosten.
- **Capture** (`--capture <dir>`, offscreen PNGs, nur temporäre Verzeichnisse): ignoriert Eingaben, ohne VSync, 4× Tempo; gibt am Ende Zähler und
  Gebäudezustände aus. Umgebungsvariablen `HOMESTEAD_CAPTURE_…`:
  `ZOOM=4`; `EVERY=<n>` (PNG alle n Frames, sonst 120); `SIZE=1920x1080` (sonst 960×600); `SCALING=<Sharp|Linear|CatmullRom|Lanczos2>`;
  `FOCUS=<gebäude>` (`site`: frische Baustelle; `temple`: kleiner Tempel mit Mana; Militärgebäude wie `castle`: voll besetzt); `FOG=1`;
  `EVENT=1` (springt zu Events); `BATTLE=1` (Stufe-3-Feinde greifen den Startturm an; `=big`: zwei Armeen à 30 treffen sich südlich des Starts; `=open`: 40 gegen 40 auf freiem Feld; `=town[:n]`: n (90) gegen n mitten im Dorf; mit `ZOOM` Kamera darauf); `COLORS=2,4` (Palettenfarben je Spieler); `STRIKE=1` (Abriss des fokussierten Gebäudes + 10 Träger ohne Wohnraum);
  `GEOLOGISTS=1`; `PIONEERS=1`; `SELECT=1` (Soldaten aller Typen/Stufen gewählt); `SAVE=<slot>` (nach 300 Frames; mit `HOMESTEAD_SAVE_DIR` temporär!);
  `TAB=system[:save|quit]` bzw. `goods|settlers[:<untermenü>[:manual|<ware>]]` (z. B. `settlers:soldiers:manual`, `goods:distribution:bread`);
  `STORE=<ware>` (mit `FOCUS=stock`); `PLACE=<gebäude>`; `START=<tab>` (nur `start.png` des Startscreens, 3: Zufallsgenerator);
  `MAGIC=1` (großer Tempel, Mana-Kette, Priester zaubert in einen Kampf; `=summon`: Waren herbeirufen);
  `MINUTES=<n>` (Benchmark: KI auch für Spieler 1, n min ohne Zeichnen vorgespult, dann Kameraschwenk über die Karte, PNG alle 1200 Frames);
  `STATS=<frame>` (Statistik öffnet in diesem Frame, alle Seiten als `stats_N.png`, dann Ende);
  `VIEW=economy|battle` (statt Schwenk: Kamera auf Spieler 1 bzw. folgt dem größten Kampf; `battle` spult mit `MINUTES` bis zu 20 min weiter, bis ≥ 20 Soldaten an einem Kampf sind).

### Werkzeuge
- `tools/Homestead.Arena` – KI gegen KI ohne Fenster: `dotnet tools/Homestead.Arena/bin/Release/net9.0/Homestead.Arena.dll
  [karte… | random:4:mirror:7] [--all] [--minutes 30] [--every 5] [--peoples amazon,asian] [--characters ,general] [--seed 1] [--log] [--notes] [--profile]
  [--until-decided] [--csv <datei>]`. Charakterzug-Entscheidungen sofort (alle mit `--notes`); `--profile`: ms je Tick und Teil (`World.StepProfile`);
  `--seed` gibt Völkern und Welt (`World.Seed`) den Zufall: gleicher Seed, gleiche Partie. Ausgabe je Spieler: Land (frei, Wasseranteil), Gebäude,
  Mana, Priester, Siedler, Wohnraum/Streikende, Vorräte, hängende Baustellen. `--until-decided`: Ende, wenn nur einer Militär hat; `--csv` hängt Ergebnisse an.
- `tools/trait-tournament.py run|report` – Turnier der KI-Charaktere/-Züge über die Arena (jede Paarung, Karte, Volk, beide Startpositionen;
  parallel, fortsetzbar); `--preset traits|peoples`, `egyptian:hothead` nur mit seinem Volk, `--against`, `--vary pfad=a/b/c`;
  Ergebnisse `tournament-results/<preset>.csv` (nicht im Repo).
- `tools/Homestead.AssetTool` – `scan`, `export`, `sheet` (PNG), `maps`, `resources`, `minimap`.

## Kartenobjekte
`GameMap` speichert je Feld den Original-Objektcode (`MapObjectCodes`); Begehbarkeit und Grafik leiten sich daraus ab.
Laufzeit: Baumstumpf 41 (verschwindet nach `trees.stumpSeconds`), Setzling 84, Felsgrößen 115→127 (Geröll).
Bodenbelag (`GroundCover`, nur auf Gras): Ackerland (als Erde, Original-Übergänge zu Gras), Trampelpfad (als Sand, Sand-Gras-Übergänge).
Eigene Anbau-Codes: Getreide 200–207 (207 reif), Wein 208–210, Reis 211–214, Bienenstöcke 215–218 (Bild vorerst Blumen).

## Grafik-Konventionen
- Raster → Pixel: `x*16 - y*8`, `y*9 - höhe*HeightStep` (Zeilen halb versetzt), 6 Nachbarn. Original HeightStep 2, wir 1 (`ScreenProjection.HeightStep`).
- Gelände-Atlas: Texturkoordinaten einen halben Texel innerhalb der Zelle, sonst Linien aus Nachbarzellen.
- Sprites nur über `SpriteLibrary` (gemeinsame Atlas-Seiten, `SpriteAtlas`); keine eigene `Texture2D` je Bild (Draw-Calls).
- Lauf-Sequenzen: 72 Frames = 6 Richtungen à 12 (SW, W, NW, NE, O, SO); Frame 0 stehen, 1–11 Zyklus je Rasterschritt.

## Installation des Originals
`HOMESTEAD_GAME_DIR` oder `gameDirectory` in `~/.config/homestead/settings.json`; der erste gültige gilt (`GameInstallation.TryLocate`).
Bei jedem Start geprüft (`InstallationCheck`: `GFX` mit `RequiredGfx`, `SND` mit `siedler3_00/01.dat`, `Map` mit Karten); sonst
`InstallationDialog` (eigener Prozess `--setup` – zweites Spielfenster im selben Prozess bricht SDL/GL beim Beenden; ohne Originalgrafik):
Pfadfeld (Tippen, Strg+V über SDL), Ordnerbrowser, Suche an üblichen Orten (`Detect`), Prüfung live, Installation auch über/unter dem
gewählten Ordner (`FindRoot`); „Spiel starten“ → `GameInstallation.Remember`. Capture: `HOMESTEAD_CAPTURE_DIALOG=<png>` (übernimmt die gültige).
Lokal: `~/Games/gog/the-settlers-iii-ultimate-collection/drive_c/GOG Games/Settlers 3 Ultimate`.

## Versionen und Pipeline
- Version aus Git-Tags `vX.Y.Z` (MinVer in `Directory.Build.props`; ohne Tag `0.1.0-alpha.0.<n>`), Titelbildschirm unten rechts.
  Regeln: `docs/VERSIONING.md` (unter 1.0: PATCH Fixes/Balancing/Übersetzungen, MINOR Features oder Spielstand-/Settings-Bruch).
- `.github/workflows/build.yml`: Push auf main/PR → Build + Tests, Pakete linux-x64 (`tar.gz`) und win-x64 (`zip`), self-contained, als Artefakte;
  Tag `v*` → zusätzlich GitHub-Release (`gh release create`, Suffix → Vorabversion). Kein macOS.

## Bauen/Starten
- `dotnet build Homestead.sln`
- Programme mit `dotnet <pfad>.dll` bzw. `dotnet run --project …` – der native Apphost findet .NET in der Flatpak-Umgebung von VS Code nicht.
