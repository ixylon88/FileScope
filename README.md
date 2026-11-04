# FileScope

WPF-Dateianalyse für Windows mit .NET 10: Dateien auf das Fenster ziehen, Prüfsummen und eingebettete Authenticode-Signaturen prüfen, Ergebnisse filtern und kopieren.

Die Projektmappe enthält **genau zwei Projekte**. Der vollständige Quelltext liegt bei. Es werden keine Drittanbieter-NuGet-Pakete verwendet.

## Starten

Voraussetzung: Windows und ein installiertes **.NET 10 SDK**. Für Visual Studio den Workload **.NET-Desktopentwicklung** sowie eine Version mit .NET-10-Unterstützung verwenden.

1. Das ZIP vollständig entpacken.
2. `FileScope.sln` öffnen und `FileScope.App` als Startprojekt festlegen.
3. Mit F5 starten.

Alternativ im entpackten Hauptordner:

```powershell
dotnet build FileScope.sln -c Release
dotnet run --project src/FileScope.App -c Release
```

Oder mit dem mitgelieferten Skript:

```powershell
.\Build.ps1 -Run
```

Das SDK stellt bei Bedarf die Microsoft-Framework-Referenzpakete wieder her. Für die erste Wiederherstellung kann eine Internetverbindung erforderlich sein.

Die App normal ohne Administratorrechte starten. Windows kann Drag-and-drop aus dem Explorer in ein manuell erhöht gestartetes Programm blockieren.

## Bedienung

| Funktion | Verhalten |
| --- | --- |
| Dateien ablegen | Eine oder mehrere Dateien auf einen beliebigen Bereich des Fensters ziehen. Alternativ „Dateien öffnen“ / Ctrl+O. |
| Prüfoptionen | MD5, SHA-1, SHA-256, SHA-384 und SHA-512 separat auswählen. Beim ersten Start: SHA-256 und Signaturprüfung. Danach wird die gespeicherte Auswahl aller Prüf-Häkchen wiederhergestellt. |
| Sichtbare Spalten | Hashspalten folgen sofort ihren Häkchen. Die Signatur- und Zertifikatsspalten folgen gemeinsam der Signaturprüfung. Ausblenden behält bestehende Ergebnisse; neu gewählte Werte lassen sich mit „Alle neu prüfen“ berechnen. |
| Verarbeitung | Ein Dateistream, ein gemeinsamer Lesedurchlauf für alle gewählten Hashes; Dateien werden nacheinander im Hintergrund bearbeitet. |
| Zertifikate | Von Windows gemeldete primäre und zusätzliche eingebettete Authenticode-Signaturen jeweils prüfen; Unterzeichner, Inhaber, Aussteller, Seriennummer, Gültigkeitsdaten und Fingerabdrücke der gewählten Signatur anzeigen. |
| Signaturauswahl | Sobald mindestens eine geladene Datei mehrere Signaturen hat, erscheint eine Auswahlspalte pro Zeile. Die Einträge nennen Signaturnummer, Unterzeichner und Dateisignatur-Hashalgorithmus. Auch dasselbe Zertifikat kann mehrfach vorkommen. Status, Zertifikatsdaten, Datum, Details, Suche und Sortierung folgen der Auswahl; keine erneute Analyse. Bei einzelnen Signaturen ist die Auswahl deaktiviert. |
| Signaturzeitstempel | Authenticode-Gegensignatur oder RFC-3161-Zeitstempel in UTC. Fehlende, nicht auslesbare und nicht bestätigte Zeitstempel sind ausdrücklich beschriftet. |
| Online-Sperrprüfung | Optional. Ohne Häkchen werden ausschließlich lokale/zwischengespeicherte Ketten- und Sperrinformationen verwendet. |
| Alle neu prüfen | Alle geladenen Dateien mit der aktuellen Auswahl erneut verarbeiten, einschließlich ausgefilterter Dateien. Vorherige Ergebnisse dieser Dateien werden dabei ersetzt. |
| Suche | Textsuche über Pfad, Größe, Datum, Hashwerte, Status, Zertifikatsangaben und Diagnose, ohne Groß-/Kleinschreibung. |
| Filter | Textsuche, Analysestatus und Signaturstatus werden mit UND verknüpft. |
| Sortierung | Spaltenüberschrift anklicken; bei Bedarf mit Shift weitere Sortierspalten hinzufügen. Größen und Datumsfelder werden nach ihren Werten sortiert. |
| Tabelle | Schreibgeschützt und horizontal scrollbar. Einheitlich helle Zeilen mit den nativen WPF-Tabellenvorlagen. Die ersten zwei Spalten der gewählten Reihenfolge bleiben links stehen. |
| Spaltenreihenfolge | Zahnrad → Optionen: Alle 24 Spalten per Auf/Ab, ganz nach oben/unten oder Zurücksetzen anordnen, auch ausgeblendete Spalten. Verschieben der Tabellenüberschriften synchronisiert diese Liste. |
| Spaltenbreiten | Automatisch nach Überschrift und sichtbarem Inhalt, innerhalb fester Mindest-/Höchstbreiten je Spalte. Lange Werte sind über Tooltip, Details und Kopieren vollständig verfügbar. Manuelles Vergrößern bleibt möglich; „Breiten neu anpassen“ schaltet wieder auf Automatik. |
| Hashdarstellung | Unter Optionen: Hex groß, Hex klein, Hex mit Doppelpunkten, Base64 oder Base64url ohne Padding. Datei-Hashes und Zertifikatsfingerabdrücke wechseln sofort, auch in Details, Suche und Kopierausgabe. Keine erneute Dateianalyse. |
| Kopieren | Zeilen auswählen und Ctrl+C verwenden. Unter „Ausgewählte Datei“ ist der komplette Detailtext markierbar. |
| Abbrechen | Esc oder „Abbrechen“. Fertige Ergebnisse bleiben erhalten, noch offene Zeilen werden als abgebrochen markiert. |
| Leeren / Clear | Ctrl+L oder „Leeren“. Bricht einen laufenden Durchlauf ab und leert danach alle Zeilen und Filter. |
| Sprachen | Im Optionsfenster: Englisch, Deutsch, vereinfachtes Chinesisch. Umschalten ohne Neustart, auch bei bestehenden Ergebnissen und laufender Verarbeitung. |
| Fenster | Normale Breite/Höhe und Maximierungszustand werden beim Schließen gespeichert. Ein minimiert geschlossenes Fenster merkt sich den letzten normalen/maximierten Zustand. Neustart zentriert auf dem Primärmonitor; zu große Maße werden auf dessen Arbeitsfläche begrenzt. |
| Design | Festes helles Design. Der Theme-Umschalter und die eigenen Vorlagen für Zeilen/Zellen wurden entfernt. |

Während der Verarbeitung sind neue Dateiaufnahmen und Prüfoptionen gesperrt. Eine aktive native Windows-Signaturprüfung ist nicht mitten im Aufruf abbrechbar; „Abbrechen“ und „Leeren“ warten auf deren Rückkehr. Das Fenster bleibt bedienbar.

Mehrfach abgelegte **normalisierte Pfade** werden unter Windows ohne Beachtung der Groß-/Kleinschreibung dedupliziert. Unterschiedliche Pfade mit demselben Dateiinhalt bleiben separate Zeilen. Ordner werden als eigene Fehlerzeilen gemeldet und nicht rekursiv eingelesen. Ohne gewählten Hash und ohne Signaturprüfung werden nur Dateimetadaten ermittelt.

Die gewählten Prüfoptionen gelten als unveränderlicher Schnappschuss für einen Durchlauf. Ein späterer Sprach- oder Hashformatwechsel verändert keine Analysewerte. Die fünf Hash-Häkchen, Signaturprüfung und Online-Sperrprüfung werden bei jeder Änderung zusammen mit Sprache, Hashformat, Spaltenreihenfolge, Fenstergröße und Maximierungszustand in `%LOCALAPPDATA%\FileScope\settings.json` gespeichert. Ergebnisse und manuell veränderte Breiten gelten nur für die laufende Sitzung. Ungültige Einstellungen fallen auf Standardwerte zurück; Schreibfehler werden im Optionsfenster angezeigt, ohne die Dateianalyse zu blockieren.

Auch eine vollständig abgewählte Prüfauswahl wird gespeichert. Die Online-Sperrprüfung behält ihren gewählten Wert, wenn sie durch abgeschaltete Signaturprüfung ausgegraut ist; sie wird erst bei aktivierter Signaturprüfung verwendet. „Leeren“ setzt die Prüf-Häkchen nicht zurück.

Im Code liegen die Bindungswerte in `MainViewModel`: `ChecksumOptions[*].IsSelected`, `InspectCertificates` und `OnlineRevocation`. Die Startwerte sind zentral in `AnalysisPreferences` in `Services/PreferencesStore.cs` definiert. Ältere Einstellungen ohne den Abschnitt `Analysis` verwenden die bisherigen Standards. `OptionsViewModel` speichert alle Bereiche gemeinsam, damit eine Änderung der Prüf-Häkchen keine anderen Einstellungen überschreibt.

Neue Spalten werden bei einer bereits gespeicherten Reihenfolge hinten ergänzt und lassen sich unter Optionen einsortieren. Die Signaturauswahlspalte bleibt bei aktiver Signaturprüfung sichtbar, solange irgendeine geladene Datei mehrere Signaturen hat, auch wenn diese gerade ausgefiltert ist. Nach Leeren oder einer erneuten Analyse ohne Mehrfachsignaturen verschwindet sie.

Die Breitenautomatik misst den sichtbaren Inhalt und wächst bei Bedarf bis zur jeweiligen Obergrenze. Ein Sprach-/Formatwechsel, „Leeren“ oder „Breiten neu anpassen“ startet die Messung neu und erlaubt auch schmalere Spalten. Das Filtern allein setzt manuell gewählte Breiten nicht zurück.

Beim horizontalen Scrollen kann WPF für `CellsPanelHorizontalOffset` einen geringfügig negativen Wert berechnen. Die Fluent-Vorlage bindet diesen Wert direkt an die Breite ihres internen „Alles auswählen“-Knopfs, auch bei ausgeblendeten Zeilenüberschriften. `DataGridScrollFix` ergänzt beim Laden der Tabelle ausschließlich für diese Bindung eine Begrenzung auf null. Positive Breiten bleiben erhalten; die native Vorlage, fixierte Spalten, Virtualisierung und automatische Spaltenbreiten bleiben bestehen. Die Binding-Fehlerausgabe wird nicht abgeschaltet.

## Projekte und Entwurfsmuster

| Projekt / Komponente | Aufgabe |
| --- | --- |
| `FileScope.App` | WPF-Fenster, gruppiertes Menüband aus nativen Controls, DataGrid, ViewModels, Commands, Lokalisierung, Dateiauswahldialog und Drag-and-drop-Behavior. |
| `FileScope.Core` | Geschäftslogik: Modelle, Analysevertrag, Streaming-Prüfsummen, Strategiefabrik und Windows-Signaturprüfung. Keine WPF-Abhängigkeit. |
| `MainViewModel` | Koordiniert Benutzeraktionen, Status und gefilterte Ergebnisansicht. Berechnet selbst keine Hashwerte. |
| `FileAnalysisService` | Orchestriert Dateizugriff, Prüfsummen und Signaturstrategie. Liefert typisierte Resultate ohne UI-Texte zurück. |
| `IChecksumStrategy` / Factory | Strategy und Factory für inkrementelle Hashberechnungen. Pro Datei werden neue, anschließend entsorgte Strategieinstanzen erzeugt. |
| `ISignatureInspector` | Austauschschnittstelle für Signaturprüfung; implementiert durch `WindowsAuthenticodeInspector`. |
| `App.xaml.cs` | Composition Root: Abhängigkeiten werden per Konstruktor injiziert. Kein Service Locator und kein DI-Container erforderlich. |
| `RelayCommand` / `AsyncRelayCommand` | Command Pattern; asynchrone Ausführung mit Fehlerbehandlung und Schutz vor Mehrfachstart. |
| `FileDropBehavior` | Übersetzt Drag-and-drop-Ereignisse in ein ViewModel-Command. Auch über Textfeldern und Tabelle wirksam. |
| `OptionsViewModel` / `OptionsWindow` | Lokalisierte Optionen, vollständige Spaltenreihenfolge und Command-basierte Verschiebung. |
| `ColumnLayoutBehavior` | Überträgt stabile Spalten-IDs auf die `DisplayIndex`-Werte und synchronisiert manuelles Verschieben zurück; koordiniert die begrenzte automatische Breitenmessung. |
| `DigestFormatter` | Formatiert die vorhandenen Digest-Bytes; `FileRowViewModel` hält die dargestellten Werte im Cache. Die Core-Ergebnisse bleiben unverändert. |
| `IPreferencesStore` / `PreferencesStore` | Speichert kleine Benutzereinstellungen über eine temporäre Datei mit anschließendem Ersetzen der Zieldatei. |
| `BindingProxy` | Liefert den Fenster-DataContext an DataGrid-Spalten, die selbst keinen DataContext erben. Damit bleibt die Sichtbarkeit per Binding an die Prüfoptionen gekoppelt. |

MVVM trennt View, ViewModel und Domänenmodelle. Code-behind beschränkt sich auf Fensterlebenszyklus einschließlich Größenwiederherstellung, Öffnen des Optionsdialogs und Sichtbarhalten der ausgewählten Listenzeile. `INotifyPropertyChanged` sowie `ICollectionView` aktualisieren und filtern die Darstellung.

Das Menüband ist eine gruppierte native WPF-Befehlsleiste im Fluent-Stil, **kein zusätzlicher Ribbon-Control-Anbieter**. Der WPF-Tabellentyp heißt `DataGrid`; `DataGridView` gehört zu Windows Forms.

Die Kernbibliothek zielt auf `net10.0`; ihre Hash- und Orchestrierungslogik lässt sich auch ohne Windows verwenden. Die WPF-App zielt auf `net10.0-windows`. Die native Authenticode-Strategie meldet außerhalb von Windows ausdrücklich „UnsupportedPlatform“.

## Zertifikatsprüfung: genaue Bedeutung

Verwendet wird `WinVerifyTrust` mit der Windows-Authenticode-Richtlinie. Dadurch werden Dateiintegrität und Vertrauenskette durch Windows beurteilt; ein auslesbares Zertifikat allein wird nie als gültige Signatur gewertet. Windows berücksichtigt dabei auch unterstützte Signaturzeitstempel. Ein aktuell abgelaufenes Unterzeichnerzertifikat kann bei gültigem Zeitstempel trotzdem zu einer gültigen Signatur gehören.

- **Gültig · Sperrprüfung aus Cache:** Windows hat unter der lokalen Vertrauensrichtlinie und den vorhandenen Cache-Daten erfolgreich geprüft. Dies ist keine Zusicherung aktueller Online-Sperrinformationen.
- **Gültig · Online-Sperrprüfung:** Netzwerkzugriff für die Prüfung ist erlaubt. Windows darf trotzdem noch gültige Cache-Daten nutzen; eine frische Netzabfrage wird nicht erzwungen.
- **Sperrstatus unbekannt:** Die benötigten Sperrinformationen fehlen oder sind nicht erreichbar. Dies wird nicht als „gültig“ dargestellt.
- **Keine eingebettete Signatur** und **Signaturformat nicht unterstützt** bleiben getrennte Ergebnisse.
- Fehlerhafte, nicht vertrauenswürdige, gesperrte und zeitlich ungültige Zertifikate erhalten getrennte Statuswerte. Der originale Windows-Rückgabecode wird als Hexwert angezeigt.

Der Umfang sind **die primäre und alle zusätzlichen eingebetteten Authenticode-Signaturen, die Windows über `WINTRUST_SIGNATURE_SETTINGS` meldet**. Jede wird separat geprüft, auch wenn die primäre Prüfung fehlschlägt. Die Tabelle zeigt den Prüfstatus der ausgewählten Signatur, kein pauschales „alle Signaturen gültig“. Zertifikate aus der Vertrauenskette und Zeitstempel-Unterzeichner werden nicht als zusätzliche Dateisignaturen angeboten. Separate `.cat`-Zuordnungen, Signaturen innerhalb von Archiven, PDF-/Office-Signaturen sowie der Import eigenständiger CER/PFX-Zertifikate sind nicht implementiert. Hashwerte können unabhängig davon für beliebige lesbare Dateien berechnet werden. „Keine eingebettete Signatur“ bedeutet deshalb nicht, dass eine Datei niemals anderweitig signiert ist.

**Zum Datum:** „Gültig ab“ ist der Gültigkeitsbeginn des Zertifikats. Ein X.509-Zertifikat enthält kein allgemeines separates Feld für das Datum seiner eigenen Unterzeichnung. Die neue Spalte zeigt stattdessen die Zeit aus dem Zeitstempel der Dateisignatur: ältere PKCS#9-Gegensignaturen oder RFC-3161-Token. Ein Zeitstempel bestätigt bei erfolgreicher Prüfung, dass die Signatur zu dieser Zeit bereits vorlag; er ist nicht zwingend der exakte Moment der ursprünglichen Unterzeichnung. Selbst angegebene `signingTime`-Attribute des Dateisignierers, die aktuelle Uhrzeit und der Gültigkeitsbeginn werden nicht als Ersatz verwendet.

Die Beschriftung „bestätigt“ verlangt einen erfolgreichen Windows-Prüfstatus, einen fehlerfrei geprüften Gegensignierer und einen ausgelesenen Zeitstempel, der zur von Windows verwendeten Prüfzeit passt. Andernfalls bleibt ein lesbares Datum „nicht bestätigt“; das ist keine Behauptung, dass der Zeitstempel ungültig sei. Offline fehlende Vertrauens-/Sperrinformationen können eine Bestätigung verhindern.

Die angezeigten Zertifikatsfingerabdrücke beziehen sich auf das Zertifikat; die Hashspalten beziehen sich auf die Datei. Der Zertifikatssignaturalgorithmus ist ebenfalls der Algorithmus des Zertifikats und nicht automatisch der Dateisignatur. MD5/SHA-1 sind für den Vergleich vorhandener/älterer Prüfsummen enthalten; SHA-256 ist voreingestellt.

Dateien werden nur gelesen. Während der Analyse bleibt derselbe Lesehandle offen; auf Windows verhindert die Freigabeart paralleles Schreiben/Löschen. Der native Prüfer erhält denselben Handle. Es werden keine Programme gestartet, keine Archive entpackt und keine Zertifikate installiert. Bei eingeschalteter Online-Sperrprüfung kann Windows Zertifizierungsstellen kontaktieren.

## Sprachressourcen

Alle Oberflächentexte stehen in `src/FileScope.App/Resources`:

| Datei | Sprache |
| --- | --- |
| `Strings.resx` | Englisch, Fallback |
| `Strings.de.resx` | Deutsch |
| `Strings.zh-Hans.resx` | Vereinfachtes Chinesisch |

`LocExtension` bindet XAML-Texte an den `LocalizationService`. Status- und Fehlercodes bleiben in der Kernbibliothek sprachneutral. Vorhandene Zeilen und Filterauswahlen werden beim Umschalten aktualisiert. Technische Ausnahmebeschreibungen von Betriebssystem/.NET und originale Zertifikatsfelder bleiben unverändert. Der Start verwendet die Windows-UI-Sprache, sofern unterstützt, sonst Englisch.

## Erweitern

**Weiterer Hash:** `ChecksumAlgorithm` erweitern, die Factory um eine passende `IChecksumStrategy` ergänzen und eine Option/Spalte im App-Projekt hinzufügen. `StreamChecksumService` muss dabei nicht geändert werden.

**Andere Signaturprüfung:** `ISignatureInspector` implementieren und im Composition Root austauschen oder eine zusammengesetzte Strategie einfügen. Neue, dauerhaft benötigte Resultatfelder gehören in die Core-Modelle.

**Weitere Sprache:** Gleichnamige Schlüssel in `Strings.<culture>.resx` ergänzen, die Sprache in `LocalizationService.Languages` aufnehmen und gegebenenfalls die Startsprachauswahl erweitern.

## Überprüfung

```powershell
.\Verify.ps1
```

Das Skript baut die Projektmappe und führt Core-, Formatierungs-/Einstellungs- sowie unter Windows WPF/MVVM-Prüfungen aus. Es erstellt dafür temporäre Test-Harness-Projekte im ignorierten `artifacts/verify`-Ordner und entfernt diese danach. **Die eigentliche Projektmappe bleibt bei zwei Projekten.** Die Tests benötigen keine Testframework-NuGet-Pakete.

Optional mit einer bekannten Datei, die tatsächlich eine eingebettete Authenticode-Signatur besitzt:

```powershell
.\Verify.ps1 -SignedFile 'C:\Pfad\zur\signierten-datei.exe'
```

Der optionale Test liest diese Datei und prüft, dass Unterzeichnerdaten sowie der native Prüfrückgabecode ermittelt werden. Er verändert und startet sie nicht. Vertrauenswürdigkeit wird nicht pauschal angenommen, weil sie vom Rechner, den Stammzertifikaten und Sperrinformationen abhängt.

Der tatsächliche Ausführungsstand der mitgelieferten Version steht in `VALIDATION.md`. Ein zusätzlicher statischer Strukturcheck kann optional mit `python tools/check_structure.py` ausgeführt werden. Er ersetzt keinen .NET-Build.

## Eigenständige Windows-Ausgabe

```powershell
.\Build.ps1 -Publish -Runtime win-x64
```

Die Ausgabe liegt unter `artifacts/publish/win-x64`. Den **gesamten** Ordner weitergeben, einschließlich `de` und `zh-Hans`; diese enthalten die Sprachressourcen. Durch `--self-contained true` ist auf dem Zielrechner keine separate .NET-Desktop-Runtime erforderlich. Die Ausgabe wird nicht getrimmt, da WPF und dynamische Ressourcen Reflection verwenden. ARM64 bzw. x86 können über den Runtime-Parameter gewählt werden.

## Technische Referenzen

- [.NET-Supportübersicht](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [WPF in .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)
- [Fluent-Theme und ThemeMode](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90)
- [Mehrere Signaturen: WINTRUST_SIGNATURE_SETTINGS](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/ns-wintrust-wintrust_signature_settings)
- [Authenticode-Zeitstempel](https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures)
- [WinVerifyTrust](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/nf-wintrust-winverifytrust)
- [WINTRUST_DATA und Offline-Flags](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/ns-wintrust-wintrust_data)
- [WINTRUST_FILE_INFO und Dateihandle](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/ns-wintrust-wintrust_file_info)
