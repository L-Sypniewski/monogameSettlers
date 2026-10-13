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
| Hinterhältiger | dort zuschlagen, wo keiner hinschaut | flanking, exposedTowers, frontGarrison, jitter |
| Landnehmer | wächst über Grenzen und nimmt Land ohne Kampf | landGrab, frontGarrison, economicTargets |
| Wirtschafter | ruhig aufbauen, dann mit starken Soldaten zuschlagen | powerSpike, fullGarrison, spellTiming |

## Vorgehen

1. Modul-Gerüst: Charaktere in `ai.json`, `IAiModule`, heutiges Verhalten als Standard-Charakter
   (Verhalten unverändert, Arena-Ergebnisse gleich).
2. Module einzeln umsetzen, je mit Test und Arena-Vergleich gegen den Standard.
3. Charaktere den Völkern zuordnen.
