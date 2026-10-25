# KI (`src/Homestead.Simulation/Ai`)

Konzept der Charaktere und Taktiken: [../KI-TACTICS.md](../KI-TACTICS.md).

- `Ai/` – `AiPlayer`: gleiche Befehle wie ein Mensch, kennt die ganze Karte. `World.AiPlayers` jeden Tick angestoßen. `MapSurvey`: Kartenüberblick in Zellen.
  - Bauen nach `data/ai.json` und Bedarf: Wohnhaus, solange Träger ≤ 3 × Produktionsgebäude (Größe nach fertigen Sägewerken/Steinmetzen
    je Volk); kein Material für Türme zurückhalten; kein weiteres Gebäude einer Art, solange eins auf Arbeiter wartet; Rohstoffgebäude ohne
    Platz probieren weitere Zellen; Wasserwerk ohne Platz → Expansion zum Fluss. Gebäude ohne Platz am Anker → geräumigste eigene Zelle.
  - Wachstum nach der Build-Order (`AiPlayer.Growth`, `growth` in `ai.json`): je Fenster (120 s) gemessen, worauf fertige, besetzte Gebäude
    warten; Ware, auf die eine Abnehmerart ≥ `starved` (25 %) der Zeit wartet (je Art, nicht gemittelt), ohne Lagerbestand → ein Erzeuger mehr (Werkstatt/Mine nur, wenn die
    vorhandenen ≥ `busy` (70 %) arbeiten – sonst liegt der Engpass weiter vorn); ausgelastete Waffenschmiede/Kasernen (bei Waffen im Lager)/kleine Tempel → noch einer.
    Je Art einer zugleich; nicht, wenn im Fenster einer fertig wurde.
  - Minen nur ab Erfolgschance `mines.minChance` (0,3), Abriss unter `mines.abandonChance` (0,1); kein Platz → Erz fehlt → Expansion.
    Schmelzen erst mit eigener Mine ihres Erzes.
  - Vor dem Abriss erschöpfter Steinmetze/Fischer: Arbeitsbereich (bis `MaxWorkCenterDistance`) dorthin versetzen, wo mehr in Reichweite ist (`BetterWorkCenter`).
  - Lagerhaus erst mit Gold/Edelsteinen (Goldschmelze/Edelsteinmine fertig oder Barren/Steine auf eigenem Land, auch erobert), dann bei `stockWhenWaiting` (8)
    herumliegenden einlagerbaren Waren und weniger als 2 freien Lagerplätzen.
  - Wohnhaus-Abriss, wenn danach noch `spareRoom` frei. Förster alle `forester.checkSeconds` (10) angehalten ab `forester.maxTrees` (10) Bäumen/Setzlingen im Bereich.
    Förster der Build-Order nur als Obergrenze: gebaut am fertigen Holzfäller mit den wenigsten, unter `forester.buildBelowTrees` (15) Bäumen/Setzlingen
    im Arbeitsbereich, dessen Mitte in keinem Förster-Arbeitsbereich liegt (voller Wald: keiner), höchstens Förster-Radius − 2 entfernt (sonst nächster
    bedürftiger Holzfäller, sonst keiner – früher landeten sie weit weg, der Holzfäller blieb bedürftig, ein Förster nach dem anderen), einer nach dem anderen
    (der letzte muss arbeiten); ohne Holzfäller (kein Baum auf eigenem Land) am Startpunkt.
    Imkerei wie ein Rohstoffgebäude: Zelle mit den meisten Bäumen, Holzfäller ringsum (16) zählen dagegen; ohne Bäume keine.
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
    Beim Sammeln Aufgabe erst unter 85 % der Abbruchschwelle; aufgegebenes/geräumtes Ziel 60 s in Ruhe.
    Werte: `attack.armyRatio` (0 = egal), `attack.commit` (Anteil freier Soldaten, 1 = alle), `attack.abortRatio` (0 = nie aufgeben),
    `attack.maxDistance` (0 = egal), `military.retreatRatio` (0 = nie zurück).
  - `AiPlayer.Tactics`: in kleinen Gefechten (Strike/Feldzug ≤ `military.wounded.maxSoldiers` 10) ziehen sich Kämpfende unter `withdrawHealth` (35 %)
    zurück (erzwungen, Position halten `restDistance` vom Feind Richtung `RetreatDoor`; verfolgt → zum Gebäude; eigenes Lazarett in Rufweite, nicht
    näher am Feind: stattdessen freigegeben zu dessen Tür), solange der Rest `holdRatio` × Gegner hat;
    zurück bei `rejoinHealth` (geheilt), unter `needRatio` × Gegner oder Gegner fast besiegt (`mopUpRatio`); 20 s kein erneuter Rückzug. Hitzkopf: nie.
    Lazarett (`hospital` in `ai.json`): untätige Verwundete (< `woundedHealth` 80 %) zum nächsten eigenen Lazarett bis `sendDistance` (60): der schwächste
    auf den Heilplatz, die übrigen warten 3 Felder vor der Tür (sonst schieben sie sich gegenseitig weg, der Heiler kommt nie); mehr als `wounded` (5) solche (ohne die noch Kämpfenden) weiter als `sendDistance` von jedem Lazarett
    (auch Baustellen) → Lazarett vor der Build-Order (wie Wohnhäuser) am Militärgebäude nächst ihnen.
    Late Game = große Armee (`army.lateSoldiers` 20 Soldaten außerhalb der Gebäude; zu groß, um Einzelne zurückzuziehen): Waffenschmied-Anteile
    `lateShares` (Speer 100 %, Bogen 80 %, Schwert 15 %); unter ¾ davon die Anteile von vorher.
    Strikes verfolgen alle Teile zerfallener Zielgruppen (nächster zuerst).
  - `AiPlayer.Magic`: Priester folgen Angriffen mit Abstand, zaubern ab `magic.minTargets` noch nicht Getroffenen (Feinde nahe eigenen
    Kämpfenden/Strike-/Feldzug-Soldaten, auch Bogenschützen und Verstärkung; Eingefrorene kurz vor dem Auftauen zählen wieder), mehrere zugleich
    auf verschiedene Stellen, Mana laufender Zauber reserviert. Eingefrorene zählen im Kräftevergleich nichts, mit dem Auftauen wieder mehr (`Strength`).
    Blatt wenden (`TurnWithSpell`): statt Strike-Abbruch, Feldzug-Aufgabe oder -Rückzug erst der Volkszauber (nicht Eile), solange noch
    `magic.turnRatio` (0,6) des Nötigen da ist. Aufwertungen sofort bei genug Mana
    (`magic.upgradeOrder`: Schwertkämpfer, Bogenschützen, Pikeniere). KI zaubert nur den Volkszauber.
  - Charaktere/Charakterzüge (Konzept: `docs/KI-TACTICS.md`; Kennungen englisch, deutsche Namen in `ui.json` `aiCharacters`/`aiTraits`):
    Charakter (`AiCharacter`, `characters` in `ai.json`) nennt Züge (`AiTrait`, `Ai/Traits/<Kennung>Trait`; Vorgaben unter `traits`,
    je Charakter überschreibbar) und überschreibt Werte; `characterWeights` (je Volk) losen je Partie.
    Entscheidungspunkte: `LeadCampaign` (Truppenführung), `LeadStrike` (Gefechtsführung, `StrikeLeader`), `Guard` (Bewachung), `AllowCampaign` (Zeitpunkt), `GarrisonLimit` (Besatzung),
    `ArrangeBuildOrder` (Build-Order, einmal beim Start), `RunErrands` (Besorgungen, nach der Bewachung).
    Züge: `waves` (Ruhe/Wellen im Wechsel), `frontGarrison` (nur frontnahe Gebäude voll, sonst `backline`), `earlyArmy` (Schmelze,
    Waffenschmied, Kaserne direkt nach Holz/Stein; Schmelze auch ohne Mine bei Erz auf eigenem Land), `forwardBase` (Sammeln am frontnächsten
    Militärgebäude, Rückzug ohne Kampf vor Stärkeren), `pincer` (Feldherr: Finte vom Sammelpunkt, Hauptgruppe im Bogen zur Flanke),
    `thief` (Plänklerin, Hitzkopf: `scouts` Späher stehlen Stapel am Rand feindlichen Lands, `ranks` Gold/Waffen/Erze > Werkzeuge > Nahrung > Rest;
    nicht bei feindlichen Soldaten in `soldierRadius`, sonst heimgerufen, Stapel `retrySeconds` gemieden).
    `summoner` (Legion: Mana über `reserveSpells` × Volkszauber → „Himmlische Geschenke“ durch einen freien Priester auf eigenem Land
    nahe ihm, `edgeDistance` vom Rand, ohne Feindsoldaten, mit `minFreeFields` freien Feldern).
    `formation` (Legion, Feldherr, Plänklerin: Strike mit Bogen + Nahkampf gegen Feinde zu Fuß, noch außer Reichweite – Bogenschützen vor auf
    Reichweite − 2, Nahkämpfer halten `parkDistance` dahinter; Feind ≤ `fallBackDistance` an Bogenschützen → diese halten hinter den Nahkämpfern,
    die gehen rein; danach alle `fallBackSeconds` erneut, ohne Ziel rücken Bogenschützen nach; spätestens nach `screenSeconds` rein).
    `buildOrderMoves` verschiebt Gebäude in der Build-Order.
    Volks-Charaktere (`standard`/`general` nur per `--characters`): `hothead` (Ägypter: `earlyArmy`, alles greift an, Hinterland dünn, stiehlt),
    `unyielding` (Asiaten: gibt erst unter halber Verteidigungsstärke auf), `legion` (Römer: frühes Gold, Wellen, Zange, Warenzauber),
    `skirmisher` (Amazonen: Feldzüge erst bei 2:1, kleine Trupps, nahe Ziele, stiehlt).
    Ausgedacht (Arena, Turnier): Züge mit `+` (`skirmisher+forwardBase`, angehängte vor den eigenen), Werte mit `@pfad=wert`
    (`general@attack.ratio=1.3@buildOrderMoves.barrack=-5`).
    Notizen eines Zugs beginnen mit seiner Kennung, erscheinen live (`AiPlayer.Noted`, `TraitOfNote`).
    Werte in `ai.json`, je Volk überschreibbar (`civilizations`, dort auch die Build-Order).
