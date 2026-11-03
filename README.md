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
| Prüfoptionen | MD5, SHA-1, SHA-256, SHA-384 und SHA-512 separat auswählen. Standard: SHA-256 und Signaturprüfung. |
| Verarbeitung | Ein Dateistream, ein gemeinsamer Lesedurchlauf für alle gewählten Hashes; Dateien werden nacheinander im Hintergrund bearbeitet. |
| Zertifikate | Primäre eingebettete Authenticode-Signatur prüfen und Unterzeichner, Inhaber, Aussteller, Seriennummer, Gültigkeitsdaten und Fingerabdrücke anzeigen. |
| Online-Sperrprüfung | Optional. Ohne Häkchen werden ausschließlich lokale/zwischengespeicherte Ketten- und Sperrinformationen verwendet. |
| Alle neu prüfen | Alle geladenen Dateien mit der aktuellen Auswahl erneut verarbeiten, einschließlich ausgefilterter Dateien. Vorherige Ergebnisse dieser Dateien werden dabei ersetzt. |
| Suche | Textsuche über Pfad, Größe, Datum, Hashwerte, Status, Zertifikatsangaben und Diagnose, ohne Groß-/Kleinschreibung. |
| Filter | Textsuche, Analysestatus und Signaturstatus werden mit UND verknüpft. |
| Sortierung | Spaltenüberschrift anklicken; bei Bedarf mit Shift weitere Sortierspalten hinzufügen. Größen und Datumsfelder werden nach ihren Werten sortiert. |
| Tabelle | Schreibgeschützt, verschiebbare/vergrößerbare Spalten, horizontal scrollbar. Die ersten zwei Spalten bleiben links stehen. Jede zweite sichtbare Zeile ist leicht grau. |
| Kopieren | Zeilen auswählen und Ctrl+C verwenden. Unter „Ausgewählte Datei“ ist der komplette Detailtext markierbar. |
| Abbrechen | Esc oder „Abbrechen“. Fertige Ergebnisse bleiben erhalten, noch offene Zeilen werden als abgebrochen markiert. |
| Leeren / Clear | Ctrl+L oder „Leeren“. Bricht einen laufenden Durchlauf ab und leert danach alle Zeilen und Filter. |
| Sprachen | Englisch, Deutsch, vereinfachtes Chinesisch. Umschalten ohne Neustart, auch bei bestehenden Ergebnissen und laufender Verarbeitung. |

Während der Verarbeitung sind neue Dateiaufnahmen und Prüfoptionen gesperrt. Eine aktive native Windows-Signaturprüfung ist nicht mitten im Aufruf abbrechbar; „Abbrechen“ und „Leeren“ warten auf deren Rückkehr. Das Fenster bleibt bedienbar.

Mehrfach abgelegte **normalisierte Pfade** werden unter Windows ohne Beachtung der Groß-/Kleinschreibung dedupliziert. Unterschiedliche Pfade mit demselben Dateiinhalt bleiben separate Zeilen. Ordner werden als eigene Fehlerzeilen gemeldet und nicht rekursiv eingelesen. Ohne gewählten Hash und ohne Signaturprüfung werden nur Dateimetadaten ermittelt.

Die gewählten Optionen gelten als unveränderlicher Schnappschuss für einen Durchlauf. Ein späterer Sprachwechsel verändert keine Analysewerte. Einstellungen und Ergebnisse werden derzeit nur für die laufende Sitzung gehalten.

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

MVVM trennt View, ViewModel und Domänenmodelle. Der kleine Code-behind-Teil setzt nur den DataContext und behandelt den Fensterlebenszyklus. `INotifyPropertyChanged` sowie `ICollectionView` aktualisieren und filtern die Darstellung.

Das Menüband ist eine gruppierte native WPF-Befehlsleiste im Fluent-Stil, **kein zusätzlicher Ribbon-Control-Anbieter**. Der WPF-Tabellentyp heißt `DataGrid`; `DataGridView` gehört zu Windows Forms.

Die Kernbibliothek zielt auf `net10.0`; ihre Hash- und Orchestrierungslogik lässt sich auch ohne Windows verwenden. Die WPF-App zielt auf `net10.0-windows`. Die native Authenticode-Strategie meldet außerhalb von Windows ausdrücklich „UnsupportedPlatform“.

## Zertifikatsprüfung: genaue Bedeutung

Verwendet wird `WinVerifyTrust` mit der Windows-Authenticode-Richtlinie. Dadurch werden Dateiintegrität und Vertrauenskette durch Windows beurteilt; ein auslesbares Zertifikat allein wird nie als gültige Signatur gewertet. Windows berücksichtigt dabei auch unterstützte Signaturzeitstempel. Ein aktuell abgelaufenes Unterzeichnerzertifikat kann bei gültigem Zeitstempel trotzdem zu einer gültigen Signatur gehören.

- **Gültig · Sperrprüfung aus Cache:** Windows hat unter der lokalen Vertrauensrichtlinie und den vorhandenen Cache-Daten erfolgreich geprüft. Dies ist keine Zusicherung aktueller Online-Sperrinformationen.
- **Gültig · Online-Sperrprüfung:** Netzwerkzugriff für die Prüfung ist erlaubt. Windows darf trotzdem noch gültige Cache-Daten nutzen; eine frische Netzabfrage wird nicht erzwungen.
- **Sperrstatus unbekannt:** Die benötigten Sperrinformationen fehlen oder sind nicht erreichbar. Dies wird nicht als „gültig“ dargestellt.
- **Keine eingebettete Signatur** und **Signaturformat nicht unterstützt** bleiben getrennte Ergebnisse.
- Fehlerhafte, nicht vertrauenswürdige, gesperrte und zeitlich ungültige Zertifikate erhalten getrennte Statuswerte. Der originale Windows-Rückgabecode wird als Hexwert angezeigt.

Der Umfang ist **die primäre eingebettete Authenticode-Signatur und ihr Unterzeichnerzertifikat**. Separate `.cat`-Zuordnungen, sämtliche Zusatz-/Gegensignaturen, Signaturen innerhalb von Archiven, PDF-/Office-Signaturen sowie der Import eigenständiger CER/PFX-Zertifikate sind nicht implementiert. Hashwerte können unabhängig davon für beliebige lesbare Dateien berechnet werden. „Keine eingebettete Signatur“ bedeutet deshalb nicht, dass eine Datei niemals anderweitig signiert ist.

Die angezeigten Zertifikatsfingerabdrücke beziehen sich auf das Zertifikat; die Hashspalten beziehen sich auf die Datei. Der Zertifikatssignaturalgorithmus ist ebenfalls der Algorithmus des Zertifikats und nicht automatisch der Dateisignatur. MD5/SHA-1 sind für den Vergleich vorhandener/älterer Prüfsummen enthalten; SHA-256 ist voreingestellt.

Dateien werden nur gelesen. Während der Analyse bleibt derselbe Lesehandle offen; auf Windows verhindert die Freigabeart paralleles Schreiben/Löschen. Der native Prüfer erhält denselben Handle. Es werden keine Programme gestartet, keine Archive entpackt und keine Zertifikate installiert. Bei eingeschalteter Online-Sperrprüfung kann Windows Zertifizierungsstellen kontaktieren.

## Sprachressourcen

Alle 90 Oberflächentexte stehen in `src/FileScope.App/Resources`:

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

Das Skript baut die Projektmappe und führt Core- sowie unter Windows WPF/MVVM-Prüfungen aus. Es erstellt dafür temporäre Test-Harness-Projekte im ignorierten `artifacts/verify`-Ordner und entfernt diese danach. **Die eigentliche Projektmappe bleibt bei zwei Projekten.** Die Tests benötigen keine Testframework-NuGet-Pakete.

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
- [WinVerifyTrust](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/nf-wintrust-winverifytrust)
- [WINTRUST_DATA und Offline-Flags](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/ns-wintrust-wintrust_data)
- [WINTRUST_FILE_INFO und Dateihandle](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/ns-wintrust-wintrust_file_info)
