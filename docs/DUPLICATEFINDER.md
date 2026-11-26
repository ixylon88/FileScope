# DuplicateFinder

DuplicateFinder findet binär identische Dateien in mehreren frei wählbaren Suchordnern. Jeder Suchordner erhält eine **Aufbewahrungspriorität**. Eine höhere Zahl bedeutet, dass seine Dateien bevorzugt erhalten bleiben. Fällt ein Pfad in mehrere Suchordner, zählt er einmal mit der höchsten zutreffenden Priorität.

## Starten

In Visual Studio lässt sich `DuplicateFinder.App` als Startprojekt wählen. Aus dem Repository startet auf Windows auch dieser Befehl die App.

```powershell
dotnet run --project src/DuplicateFinder.App
```

Unter **Optionen → Suchordner → Ordner hinzufügen** die Suchordner wählen. Neue Suchordner beginnen gleichrangig bei 0. Ein Doppelklick auf eine Priorität macht sie editierbar. Beispielsweise kann das Archiv die Priorität 10 und der Downloadordner die Priorität 1 erhalten. **Speichern** übernimmt die Einstellungen. **Ordner durchsuchen** oder **F5** startet den Suchlauf. **Strg+F** fokussiert den Tabellenfilter. Die Filterung erfasst unter anderem Pfade, Prüfsummen und Herausgeber.

## Ergebnisse und Bedienung

Unter **Optionen → Suchordner → Dateigröße beim Einlesen** lassen sich eine Mindest- und eine Höchstgröße für den Hauptdatenstrom setzen. Anfangs sind beide Felder leer und alle Größen zugelassen. Jede Grenze lässt sich einzeln setzen und zählt mit. **Von 200 kB bis 1,5 MB** erfasst beispielsweise 200.000 bis 1.500.000 Byte. **Bis 0 B** findet ausschließlich leere Hauptdatenströme. Pro Feld stehen B, kB, MB und GB zur Wahl (1 kB = 1.000 Byte).

Der Bereich gilt vor Prüfsummen, Signaturprüfung und Binärvergleich. Auch **Alle gescannten Dateien** beim Bereinigen umfasst nur Dateien aus diesem Bereich. Nach einer Änderung der Grenzen startet **F5** einen neuen Suchlauf. Der Tabellenfilter lässt sich anschließend jederzeit ändern. Bei aktiver Katalogsuche können auch `.cat`-Dateien außerhalb des Bereichs die Signaturinformationen zu einer aufgenommenen Datei liefern.

Die eigenständige WPF-App übernimmt das helle und dunkle Theme sowie die Aufteilung von FileScope mit einklappbarem Menü, filterbarer Tabelle, Detailansicht und Statusleiste. Optionen, Infofenster und die mit F1 erreichbaren „Tastenkürzel“ (`ShortcutsWindow`) zeigen die Einstellungen, Programminformationen und Befehle von DuplicateFinder.

**Filter zurücksetzen** leert die Textsuche und setzt Ergebnisart und Signaturfilter auf „Alle“. Die Häkchen an Dateien und die Größenbegrenzung für den Suchlauf bleiben erhalten. Rechts neben dem Suchfeld lässt sich die primäre eingebettete Signatur nach denselben zwölf Codes und vier Sammelfiltern wie in FileScope filtern. Beispielsweise zeigt `2` nur gültige Signaturen mit Sperrprüfung aus dem Cache. Katalogtreffer stehen in einer eigenen Spalte. [Codes, Farben und Bedeutung](../README.md#dateien-nach-signaturergebnis-verschieben)

**Über DuplicateFinder** zeigt das DF-Symbol, die laufende Version, den Ablauf der Duplikatsuche und den vollständigen MIT-Lizenztext. Der Repository-Link führt zum gemeinsamen Projekt. [PNG-Vorlage des DF-Symbols](DuplicateFinder.png)

Ein Klick auf den Gruppenkopf klappt die Gruppe auf oder zu. Darin stehen die Kopien nach absteigender Aufbewahrungspriorität. Die Tabelle zeigt das **Änderungsdatum** mit Uhrzeit in UTC. Der aufklappbare Bereich **Ausgewählte Datei · Details und Kopieren** zeigt den vollständigen Pfad der aktiven Zeile. Im Textfeld lassen sich beispielsweise Prüfsummen oder Katalogpfade markieren und mit **Strg+C** kopieren.

Die Kästchen vor den Dateinamen bestimmen die manuelle Auswahl für den Papierkorb. Ein Klick auf eine Dateizeile oder ihr Kästchen schaltet die Markierung um. Die Häkchen bleiben erhalten, wenn man eine Gruppe zuklappt oder mit den Pfeiltasten eine andere Datei ansieht. Während eines Such- oder Papierkorbvorgangs kann man vorhandene Ergebnisse weiterhin ansehen, scrollen und kopieren. Neue Markierungen sind erst nach Abschluss möglich.

## Wie die Duplikatsuche arbeitet

1. **Dateien erfassen.** DuplicateFinder ermittelt Dateigröße, Änderungszeit und Datei-ID und wendet die eingestellten Größengrenzen an. Mehrere Hardlinks auf dieselbe Datei zählen einmal.
2. **Nach Größe vorsortieren.** Nur wenn mindestens zwei verschiedene Dateien dieselbe Größe in Byte haben, berechnet `FileScope.Core` SHA-256 und alle zusätzlich gewählten Prüfsummen in einem gemeinsamen Lesedurchlauf. Bei einer einmaligen Größe bleiben die Prüfsummenfelder leer. Zwei gleich große Dateien mit unterschiedlichem Inhalt erhalten also beide einen Hash, auch wenn am Ende beide einzeln stehen.
3. **Signaturen prüfen, falls aktiviert.** Eingebettete Signaturen und Katalogzuordnungen werden für jede aufgenommene, lesbare Datei geprüft, auch bei einmaliger Größe. Windows kann dafür zusätzliche Daten lesen und eigene Kataloghashes berechnen.
4. **Identität bestätigen.** Bei gleicher Größe und gleichem SHA-256 vergleicht DuplicateFinder den Hauptdatenstrom Byte für Byte. Erst übereinstimmende Inhalte bilden eine Duplikatgruppe. Innerhalb dieser Gruppe prüft die App auch Namen und Inhalte der NTFS-Zusatzdatenströme. Die automatische Bereinigung berücksichtigt die Aufbewahrungsprioritäten innerhalb jeder vollständig geprüften, identischen Variante.

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

Die Tabelle unterscheidet „Alle Datenströme identisch“, „Hauptdatenstrom identisch · Zusatzdatenströme unterschiedlich“ und „Hauptdatenstrom identisch · Zusatzdatenströme nicht vollständig geprüft“. Bei unterschiedlichen Zusatzdatenströmen gelten die Aufbewahrungsregeln innerhalb jeder vollständig identischen Variante. Bei unvollständiger Prüfung wird die betroffene Datei bei **Bereinigen** übersprungen.

Die Signaturprüfung übernimmt die eingebetteten Signaturen, Statuswerte und Sperrprüfungsmodi von FileScope. Die zusätzliche Katalogsuche erfasst lose `.cat`-Dateien in den Suchordnern und fragt die registrierten Windows-Kataloge über den Mitgliedshash ab. Für die registrierten Kataloge werden die SHA-1- und SHA-256-Kontexte verwendet. Bei losen Katalogen werden auch SHA-384 und SHA-512 berücksichtigt. Kataloge werden direkt geprüft. Eine Installation ist dafür überflüssig. [Microsoft Learn zur automatischen und expliziten Katalogprüfung](https://learn.microsoft.com/en-us/windows/win32/seccrypto/using-signtool-to-verify-a-file-signature)

Ein Katalogtreffer gilt als vertrauenswürdig, wenn der Mitgliedshash der Datei zum signierten Eintrag passt und Windows die Vertrauensprüfung des Katalogs erfolgreich abschließt. Die Ergebnisansicht zeigt die Quelle (eingebettet oder Katalog) und bei einem Katalogtreffer den Katalogpfad. Liefert eine abgeschlossene Suche keinen Treffer, erscheint „Kein passender Katalog gefunden“. Die Entscheidung über Duplikate beruht auf dem Binärvergleich. [FileScope zur Signaturprüfung](../README.md#zertifikatsprüfung-genaue-bedeutung)


## Architektur

`FileScope.Core` liefert die Prüfsummenstrategien, den Ordnerscanner und die Windows-Signaturprüfung. `DuplicateFinder.Core` bildet die Duplikatgruppen, ermittelt die Aufbewahrungspriorität je Datenstromvariante und prüft die Dateien vor dem Verschieben erneut. `DuplicateFinder.App` enthält die WPF-Ansichten und hält ihre Einstellungen separat unter `%LOCALAPPDATA%\DuplicateFinder\settings.json`. `FileScope.Shared` stellt beiden Anwendungen die Signaturfilter, Codes und Katalogtexte sowie unter Windows die Fluent-Stile, Farbpaletten, Statusfelder und den Über-Dialog bereit.

| Native Schnittstelle | Aufgabe |
| --- | --- |
| `GetFileInformationByHandleEx` | Liefert Datei-ID, Volume, Linkanzahl, Änderungskennung und die Liste benannter Datenströme. Bei älteren Providern dient der Dateiindex aus `GetFileInformationByHandle` als Alternative. Ohne verlässliche Identität erscheint eine Diagnose. |
| `CryptCATEnumerateMember` und `CryptCATAdminCalcHashFromFileHandle2` | Lesen Mitgliedseinträge loser Kataloge und berechnen den zum Dateiformat gehörenden Mitgliedshash. Windows kann dafür weitere Dateidaten lesen. |
| `CryptCATAdminEnumCatalogFromHash` und `WinVerifyTrust` | Finden registrierte Kataloge und prüfen den konkreten Mitgliedseintrag samt Katalogsignatur. Die Detailansicht unterscheidet eingebettete Signaturen, registrierte und lose Kataloge sowie eine unvollständige Suche. |
| `ITransferSource.RecycleItem` | Übergibt die geprüfte Datei an den bekannten Papierkorbordner der Windows-Shell. Erst ein zurückgeliefertes Papierkorbelement bestätigt das Verschieben. Schlägt der Vorgang fehl, erscheint der Grund im Bericht. |

Zur nativen Papierkorbfunktion beschreibt Microsoft auch den zurückgegebenen Verweis auf die verschobene Datei. [Microsoft Learn zu RecycleItem](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-itransfersource-recycleitem)
