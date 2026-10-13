todo:

GEBÄUDE:
x größerer Platzbedarf, damit die Gebäude nicht überschneiden und genug Platz lassen für Siedler und Optik

TERRAIN:
x Höhenunterschiede geringer, auf 50% reduzieren.

GAMEPLAY:
x Beim Selektieren und kommandieren der Soldaten soll die Zielkoordinate mit der Höhe verrechnet werden.
x 30% aller Träger (gezählt werden alle derzeitigen Fachkräfte + Träger) wird reserviert und darf niemals zu einem Soldaten oder einer Fachkraft werden. Für die KI heißt es dann, Wohnhäuser höher zu priorisieren
x Tasten 1-9: Gruppierte Einheiten auswählen
x Erzwungener Befehl: Wenn zum Rechtsklick noch Strg gedrückt ist, erhält der Lauf-Befehl oberste Priorität.

x Bei selektierten Soldaten:
x Rechtsklick, um einen Befehl zu erteilen
x Linksklick, um die Soldaten wieder abzuwählen
x Ctrl+1-9: Gruppe erstellen. Gruppe soll oben rechts als kleines Icon eingeblendet werden. Auf dem Icon steht die Nummer (1-9) und die Anzahl der Soldaten in dieser Gruppe. Auf Klick dieses Icons werden diese Einheiten wieder ausgewählt.
x Shift+Auswahl von Einheuten: Einheiten der aktuellen Auswahl hinzufügen
x Völker-spezifische Einheiten/Siedler anzeigen
x Amazonen mit einbinden

KI:
x Die KI sollte freien Bauplatz ebenfalls wie eine Resource behandeln und die Einnahme von neuer Baufläche anstreben.
x Gebäude, die zu einer Wirtschaftskette gehören zB Eisenmine -> Eisenschmelze sollten näher aneinander gebaut werden
Ist die Wirtschaft komplett, soll erweitert werden. Ziel ist die schnellere Einheitenproduktion. Bevorzugt werden demnach Gebäude, bei denen es ein Engpass gibt.
x BuildOrder je Volk: Asiaten/Amazonen früh eine 2. Holzproduktion (bei langen Spielen eine 3. abseits), anfangs 1 Steinmetz; Ägypter eine Holzproduktion, viele Steinmetze; Römer anfangs je 1, etwas später je 2
x Es sollen auch mittlere und größere Wohnhäuser gebaut werden, falls entsprechende Resourcen vorhanden sind
x Die KI soll auch (vorerst) wissen, wo gegnerische Truppen liegen.
x Bei Angriffen / Truppenbewegungen soll mit einbezogen werden, ob sich auf dem Weg dahin gegnerische Truppen befinden. Wenn also 2 Soldaten einen alleinstehenden angreifen wollen, muss geprüft werden, ob auf dem Weg dahin noch ein paar Gegner warten.
x Bei Flucht-manövern oder Rückzügen wird "Erzwungener Befehl" genutzt.


KI-Kampf:
x Die KI soll proaktiv guerilla-Angriffe ausführen. Zum Beispiel kleinere Truppen angreifen, falls lukrativ genug
x Falls ein Guerilla-Angriff abgeschlossen ist (erfolgreich oder nicht erfolgreich), kehren Die Soldaten nach hause zurück.
x Die Verteidigung des eigenen Landes hat oberste Priorität
x Die KI soll die Verteidigung des eigenen Landes verteidigen, indem Einheiten dahin verteilt werden, wo gegnerische Einheiten näher sind
x Die KI soll Einheiten sammeln, bevor sie ein Ziel angreift
x Die KI soll das gegnerische Land angreifen, wenn sie eine 1,5:1-Überlegenheit hat.
x Die KI soll beim Angriff Ziele am Rand priorisieren, die schlechter verteidigt sind und erreichbar sind, ohne auf Gegner zu stoßen.
x Je nach Volk sollen die Werte/Thresholds unterschiedlich konfigurierbar sein können.
x Guerilla wird der Verteidigung vorgezogen -> Das soll bewirken, dass die KI aus der Verteidigung heraus die Angreifer angfreifen kann, falls sinnvoll.

x Zusätzliche/spezifische KI-Regeln in json auslagern (wie es bereits schon gemacht wurde)


GEOLOGEN:
x Mehrere Träger auswählen (Soldaten haben Vorrang); Priorisierung Soldaten > Geologen > Träger
x Sind Träger ausgewählt, wird links statt des Baumenüs ein Konvertierungs-Menü angezeigt (vorerst Geologen)
x Mit Knopfdruck werden sie konvertiert, so weit die 30%-Regel es zulässt
x Geologe + Rechtsklick auf ein Gebirge: läuft hin und sucht. Danach nächstes freies Feld (bei mehreren zufällig), immer höchstens 10 Felder von der Start-Suchposition

UI:
x Erstmal simpel: Links am Rand ein Menü mit den selben Kategorien wie im Originalspiel. Darunter die entsprechenden Bauoptionen. Jede Bauoption als Icon anzeigen.
x Solange F12 gedrückt ist, erhöht sich die Spielgeschwindigkeit auf das 10-fache

Tweaks:
Balancing abweichend vom Originalspiel konfigurierbar:
x Resourcenverbrauch beim Abbau von Steinen, Erze (alle Rohstoffe in Bergen), Fisch: 100%, 75%, 50%, 25%, 10%, 0% (unendlich). 3 Kategorien, individuell konfigurierbar.

Simulation:
Militärisches Verhalten (ai.military und ai.attack) gegeneinander simulieren.
Verschiedene Build-Orders testen

Holy Shit:
x Nach der einfachen build-order (Soldaten können gebaut werden, Nahrungskette komplett) wird der große Tempel gebaut.
x Der große Tempel spawnt alle 5 Minuten und direkt nach Fertigstellung einen priester des entsprechenden Volkes.
Der Priester kriegt pro Volk vorerst einen Zauber:
x - Römer: Barbaren bekehren (5 gegnerische Einheiten im Bereich wechseln die Seite. Im Gegensatz zum Originalspiel wechseln sie nur die Farbe. Eine Amazone wird nicht auf einmal zum Römer.. das hat mich schon immer gestört)
x - Asiaten: Samuraischwert (Im Bereich greifen die Soldaten 10 Sekunden lang schneller an)
x - Amazonen: Einfrieren, ich kenne den Namen nicht. (Im Bereich werden alle gegnerischen Einheiten auf der Stelle für 10 Sekunden eingefroren)
x - Ägypter: Zerstreuen (8 gegnerische Einheiten im Bereich werden zufällig auf der Karte verteilt)

Kosten: 
x Jeder Zauber kostet Mana. Im Gegenzug zum Originalspiel bleiben die Kosten konstant.
x Reichweite der Zauber ist die der Bogenschützen
x Gezaubert wird nur im Kampf
x Die KI schleppt Priester in den Gruppen mit. Priester bleiben in den Kämpfen immer hinten.

Mana und Aufwertungen:
x Nach dem großen Tempel wird die manaproduktion gestartet.
x Amazonen: Imker -> kleiner Tempel, bringt 1 Mana
x Asiaten: Reis (An Sümpfen anbaubar) -> Brennerei -> kleiner Tempel, bringt 2 Mana
x Römer: Winzer, wächst schneller an Hängen, Höhenunterschieden zwischen Tiles -> kleiner Tempel, bringt 1 Mana
x Ägypter: Getreide ->  Brauerei -> kleiner Tempel, bringt 1 Mana

x Kosten für die nächste Aufwertung steigt nach jeder Aufwertung.
x 10 -> 20 -> 30 -> 40 -> 50 -> 60
x Dabei müssen erst alle Klassen (Schwert/Bogen/Speer) auf Feldwebel und können erst danach auf Hauptmann aufgewertet werden.
x Das Aufwertungsmenü gibt es, wenn man einen kleinen Tempel auswählt.


Schwierigkeitsgrad:
Jedem KI-Gegner wird eine Schwierigkeit leicht/mittel/schwer zugewiesen.
Leicht:
    - KI weiß nur das, was sie normalerweise sehen sollte. Im eigenen Land, Gegnerpositionen durch Späher, Resourcen durch Geologen
    - Bildet Geologen und Späher aus
    - Späher werden genutzt, um Gebirge und Steine zu finden
    - Keine Guerilla-Angriffe
    - Keine präventive Verteidigung, nur reaktiv
    - Soldaten werden irgendwo im Land platziert
Mittel:
    - KI weiß nur das, was sie sieht und die Lage von Gebirgen und Steinen. Im eigenen Land, Gegnerpositionen durch Späher, Resourcen durch Geologen
    - Bildet Geologen und Späher
    - Späher werden genutzt, um Gegner zu finden
    - Soldaten werden primär an Grenz-Türmen platziert, die zum Gegnerischen Land zeigen
    - Soldaten reagieren auf nahe feindliche Einheiten (falls entdeckt)
Schwer:
    - KI weiß alles. (Map, Militär, Rohstoffe)
    - Bildet keine Späher oder Geologen aus.
    - Soldaten werden primär an Grenz-Türmen platziert, die zu gegnerischen Einheiten
    - Soldaten reagieren auf nahe, feindliche Einheiten