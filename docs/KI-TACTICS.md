# KI-Taktiken und Charaktere

Ideensammlung, wie die Computergegner herausfordernder, taktischer und überraschender werden.
Grundlage ist die bestehende KI (`src/Homestead.Simulation/Ai/`, Werte in `data/ai.json`).

## Grundgedanke: Module und Charaktere

Die KI bleibt ein Spieler mit denselben Befehlen wie ein Mensch. Neues Verhalten kommt aber nicht
fest verdrahtet in `AiPlayer`, sondern als **Modul**: ein abgeschlossener Baustein mit eigenem Namen,
eigenen Werten und klar umrissenem Einsatzort. Ein **Charakter** ist eine Liste von Modulen samt
Werten. Jedes Volk bekommt einen oder mehrere Charaktere; pro Partie wird einer davon gewählt.

```
Volk ──(einer davon je Partie)──> Charakter ──> Module (+ Werte)
```

### Was die Grundlage bleibt

Nicht alles soll abschaltbar sein. Ohne folgendes Grundverhalten wäre eine KI kaputt, nicht anders:

- Wirtschaft nach Build-Order, Wohnraum, Expansion zu Rohstoffen
- Verteidigung zuerst (Feinde im eigenen Land werden immer bekämpft)
- Kein Abriss eines Militärgebäudes, dessen Land eigene Gebäude trägt (seit Abriss Land
  ungeschützt zurücklässt, würde die KI sich sonst selbst verwundbar machen)

Das heutige Verhalten (Guerilla, Bewachung, Kampagne bei 1,5:1, Besatzung voll) wird zum
**Standard-Charakter** – so verhält sich die KI ohne Angabe genau wie jetzt.

### Entscheidungspunkte

Module hängen sich an feste Stellen im Denkablauf der KI. Jede Stelle legt fest, wie mehrere Module
zusammenwirken:

| Entscheidungspunkt | Wann | Zusammenwirken mehrerer Module |
|---|---|---|
| **Besatzung** | jede Militär-Runde, je eigenem Militärgebäude | Module liefern Soll-Besatzung; das höchste Soll gewinnt |
| **Angriffsziele** | Planung einer Kampagne | Module bewerten Ziele (Punkte addieren sich), bestes Ziel gewinnt |
| **Truppenführung** | während eines Angriffs | ein Modul führt den Trupp (z. B. Zange *oder* Hinterhalt), Formation kann zusätzlich gelten |
| **Turmplätze** | Expansion | Module bewerten Bauplätze, Punkte addieren sich |
| **Zeitpunkt** | jede Militär-Runde | Module können Angriffe erlauben, verbieten oder anschieben |
| **Zauber** | Priester am Kampf | ein Modul entscheidet Zauber und Moment |
| **Schwellen** | überall, wo ein Verhältnis entscheidet | Modifikator auf Werte wie `attack.ratio` |

### Soldaten verteilen

Mehrere Module wollen dieselben untätigen Soldaten. Feste Reihenfolge:

1. Verteidigung (Grundverhalten)
2. laufende Trupps behalten ihre Soldaten
3. Module nach ihrer Priorität im Charakter (erst genannt = zuerst)
4. Rest: Bewachung nach Bedrohung

Ein Modul nimmt nur, was es braucht, und gibt Soldaten zurück, wenn sein Vorhaben endet.

### Vorschlag für die Daten (`ai.json`)

```json
"characters": {
  "standard":   { "modules": ["guerilla", "campaign", "guardByThreat", "fullGarrison"] },
  "stuermer":   { "modules": ["earlyRush", "campaign", "frontGarrison"], "attack": { "ratio": 1.2 } },
  "hinterhalt": { "modules": ["flanking", "exposedTowers", "frontGarrison", "jitter"],
                  "flanking": { "squadSize": 5, "detour": 25 } }
},
"civilizations": {
  "Asian": { "characters": [["hinterhalt", 2], ["stuermer", 1]] }
}
```

Werte eines Moduls stehen im Charakter unter dem Modulnamen und überschreiben die Vorgabe des
Moduls. Gewichte bei den Völkern bestimmen, wie oft ein Charakter gewählt wird.

### Vorschlag für den Code

- Schnittstelle `IAiModule` mit optionalen Methoden je Entscheidungspunkt (z. B.
  `WantedGarrison(...)`, `ScoreTarget(...)`, `LeadSquad(...)`, `ScoreTowerSpot(...)`).
- Jedes Modul in eigener Datei unter `Ai/Modules/`, testbar für sich allein.
- `AiPlayer` fragt an jedem Entscheidungspunkt die Module seines Charakters.
- Arena: `--characters stuermer,hinterhalt`, um Charaktere gegeneinander zu messen.
- Log (`L` im Spiel, `--log` in der Arena) nennt das Modul, das entschieden hat.

## Module

Status: alle **offen**. Aufwand grob: S (klein), M (mittel), L (groß).

### Besatzung

**`frontGarrison` – Besatzung nach Frontlage** · M
Militärgebäude im Hinterland bekommen 1 Soldaten, Gebäude an der Grenze zum Feind voll, dort mit
Bogenschützen. Ergebnis: deutlich mehr Soldaten frei für Angriffe.
Werte: Abstand, ab dem ein Gebäude als „Front“ gilt; Mindestbesatzung Hinterland.

**`reinforceThreatened` – Vorab aufstocken** · S
Steht ein feindlicher Trupp in Reichweite eines Grenzturms, wird dessen Besatzung sofort auf voll
gesetzt, bevor der Angriff beginnt.

**`rotateWounded` – Verwundete tauschen** · M
Angeschlagene Soldaten kehren in einen Turm zurück, frische kommen heraus.
*Voraussetzung:* Soldaten heilen derzeit nicht. Erst sinnvoll, wenn sie im Gebäude Gesundheit
zurückgewinnen (eigene Spielregel, für Mensch und KI gleich).

### Truppenführung

**`formation` – Infanterie vorn, Bogenschützen hinten** · M
Bogenschützen halten ein paar Felder Abstand hinter der Infanterie. Erreicht feindliche Infanterie
die Bogenschützen, weichen diese zurück, statt in den Nahkampf zu geraten.

**`pincer` – Zange und Finte** · L
Zwei Trupps: ein kleiner lockt die Verteidiger an einer Stelle heraus, der Hauptangriff kommt kurz
darauf von der anderen Seite. Die Kampagne bekommt dafür zwei Sammelpunkte.

**`flanking` – Hinterhalt durch Umgehung** · L
Ein kleiner Trupp läuft in weitem Bogen um das gegnerische Lager, außerhalb der Sicht feindlicher
Soldaten und der Reichweite von Bogenschützen in Türmen, und greift von hinten an. Ziel sind schwach
besetzte Gebäude im Hinterland. Wird eines erobert, nimmt es Land, und die gegnerischen
Gebäude darauf fallen (Regel „ungeschütztes Land“). Damit rechnet ein menschlicher Spieler nicht.
Werte: Truppgröße, Mindestabstand zum Feind auf dem Umweg, höchste Besatzung eines Ziels.
Wird der Trupp entdeckt (Feinde kommen in Sicht), greift er sofort an oder zieht sich zurück.

**`spellTiming` – Zauber im richtigen Moment** · M
Einfrieren unmittelbar vor dem Zusammenstoß, Zerstreuen auf Klumpen vor einem Turm, Bekehren auf
die stärksten Gegner in Reichweite. Heute zaubert die KI, sobald genug Ziele im Radius stehen.

### Schwachstellen ausnutzen

**`exposedTowers` – Entblößte Türme kontern** · M
Zieht der Gegner für einen Angriff Soldaten aus seinen Türmen ab (bis auf einen), werden genau
diese Türme sofort angegriffen. Bewertet Ziele nach Besatzung jetzt, nicht nach Normalbesatzung.

**`landGrab` – Landnahme ohne Kampf** · M
Turmplätze bevorzugen, deren Gebiet ungeschütztes gegnerisches Land überdeckt (z. B. nach einem
Abriss). Sobald der Turm besetzt ist, fallen die gegnerischen Gebäude darauf.

**`economicTargets` – Wirtschaft treffen** · S
Angriffsziele höher bewerten, deren Land Minen, Schmelzen, Waffenschmieden oder Tempel trägt.
Ein Treffer dort kostet den Gegner mehr als ein beliebiger Turm.

### Gold

Siehe [Gold als Hebel](#gold-als-hebel). Drei Module, je an einem eigenen Entscheidungspunkt.

**`goldRace` – Gold früh sichern** · M
Goldberge (Ägypter: Edelsteine) gelten von Anfang an als Expansionsziel, nicht erst nach der
Build-Order (`lateOres`). Goldmine und -schmelze rücken in der Build-Order nach vorn, sobald Nahrung
und Werkzeuge laufen. Werte: ab welchem Schritt der Build-Order, Gewicht umkämpfter Goldberge.
Entscheidungspunkte: Turmplätze, Bauen.

**`goldGuard` – Gold schützen** · M
Land, auf dem eigenes Gold liegt (Lager, Ausgabestapel der Schmelze), gilt als Front – auch im
Hinterland: Militärgebäude dort voll besetzt, mit Bogenschützen, Wachen nach der Menge Gold statt
nur nach Bedrohung. Neues Lager nur auf Land, das mehrere eigene Türme decken. Wahlweise Gold auf
mehrere Lager verteilen, damit ein Treffer nicht alles kostet (Abwägung: mehr Orte zu verteidigen).
Entscheidungspunkte: Besatzung, Bewachung, Bauplatz Lager.
Auf Schwer in jedem Charakter empfohlen – ohne ihn kann eine einzige Eroberung die KI erledigen.

**`goldRaid` – Gold erobern** · M
Angriffsziele, deren Land gegnerisches Gold trägt, bekommen die höchste Bewertung: Die Eroberung
nimmt dem Gegner Kampfkraft und gibt sie dem Angreifer. Lohnt sich auch bei knapperer Überlegenheit
(eigener Wert für `attack.ratio`), weil der Gewinn die Kräfte sofort verschiebt.
Entscheidungspunkt: Angriffsziele.

### Zeitpunkt und Strategie

**`earlyRush` – Früher Ansturm** · M
Mit den Startsoldaten früh einen nahen gegnerischen Turm angreifen, bevor der Gegner Kaserne und
Waffen hat. Danach normale Wirtschaft.

**`powerSpike` – Angriff nach Aufwertung** · S
Angriffe zurückhalten, bis eine Aufwertung fertig ist oder genug Gold für hohe Kampfkraft lagert,
dann gebündelt losschlagen. Gold dafür gezielt sammeln.

**`waves` – Spannungsphasen** · S
Wechsel aus ruhigen Aufbauphasen und Angriffswellen statt gleichmäßigen Drucks. Werte: Länge der
Phasen (mit Streuung).

**`multiFront` – Druck an mehreren Stellen** · M
Zwei oder drei Trupps bedrohen gleichzeitig verschiedene Grenzabschnitte. Der Verteidiger muss sich
aufteilen; angegriffen wird, wo er am dünnsten steht.

**`jitter` – Schwellen streuen** · S
Feste Verhältnisse (`attack.ratio`, `strikeRatio` …) werden bei jeder Entscheidung innerhalb
einer Spanne gewürfelt (z. B. 1,3–1,8 statt 1,5). Das Verhalten wird schwerer vorherzusagen.

### Verworfen

**Fokusfeuer** (mehrere Soldaten auf dasselbe Ziel, Verwundete und Bogenschützen zuerst): Der Spieler
kann keine einzelnen Einheiten gezielt angreifen lassen. Die KI soll keine Steuerung bekommen, die
dem Menschen fehlt.

## Charaktere (Entwurf)

Noch nicht den Völkern zugeordnet – das machen wir, wenn die Module stehen.

| Charakter | Idee | Module |
|---|---|---|
| Standard | heutiges Verhalten | guerilla, campaign, guardByThreat, fullGarrison |
| Stürmer | früh und oft angreifen, geringere Überlegenheit reicht | earlyRush, campaign, frontGarrison, waves |
| Feldherr | geordnete Schlachten | formation, pincer, spellTiming, reinforceThreatened |
| Hinterhältiger | dort zuschlagen, wo keiner hinschaut | flanking, exposedTowers, goldRaid, frontGarrison, jitter |
| Landnehmer | wächst über Grenzen und nimmt Land ohne Kampf | landGrab, frontGarrison, economicTargets |
| Wirtschafter | ruhig aufbauen, dann mit starken Soldaten zuschlagen | goldRace, goldGuard, powerSpike, fullGarrison, spellTiming |

## Schwierigkeit und Charakter

Zwei unabhängige Achsen:

- **Charakter** = *wie* die KI spielt (Stil, Module).
- **Schwierigkeit** = *wie gut* sie es tut (Wahrnehmung, Reaktion, Entscheidungsqualität).

Ein Stürmer kann leicht oder schwer sein. Gebaut wird die beste KI (Schwer); leichtere Stufen
bekommen gezielte Abstriche. Die Stufen im Einzelnen stehen in `TODO.md` (Schwierigkeitsgrad).

### Stellschrauben je Stufe

| Stellschraube | Leicht | Mittel | Schwer |
|---|---|---|---|
| Wahrnehmung | eigenes Land, Späher, Geologen | was sie sieht, dazu Lage von Gebirgen und Steinen | alles |
| Späher/Geologen | ja, zum Finden von Rohstoffen | ja, zum Finden von Gegnern | nein |
| Reaktion | nur auf Angriffe im eigenen Land | auf entdeckte Feinde in der Nähe | auf alle Feinde in der Nähe, vorausschauend |
| Soldaten aufstellen | irgendwo im Land | an Grenztürmen zum Feindland | an Grenztürmen zu feindlichen Truppen |
| Guerilla | nein | ja | ja |
| Module | wenige, einfache | Kampf-Module | alle des Charakters |
| Schwellen | eher zögerlich bzw. leichtsinnig | Standard | genau berechnet |
| Denkpause | lang | mittel | kurz |

Module können selbst nach Stufe abschwächen (z. B. `flanking` auf Leicht ohne Rücksicht auf
Sichtweite), statt nur an- oder abgeschaltet zu werden.

### Schwer muss gewinnen, nicht nur halten

Eine undurchdringliche Verteidigung macht die KI schwer zu *schlagen*, aber nicht schwer zu *spielen*:
Der Mensch muss nur selbst bunkern, und die Partie endet im Patt. Schwer braucht deshalb auch
einen Weg zum Sieg:

- **Wirtschaft ausspielen:** besser verteilen, früher Gold, Rohstoffe schneller sichern. Gegen eine
  gleich starke Verteidigung gewinnt, wer mehr und stärkere Soldaten nachliefert.
- **Den Verteidiger überdehnen:** Druck an zwei oder drei Stellen zugleich. Auch ein Mensch kann
  nicht überall sein; die KI greift dort an, wo er gerade nicht verteidigt.
- **Geduld und dann ein Schlag:** warten, bis Kampfkraft (Gold) und Aufwertungen klar vorn liegen
  (`powerSpike`), dann gebündelt dort angreifen, wo eine Eroberung Wirtschaft mitreißt
  (`economicTargets`).
- **Magie als Durchbruch:** Einfrieren, Zerstreuen, Bekehren gezielt beim Sturm auf einen Turm.

## Gold als Hebel

Gold ist im Spiel genau dafür da, den Verteidigervorteil zu brechen und in die Offensive zu zwingen.

- **Regel:** Im Feindland kämpft ein Soldat mit 100 % / Spielerzahl. Goldbarren auf dem eigenen Land
  heben das bis 100 %, immer teurer je Schritt. Bei zwei Spielern: 115 Barren für 100 %
  (Ägypter: 230 Barren oder 57,5 Edelsteine; `combatStrength` in `military.json`).
- **Verteidigen braucht kein Gold, Angreifen schon.** Wer mehr Gold hat, kann angreifen; wer keines hat,
  kann nur halten.
- **Gold gehört dem Land:** Gezählt werden die Stapel auf dem Land eines Spielers, gleich, wer sie
  hingelegt hat. Erobert ein Spieler das Land um ein gegnerisches Lager, gehört ihm das Gold dort
  (gewollt). Der Gegner verliert Kampfkraft, der Sieger gewinnt sie – die Partie kann damit
  sofort entschieden sein.
- **Folgen für die KI:**
  - Gold ist ein strategisches Ziel von Anfang an (`goldRace`), nicht Beiwerk am Ende der Build-Order.
  - Eigenes Gold ist der wertvollste Besitz und wird geschützt wie die Front (`goldGuard`).
  - Gegnerisches Gold ist das beste Angriffsziel (`goldRaid`).
- **Risiko:** Der Lawineneffekt ist hier am stärksten. Gewollt ist, dass ein gelungener Schlag auf das
  Gold die Partie dreht; schlecht wäre, wenn er ohne echte Chance zur Gegenwehr geschieht. Deshalb
  muss Gold sichtbar und verteidigbar liegen – für Mensch und KI gleich.

## Dynamik und Patt

### Warum Partien heute im Patt enden

- **Verteidigervorteil:** Im eigenen Land 100 % Kampfkraft, im Feindland 100 % / Spielerzahl. Bei
  zwei Spielern braucht ein Angreifer etwa die doppelte Armee; die KI greift erst bei 1,5:1 an.
- **Allwissende, sofort reagierende KI:** Wachen stehen nach Bedrohung. Wer sich sammelt, wird
  gesehen, bevor er angreift.
- **Gleiche Rechnung auf beiden Seiten:** Zwei gleiche KIs halten sich gegenseitig zurück.
- **Kaum Schwächen:** Die KI stockt selten (Schmelze erst mit eigener Mine, Material für
  Steinmetz zurückgehalten …). Im Original entschieden gerade solche Hänger die Partien.

### Was eine Partie wenden kann (gewollt)

- **Gold-Wettlauf:** Gold hebt die Kampfkraft im Feindland – der eingebaute Ausweg aus dem Patt.
  Wer zuerst nahe 100 % kommt, kann angreifen. Goldberge werden umkämpftes Ziel.
- **Rohstoffe gehen aus** (`depletion`): Wer nicht zu neuen Rohstoffen kommt, verliert langsam. Liegen
  sie zwischen den Spielern, wird Kampf nötig.
- **Eroberung mit Folgen:** Fällt ein Turm, fallen die Gebäude auf seinem Land. Ein Treffer an
  Mine, Schmelze oder Waffenschmiede legt eine ganze Kette lahm.
- **Magie:** dreht Kräfteverhältnisse, ohne doppelte Armee.
- **Unterschiedliche Charaktere:** Ein Stürmer gegen einen Wirtschafter geht ins Risiko; einer
  gewinnt früh oder verliert sich.
- **Gewollte Schwächen je Charakter:** Der Landnehmer ist überall dünn besetzt, der Stürmer
  vernachlässigt die Wirtschaft. Etwas zum Ausnutzen – erkennbar statt zufällig.

### Was eine Partie verderben kann

- **Zu schneller Lawineneffekt:** Eine frühe Eroberung kippt die halbe Wirtschaft; die Partie ist in
  Minute 15 entschieden, dauert aber noch eine Stunde.
- **Bunkern:** Wird Verteidigen noch billiger, bauen beide nur Türme und nichts passiert.
- **Allwissenheit wirkt unfair:** Ein Hinterhalt des Menschen gelingt nie. Abhilfe über die
  Wahrnehmung der Schwierigkeitsstufen.
- **Zufällige Hänger:** Stockt die KI durch einen Fehler, entscheidet Glück statt Spiel.
- **Karte entscheidet:** Ist Gold der einzige Hebel, gewinnt, wer zufällig den Goldberg hat.

### Erste Schritte

1. **Messen:** Arena, 5–10 Partien KI gegen KI à 60 Minuten; Kampfkraft, Gold, Eroberungen und
   Verluste mitschreiben. Wie oft endet es im Patt?
2. **Gold früher und gezielt:** Goldberge ansteuern (`goldRace`), Gold schützen (`goldGuard`),
   Gold für Kampfkraft horten und dann zuschlagen (`powerSpike`, `goldRaid`).
3. **Wahrnehmung begrenzen** (Stufen Leicht/Mittel): Reaktion nur auf gesehene Trupps.
4. **Verteidigervorteil** nur mit Arena-Zahlen anfassen, z. B. Abschlag erst tief im Feindland
   voll, an der Grenze schwächer.

## Vorgehen

1. Modul-Gerüst: Charaktere in `ai.json`, `IAiModule`, heutiges Verhalten als Standard-Charakter
   (Verhalten unverändert, Arena-Ergebnisse gleich).
2. Arena um Messwerte zum Patt erweitern (Kampfkraft, Gold, Eroberungen, Verluste über die Zeit).
3. Module einzeln umsetzen, je mit Test und Arena-Vergleich gegen den Standard; zuerst die, die das
   Patt brechen (`goldRace`, `goldGuard`, `goldRaid`, `powerSpike`, `economicTargets`, `multiFront`).
4. Schwierigkeit als zweite Achse: Wahrnehmung, Reaktion, Schwellen, erlaubte Module.
5. Charaktere den Völkern zuordnen.
