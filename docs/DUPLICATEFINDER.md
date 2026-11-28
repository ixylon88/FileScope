# DuplicateFinder

DuplicateFinder findet binär identische Dateien in mehreren frei wählbaren Suchordnern. Jeder Suchordner erhält eine **Aufbewahrungspriorität**. Eine höhere Zahl bedeutet, dass seine Dateien bevorzugt erhalten bleiben. Fällt ein Pfad in mehrere Suchordner, zählt er einmal mit der höchsten zutreffenden Priorität.

Werden etwa `Archiv` und `Archiv\Fotos` ausgewählt, durchsucht DuplicateFinder den gemeinsamen Teilbaum pro Suchlauf einmal. War ein Unterordner vorübergehend unlesbar, kann der zweite Suchordner ihn erneut erfassen. Bereits vollständig durchsuchte Nachbarordner werden dabei übersprungen.

## Starten

In Visual Studio `DuplicateFinder.App` als Startprojekt wählen. In VS Code den Workspace `FileScope.code-workspace` öffnen und unter **Terminal → Task ausführen** den zur Windows-Architektur passenden Task **DuplicateFinder: Run (win-x64)** oder **DuplicateFinder: Run (win-arm64)** wählen. Dieselben Skripte lassen sich im Repository direkt aufrufen.

```powershell
.\Build.ps1 -Application DuplicateFinder -Run -Runtime win-x64
.\Verify.ps1 -Application DuplicateFinder
```

Für ARM64 beim Start `-Runtime win-arm64` angeben. Verify verwendet ohne `-Runtime` die Architektur des SDK-Hosts. [Build-, Test- und Publish-Tasks](../README.md#vs-code-workspace-tasks)

Unter **Optionen → Suchordner → Ordner hinzufügen** die Suchordner wählen. Neue Suchordner beginnen gleichrangig bei 0. Ein Doppelklick auf eine Priorität macht sie editierbar. Beispielsweise kann das Archiv die Priorität 10 und der Downloadordner die Priorität 1 erhalten. **Speichern** übernimmt die Einstellungen. **Ordner durchsuchen** oder **F5** startet den Suchlauf. **Strg+F** fokussiert den Tabellenfilter. Die Filterung erfasst unter anderem Pfade, Prüfsummen und Herausgeber.

## Ergebnisse und Bedienung

Unter **Optionen → Suchordner → Dateigröße beim Einlesen** lassen sich eine Mindest- und eine Höchstgröße für den Hauptdatenstrom setzen. Anfangs sind beide Felder leer und alle Größen zugelassen. Jede Grenze lässt sich einzeln setzen und zählt mit. **Von 200 kB bis 1,5 MB** erfasst beispielsweise 200.000 bis 1.500.000 Byte. **Bis 0 B** findet ausschließlich leere Hauptdatenströme. Pro Feld stehen B, kB, MB und GB zur Wahl (1 kB = 1.000 Byte).

Bei **Dateien suchen** zeigt die Statusleiste den aktuellen Pfad und zählt alle gesichteten Dateieinträge, einschließlich der Einträge außerhalb des Größenbereichs. Die anschließende Metadatenprüfung und der Aufbau des Katalogindex zeigen jeweils den bearbeiteten Dateipfad.

Der Bereich gilt vor Prüfsummen, Signaturprüfung und Binärvergleich. Auch **Alle gescannten Dateien** beim Bereinigen umfasst nur Dateien aus diesem Bereich. Nach einer Änderung der Grenzen startet **F5** einen neuen Suchlauf. Der Tabellenfilter lässt sich anschließend jederzeit ändern. Bei aktiver Katalogsuche können auch `.cat`-Dateien außerhalb des Bereichs die Signaturinformationen zu einem bestätigten Duplikat liefern.

Die eigenständige WPF-App übernimmt das helle und dunkle Theme sowie die Aufteilung von FileScope mit einklappbarem Menü, filterbarer Tabelle, Detailansicht und Statusleiste. **F1** oder der Knopf **Tastenkürzel** öffnet die gemeinsame Übersicht. **Esc** oder **Enter** schließt sie. Darin stehen auch die DuplicateFinder-Befehle **F5** für die Suche und **Strg+F** für den Filter. [Gemeinsame Tabellenkürzel](../README.md#tastenkürzel)

Im Optionsfenster steht **Design** oben rechts. Darunter stehen **Hashdarstellung** links und **Sprache** rechts. Alle drei Einstellungen wirken sofort als Vorschau. Die Hashdarstellung gilt für vorhandene und künftige Prüfsummen und Zertifikatsfingerabdrücke. Zur Auswahl stehen Hex mit Groß- oder Kleinbuchstaben, Hex mit Doppelpunkten, Base64 und Base64url. Tabelle und kopierbare Details verwenden das gewählte Format. **Speichern** übernimmt die Vorschau, **Abbrechen** und das Schließen über das Fensterkreuz stellen die vorherige Darstellung wieder her. Die letzte Spalte passt sich dem vollständigen Pfad an. Die automatische Pfadbreite lässt sich unter **Optionen → Analyse** ausschalten.

Bei einer ungültigen Eingabe zeigt die Fußzeile den Grund und der Fokus springt zum betroffenen Feld. Scheitert das Schreiben der Einstellungen, bleibt der Dialog mit allen Eingaben offen. Du kannst **Speichern** erneut versuchen oder mit **Abbrechen** die Vorschau zurücknehmen.

Hauptfenster, Optionen, Über und Tastenkürzel merken sich ihre normale Größe und ihren Maximierungszustand getrennt. Bei Über und Tastenkürzeln gilt die vorgegebene Startgröße bis zur ersten Größenänderung oder Maximierung. Ein minimiertes Fenster öffnet sich wieder im letzten sichtbaren Zustand. Während der Suche und während eines Papierkorbvorgangs ist **Optionen** ausgegraut.

Das Suchfeld findet Datei-Prüfsummen sowohl als Hex-Wert ohne Trennzeichen als auch in der gewählten Darstellung. Teilwerte genügen. Bei Base64 und Base64url zählt die Groß- und Kleinschreibung.

**Filter zurücksetzen** leert die Textsuche und setzt Ergebnisart und Signaturfilter auf „Alle“. Die Häkchen an Dateien und die Größenbegrenzung für den Suchlauf bleiben erhalten. Im Dropdown **Signaturstatus** lassen sich die primäre eingebettete Signatur, Katalogtreffer oder beide Quellen auswerten. Die Katalogspalte zeigt für jeden unterschiedlichen Code ein farbiges Badge. Stehen dort etwa `12` und `17`, finden beide Einzelcodes die Datei. Beim Bereinigen mit „Nur angezeigte Dateien“ gilt auch der gewählte Signaturfilter. [Sammelfilter, Hexcodes und Farben](../README.md#signaturstatus-filtern)

**Über DuplicateFinder** zeigt das DF-Symbol, die laufende Version, den Ablauf der Duplikatsuche und den vollständigen MIT-Lizenztext. Der Repository-Link führt zum gemeinsamen Projekt. [PNG-Vorlage des DF-Symbols](DuplicateFinder.png)

Ein Klick auf den Gruppenkopf klappt die Gruppe auf oder zu. Darin stehen die Kopien nach absteigender Aufbewahrungspriorität. Die Tabelle zeigt das **Änderungsdatum** mit Uhrzeit in UTC. Ist genau eine Zeile ausgewählt, zeigt **Ausgewählte Datei · vollständige Details / Kopieren** ihren vollständigen Pfad. Im aufgeklappten Textfeld lassen sich beispielsweise eine Prüfsumme oder ein Katalogpfad markieren und mit **Strg+C** kopieren.

In der Tabelle erweitert **Umschalt+Pfeil auf/ab** die Zeilenauswahl. **Strg+A** wählt alle Dateien der gefilterten Ansicht aus, auch in zugeklappten Gruppen. **Strg+C** kopiert die ausgewählten Zeilen mit Spaltenüberschriften. Die Häkchen für den Papierkorb bleiben bei diesen Tastenkürzeln und bei der Zeilenauswahl per **Strg+Klick** oder **Umschalt+Klick** erhalten.

Ein einfacher Klick auf eine Dateizeile oder ihr Kästchen schaltet das Häkchen für den Papierkorb um. Mit der Tastatur geht das über die **Leertaste** in der aktuellen Zeile. Während eines Such- oder Papierkorbvorgangs kann man vorhandene Ergebnisse weiterhin ansehen, scrollen und kopieren. Neue Markierungen sind erst nach Abschluss möglich.

**Rechtsklick**, **Umschalt+F10** oder die **Menütaste** öffnet das Kontextmenü einer Dateizeile. Ist genau eine Zeile ausgewählt, öffnet **Eigenschaften** den Windows-Eigenschaftendialog. **Im Explorer anzeigen** öffnet den übergeordneten Ordner und markiert die Datei. Ein Rechtsklick innerhalb einer Mehrfachauswahl erhält diese Auswahl. Ein Rechtsklick auf eine andere Datei wählt sie allein aus. Die Häkchen für den Papierkorb bleiben dabei erhalten.

**In den Papierkorb** zeigt wie der gleichnamige Knopf alle angekreuzten Dateien zur Bestätigung an, auch wenn ein Filter einige davon ausblendet. Während eines laufenden Vorgangs sind die drei Menübefehle gesperrt.

## Wie die Duplikatsuche arbeitet

1. **Dateien erfassen.** Für die Größenvorauswahl nutzt DuplicateFinder die Größe aus dem Verzeichniseintrag. Eine gewöhnliche Datei außerhalb der Grenzen wird damit schon vor dem Öffnen aussortiert. Bei Kandidaten prüft DuplicateFinder die Größe erneut am geöffneten Handle und ermittelt Änderungszeit und Datei-ID. Mehrere Hardlinks auf dieselbe Datei zählen einmal.
2. **Nach Größe vorsortieren.** Bei einer einmaligen Größe ist die Datei nach der Metadatenerfassung fertig. Die Prüfsummenfelder bleiben leer. Gibt es im gesamten Suchlauf keine mehrfach vorkommende Größe, endet er schon hier. Nur bei gleicher Größe berechnet `FileScope.Core` SHA-256 und alle zusätzlich gewählten Prüfsummen in einem gemeinsamen Lesedurchlauf. Die Fortschrittsanzeige zählt dabei die Dateien mit Größenübereinstimmung.
3. **Identität bestätigen.** Unterschiedliche SHA-256-Werte schließen ein Duplikat bereits aus. Bei gleicher Größe und gleichem SHA-256 vergleicht DuplicateFinder den Hauptdatenstrom Byte für Byte. Erst übereinstimmende Inhalte bilden eine Duplikatgruppe. Fällt eine Vergleichsdatei aus, erhält ihre Zeile eine Diagnose. Die übrigen lesbaren Dateien werden weiter miteinander verglichen.
4. **Duplikate untersuchen.** Innerhalb jeder bestätigten Gruppe prüft die App die NTFS-Zusatzdatenströme und je nach Auswahl eingebettete Signaturen und Katalogzuordnungen. Auch der Index loser Kataloge entsteht erst für die erste Gruppe. Eine einzeln vorkommende `.cat`-Datei kann dabei als Katalog für andere Dateien dienen. Bei Einzeldateien lautet der Signaturstatus „0 | Nicht geprüft“. Die automatische Bereinigung berücksichtigt die Aufbewahrungspriorität innerhalb jeder vollständig identischen Datenstromvariante.

## Bereinigung

Bei jedem Klick auf **Bereinigen** fragt ein Dialog nach dem Umfang. Neben jeder Wahl stehen die Anzahl der bereinigbaren Dateien und die Anzahl der betroffenen Gruppen.

| Wahl im Dialog | Welche Dateien kommen in Betracht? |
| --- | --- |
| **Nur angezeigte Dateien** | Alle Treffer des aktuellen Tabellenfilters, einschließlich der Zeilen außerhalb des Bildschirms und in zugeklappten Gruppen. |
| **Alle gescannten Dateien** | Alle Dateien dieses Suchlaufs, einschließlich der vom Tabellenfilter ausgeblendeten Zeilen. |

Für beide Optionen gelten dieselben Aufbewahrungsregeln. Maßgeblich ist immer die vollständige, geprüfte Duplikatgruppe. Alle Kopien mit der höchsten in dieser Gruppe vorhandenen Priorität bleiben erhalten, auch bei Gleichstand oder wenn der Filter sie ausblendet. Damit sind auch alle Suchordner der global höchsten Priorität geschützt. Bereinigen beginnt bei der niedrigsten Priorität und erfordert keine angekreuzten Zeilen.

Beispielsweise enthält eine Gruppe vier identische Dateien. Zwei liegen im Archiv (Priorität 30), eine in „Backup“ (20) und eine in „Downloads“ (10). Bei einem Pfadfilter auf „Downloads“ umfasst **Nur angezeigte Dateien** die eine Download-Kopie. **Alle gescannten Dateien** umfasst die Kopien in „Downloads“ und „Backup“. Beide Archivkopien bleiben in beiden Fällen erhalten.

**In den Papierkorb** wird aktiv, sobald mindestens eine Datei angekreuzt ist. Der Bestätigungsdialog führt alle ausgewählten Pfade auf. Damit lassen sich auch Einzeldateien und Dateien höchster Priorität manuell entfernen. Umfasst die Auswahl sämtliche bekannten Kopien einer Gruppe, weist der Dialog darauf hin.

Beide Aktionen verschieben ausschließlich in den Windows-Papierkorb. Kann Windows eine Datei dort nicht aufnehmen, bleibt sie am bisherigen Ort und erscheint mit dem Grund im Ergebnisbericht. Vor der automatischen Aktion werden Kandidat und verbleibende Referenz erneut auf Identität und unveränderten Inhalt geprüft. Hardlinks auf dieselbe Datei zählen nur einmal und bleiben von der automatischen Bereinigung ausgenommen. Verzeichnisverknüpfungen werden bei der Suche übersprungen.

## NTFS-Zusatzdatenströme und Authenticode

Ein benannter NTFS-Datenstrom kann Metadaten wie die Herkunftszone (`Zone.Identifier`) oder eigene Nutzdaten enthalten. Innerhalb einer Gruppe mit identischem Hauptdatenstrom werden deshalb auch die Namen und die vollständigen Inhalte der Zusatzdatenströme verglichen. Ein nur einseitig vorhandener Strom zählt als Unterschied. [Microsoft Learn zu NTFS-Datenströmen](https://learn.microsoft.com/en-us/windows/win32/fileio/file-streams)

Die Gruppennummer bestätigt die byteweise Übereinstimmung der Hauptdatenströme. Unterscheiden sich Namen oder Längen der Zusatzdatenströme, stehen bereits verschiedene Varianten fest. Bei gleichen Namen und Längen vergleicht DuplicateFinder auch deren Inhalte Byte für Byte.

Die Tabelle unterscheidet „Alle Datenströme identisch“, „Hauptdatenstrom identisch · Zusatzdatenströme unterschiedlich“ und „Hauptdatenstrom identisch · Zusatzdatenströme nicht vollständig geprüft“. Bei unterschiedlichen Zusatzdatenströmen gelten die Aufbewahrungsregeln innerhalb jeder vollständig identischen Variante. Bei unvollständiger Prüfung wird die betroffene Datei bei **Bereinigen** übersprungen.

Stimmen A und B einschließlich aller Datenströme überein, kann **Bereinigen** die Kopie mit der niedrigeren Priorität vorschlagen. Das gilt auch dann, wenn die Zusatzdatenströme einer dritten Datei C unlesbar sind. Bei gleicher Priorität bleiben A und B beide erhalten.

Die Signaturprüfung übernimmt die eingebetteten Signaturen, Statuswerte und Sperrprüfungsmodi von FileScope. Die zusätzliche Katalogsuche erfasst lose `.cat`-Dateien in den Suchordnern und fragt die registrierten Windows-Kataloge über den Mitgliedshash ab. Für die registrierten Kataloge werden die SHA-1- und SHA-256-Kontexte verwendet. Bei losen Katalogen werden auch SHA-384 und SHA-512 berücksichtigt. Kataloge werden direkt geprüft. Eine Installation ist dafür überflüssig. [Microsoft Learn zur automatischen und expliziten Katalogprüfung](https://learn.microsoft.com/en-us/windows/win32/seccrypto/using-signtool-to-verify-a-file-signature)

Ein Katalogtreffer gilt als vertrauenswürdig, wenn der Mitgliedshash der Datei zum signierten Eintrag passt und Windows die Vertrauensprüfung des Katalogs erfolgreich abschließt. Die Ergebnisansicht zeigt die Quelle (eingebettet oder Katalog) und bei einem Katalogtreffer den Katalogpfad. Liefert eine abgeschlossene Suche keinen Treffer, erscheint „Kein passender Katalog gefunden“. Die Entscheidung über Duplikate beruht auf dem Binärvergleich. [FileScope zur Signaturprüfung](../README.md#zertifikatsprüfung-genaue-bedeutung)


## Architektur

`FileScope.Core` liefert die Prüfsummenstrategien, den Ordnerscanner und die Windows-Signaturprüfung. `DuplicateFinder.Core` bildet die Duplikatgruppen, ermittelt die Aufbewahrungspriorität je Datenstromvariante und prüft die Dateien vor dem Verschieben erneut. `DuplicateFinder.App` enthält die WPF-Ansichten und hält ihre Einstellungen separat unter `%LOCALAPPDATA%\DuplicateFinder\settings.json`. `FileScope.Shared` stellt beiden Anwendungen die Signaturfilter, Codes, Katalogtexte und den Hashformatierer sowie unter Windows die Fluent-Stile, Farbpaletten, Statusfelder und die Dialoge für Programminformationen und Tastenkürzel bereit. Beide Apps verwenden auch dasselbe Dateikontextmenü und denselben Windows-Shell-Dienst für Eigenschaften und Explorer-Auswahl. [Native Methoden des Dateikontextmenüs](../README.md#dateikontextmenü-und-explorer)

| Native Schnittstelle | Aufgabe |
| --- | --- |
| `CreateFileW` | Öffnet Verzeichnisse für eine Metadatenabfrage, wenn zwei Pfade sich nur in der Groß- und Kleinschreibung unterscheiden. Nur bei übereinstimmender Volume- und Verzeichnis-ID kann DuplicateFinder den zweiten Besuch überspringen. |
| `GetFileInformationByHandleEx` | Liefert Datei-ID, Volume, Linkanzahl, Änderungskennung und die Liste benannter Datenströme. Bei älteren Providern dient der Dateiindex aus `GetFileInformationByHandle` als Alternative. Ohne verlässliche Identität erscheint eine Diagnose. |
| `CryptCATEnumerateMember` und `CryptCATAdminCalcHashFromFileHandle2` | Lesen Mitgliedseinträge loser Kataloge und berechnen den zum Dateiformat gehörenden Mitgliedshash. Windows kann dafür weitere Dateidaten lesen. |
| `CryptCATAdminEnumCatalogFromHash` und `WinVerifyTrust` | Finden registrierte Kataloge und prüfen den konkreten Mitgliedseintrag samt Katalogsignatur. Die Detailansicht unterscheidet eingebettete Signaturen, registrierte und lose Kataloge sowie eine unvollständige Suche. |
| `ITransferSource.RecycleItem` | Übergibt die geprüfte Datei an den bekannten Papierkorbordner der Windows-Shell. Erst ein zurückgeliefertes Papierkorbelement bestätigt das Verschieben. Schlägt der Vorgang fehl, erscheint der Grund im Bericht. |

Zur nativen Papierkorbfunktion beschreibt Microsoft auch den zurückgegebenen Verweis auf die verschobene Datei. [Microsoft Learn zu RecycleItem](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-itransfersource-recycleitem)
