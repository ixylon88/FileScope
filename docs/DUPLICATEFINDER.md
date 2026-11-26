# DuplicateFinder (Entwurf)

DuplicateFinder soll binär identische Dateien in mehreren frei wählbaren Suchordnern finden. Jeder Suchordner erhält eine **Aufbewahrungspriorität**. Eine höhere Zahl bedeutet, dass seine Dateien bevorzugt erhalten bleiben. Fällt ein Pfad in mehrere Suchordner, zählt er einmal mit der höchsten zutreffenden Priorität.

## Ergebnisse und Bedienung

Die eigenständige WPF-App übernimmt das helle und dunkle Theme sowie die Aufteilung von FileScope mit einklappbarem Menü, filterbarer Tabelle, Detailansicht und Statusleiste. Optionen, Infofenster und die mit F1 erreichbaren „Tastenkürzel“ (`ShortcutsWindow`) erhalten die Einstellungen, Programminformationen und Befehle von DuplicateFinder.

Nach der Vorauswahl anhand der Dateigröße berechnet `FileScope.Core` alle gewählten Prüfsummen in einem Lesedurchlauf über den Hauptdatenstrom. Für die Suche genügt SHA-256. Stimmen Größe und Hash überein, vergleicht DuplicateFinder den Hauptdatenstrom der Kandidaten zusätzlich Byte für Byte. Jede bestätigte Gruppe lässt sich aufklappen. Darin stehen die Kopien nach absteigender Aufbewahrungspriorität.

Eine Checkbox am Zeilenanfang, ein Klick auf die Zeile und ihre farbige Hervorhebung bedienen dieselbe manuelle Auswahl. Bei großen Ergebnismengen bestimmen die gefilterten Ergebnisdaten den Umfang der Aktionen. Dadurch können Gruppen zugeklappt bleiben.

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

Die Signaturprüfung übernimmt die eingebetteten Signaturen, Statuswerte und Sperrprüfungsmodi von FileScope. Für DuplicateFinder ist zusätzlich eine Katalogsuche vorgesehen. Windows kann registrierte Kataloge über den Mitgliedshash finden. Lose `.cat`-Dateien in den Suchordnern sollen beim Scan erfasst und direkt geprüft werden, ohne sie zu installieren. [Microsoft Learn zur automatischen und expliziten Katalogprüfung](https://learn.microsoft.com/en-us/windows/win32/seccrypto/using-signtool-to-verify-a-file-signature)

Ein Katalogtreffer gilt als vertrauenswürdig, wenn der Mitgliedshash der Datei zum signierten Eintrag passt und Windows die Vertrauensprüfung des Katalogs erfolgreich abschließt. Die Ergebnisansicht zeigt die Quelle (eingebettet oder Katalog) und bei einem Katalogtreffer den Katalogpfad. Liefert eine abgeschlossene Suche keinen Treffer, erscheint „Kein passender Katalog gefunden“. Die Entscheidung über Duplikate beruht auf dem Binärvergleich. [FileScope zur Signaturprüfung](../README.md#zertifikatsprüfung-genaue-bedeutung)
