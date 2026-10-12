todo:

IDLE-SOLDATEN:
Wenn sie ein Ziel sehen, sollten sie nach dem Kampf dahin zurücklaufen, wo sie standen, als sie das letzte mal idle waren
Wenn ein Verbündeter in der Nähe (5 Felder) angegriffen wird, sollten sie zur Unterstützung kommen. (Verhindert, dass man mit Bogenschützen einzelne Soldaten aus einer Gruppe wegsnipert)

GEBÄUDE:
x größerer Platzbedarf, damit die Gebäude nicht überschneiden und genug Platz lassen für Siedler und Optik

TERRAIN:
x Höhenunterschiede geringer, auf 50% reduzieren.

GAMEPLAY:
x Beim Selektieren und kommandieren der Soldaten soll die Zielkoordinate mit der Höhe verrechnet werden.
x 30% aller Träger (gezählt werden alle derzeitigen Fachkräfte + Träger) wird reserviert und darf niemals zu einem Soldaten oder einer Fachkraft werden. Für die KI heißt es dann, Wohnhäuser höher zu priorisieren
Tasten 1-9: Gruppierte Einheiten auswählen
x Erzwungener Befehl: Wenn zum Rechtsklick noch Strg gedrückt ist, erhält der Lauf-Befehl oberste Priorität.

x Bei selektierten Soldaten:
x Rechtsklick, um einen Befehl zu erteilen
x Linksklick, um die Soldaten wieder abzuwählen
Ctrl+1-9: Gruppe erstellen. Gruppe soll oben rechts als kleines Icon eingeblendet werden
Shift+Auswahl von Einheuten: Einheiten der aktuellen Auswahl hinzufügen
x Völker-spezifische Einheiten/Siedler anzeigen
x Amazonen mit einbinden

KI:
x Die KI sollte freien Bauplatz ebenfalls wie eine Resource behandeln und die Einnahme von neuer Baufläche anstreben.
x Gebäude, die zu einer Wirtschaftskette gehören zB Eisenmine -> Eisenschmelze sollten näher aneinander gebaut werden
Ist die Wirtschaft komplett, soll erweitert werden. Ziel ist die schnellere Einheitenproduktion. Bevorzugt werden demnach Gebäude, bei denen es ein Engpass gibt.
Es sollen auch mittlere und größere Wohnhäuser gebaut werden, falls entsprechende Resourcen vorhanden sind
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

x Zusätzliche/spezifische KI-Regeln in json auslagern (wie es bereits schon gemacht wurde)


UI:
x Erstmal simpel: Links am Rand ein Menü mit den selben Kategorien wie im Originalspiel. Darunter die entsprechenden Bauoptionen. Jede Bauoption als Icon anzeigen.
x Solange F12 gedrückt ist, erhöht sich die Spielgeschwindigkeit auf das 10-fache

Tweaks:
Balancing abweichend vom Originalspiel konfigurierbar:
x Resourcenverbrauch beim Abbau von Steinen, Erze (alle Rohstoffe in Bergen), Fisch: 100%, 75%, 50%, 25%, 10%, 0% (unendlich). 3 Kategorien, individuell konfigurierbar.

Simulation:
Militärisches Verhalten (ai.military und ai.attack) gegeneinander simulieren.
Verschiedene Build-Orders testen