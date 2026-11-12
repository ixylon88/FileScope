# Prüfstand der ausgelieferten Quellen

## Ergänzung: Regex-Filter vor der Dateiaufnahme

Stand: 2026-11-12, Basis `11181df` (`dev`), .NET SDK **10.0.404**.

Das eigene Feld „Dateiaufnahme (Regex)“ filtert neue Dateien vor dem Anlegen
von Ergebniszeilen. Dateiauswahl, rekursive Ordnersuche und gemischte Drops
verwenden denselben Filter auf Dateinamen einschließlich Endung, ohne Ordnerpfad.
Die Suche in bestehenden Ergebnissen bleibt unverändert. Ein leeres Muster
nimmt alle Dateien auf; bestehende Zeilen und deren erneute Analyse sind vom
Aufnahmefilter unabhängig.

Ungültige Muster sperren die Aufnahme und erscheinen mit einer Meldung am Feld
in allen drei Sprachen. Die aktive Prüfung läuft im Hintergrund mit 100 ms
Zeitlimit pro Dateiname. Ein Regex-Timeout verwirft die neue Auswahl vollständig;
ein Abbruch während der Filterprüfung liefert ebenfalls keine Teilauswahl.
Während des Vorgangs bleibt das gewählte Muster unverändert. Ausschlüsse werden
getrennt von doppelten Pfaden gezählt und in der Abschlussmeldung genannt.

| Prüfung | Ergebnis |
| --- | --- |
| `Verify.ps1 -NoColor` | Bestanden: Lösungsbuild mit 0 Warnungen / 0 Fehlern, **51** Skript-, **220** Core- und **210** Präsentationsprüfungen; Windows-UI-Ausführung unter Linux ausdrücklich übersprungen |
| Neue Core-Prüfungen | **19** zusätzliche Assertions: alle README-Beispiele, Groß-/Kleinschreibung, reine Dateinamen statt Ordnerpfaden, Kulturunabhängigkeit, Leerzeichen, doppelte/leere Kandidaten, Ausschlussanzahl, Syntaxfehler, Abbruch und begrenztes Backtracking |
| UI-Testprogramm | Für `win-x64` einschließlich App, Core und XAML kompiliert: 0 Warnungen / 0 Fehler; **24** zusätzliche Assertions, insgesamt **437** unter Windows erwartet |
| Neue UI-Prüfungen | Echtes Textfeld, alle drei Aufnahmewege, spätere Aufnahme zuvor ausgeschlossener Pfade, unabhängige Ansichtsfilter, unveränderte bestehende Zeilen, vollständig ausgeschlossene Ordner, Fehlermeldungen in drei Sprachen, Timeout, Neuanalyse, festes Muster während der Ordnersuche und Abbruch mit verspätetem Suchergebnis |
| Statische Struktur-/Ressourcenprüfung | **1474** bestanden; **275** Ressourcenschlüssel je Sprache |
| Patch | `git diff --check` fehlerfrei; Anwendung auf unveränderter Patchbasis und Bytevergleich der geänderten Dateien geprüft |

Unter den bisher dokumentierten Windows-Voraussetzungen werden nun **509**
Core-Assertions erwartet. Native Windows-Prüfungen und WPF-Laufzeitprüfungen
wurden hier nicht ausgeführt. Unter Windows sind insbesondere die sichtbare
Fehlermeldung, die Bedienung des neuen Felds und Datei-/Ordner-Drops mit aktiver
Aufnahmeauswahl noch über `Verify.ps1` und einen kurzen Bedienungstest zu prüfen.

## Ergänzung: Statuscodes und gemeinsame Verschiebegruppen

Stand: 11. November 2026. Patchbasis: `800c08c` (`ps update`) auf `dev`.
Prüfung mit .NET SDK **10.0.404** und PowerShell **7.6.9** unter Linux.

`SignatureGroup` ist die gemeinsame Zuordnung für die Statusanzeige und
`SignatureMoveSelection`. Die elf Core-Zustände ergeben zwölf Anzeigegruppen:
`0` für ungeprüft, `1` für gültig mit Online-Sperrprüfung, `2` für gültig mit
Cache-Sperrprüfung und `3` bis `B` für die übrigen Ergebnisse. Nur gültige
Signaturen unterscheiden den Prüfmodus. Die fest zugewiesenen Codes hängen
nicht von Enum-Reihenfolge oder nativen Windows-Fehlercodes ab.

Bei abgeschlossenen, erfolgreichen Dateianalysen im Leerlauf kennzeichnen
gleiche Codes von `1` bis `B` dieselbe Verschiebegruppe. `0` und unbekannte
Werte erlauben kein Verschieben. Die Anzeige folgt weiterhin der ausgewählten
Signatur und der aktuellen Sprache. Details, Kopieren, Suche und der gemeinsame
CSV-/JSON-Export übernehmen den Code über `SignatureText`; Farben und bestehende
Zielordner bleiben erhalten. Die README enthält die vollständige Zuordnung.

| Prüfung | Ergebnis |
| --- | --- |
| `Verify.ps1 -NoColor` | Bestanden: Lösungsbuild, **51** Skript-, **201** Core- und **210** Präsentationsprüfungen; Windows-UI-Ausführung unter Linux ausdrücklich übersprungen |
| Neue Präsentationsprüfungen | **11** zusätzliche Assertions: feste Codes, Sprachwechsel vorhandener Zeilen, Online-/Cache-Unterscheidung, unveränderte Analysewerte/Windows-Codes, Details/Suche/Export und Übereinstimmung mit den Verschiebegruppen |
| Vollständiger Gruppenvergleich | Alle **484** Paare aus elf Signaturzuständen und zwei Prüfmodi werden innerhalb einer Assertion geprüft: gleicher Code ungleich `0` genau dann, wenn die abgeschlossenen Zeilen eine gemeinsame Verschiebegruppe haben |
| Bestehende Signaturprüfungen | Auswahl einer anderen Signatur und Zurücksetzen prüfen zusätzlich den passenden Anzeigecode; ihre Assertion-Anzahl bleibt gleich |
| UI-Testprogramm | Für `win-x64` kompiliert, einschließlich App, Core und XAML: 0 Warnungen / 0 Fehler. Vorhandene Sprachprüfung angepasst; weiterhin **413** Assertions unter Windows erwartet |
| Statische Struktur-/Ressourcenprüfung | **1440** bestanden; weiterhin **267** Ressourcenschlüssel je Sprache |
| Patch | `git diff --check` fehlerfrei; Anwendung auf unveränderter Patchbasis geprüft |

Kein neuer Windows-Lauf: Die sichtbaren Badges und das Kontextmenü sind dort
noch mit gleichen sowie unterschiedlichen Codes zu prüfen, insbesondere mit
`1` und `2`, beim Signaturwechsel und nach einem Sprachwechsel.

## Korrektur: Lösungsbuild ohne RID und ANSI-Farbabschluss

Stand: 10. November 2026. Folgepatch auf `06ee93f` (`global.json`) auf `dev`.
Prüfung mit dem dort bereits festgelegten .NET SDK **10.0.404** und PowerShell
**7.6.9** unter Linux; die SDK-Auswahl wurde nicht verändert.

`Build.ps1` und der vorangestellte Lösungsbuild von `Verify.ps1` wählen die
Architektur über `Platform`. Der unzulässige Solution-Parameter `--runtime`
(NETSDK1134) entfällt. Auch `Build.ps1 -Run` ohne Publish verwendet keinen RID,
damit `--no-build` denselben Ausgabeordner findet. Publish und die einzelnen
architekturspezifischen Testprojekte behalten ihre gezielten RID-Aufrufe.

Die bisherigen Skriptprüfungen hatten den falschen Solution-Aufruf ebenfalls
zugelassen. Der Ersatz für `dotnet` weist ihn nun zurück. Die zusätzlichen
Prüfungen decken Build/Run, die Trennung von Solution-Plattform und Test-RID
sowie ANSI-Resets vor jedem Zeilenumbruch ab, einschließlich CRLF, LF, CR,
Leerzeilen, `-NoColor` und `NO_COLOR`.

| Prüfung | Ergebnis |
| --- | --- |
| `Build.ps1 -Runtime win-x64 -NoColor` | Echter Release-Lösungsbuild bestanden; 0 Warnungen / 0 Fehler |
| `Build.ps1 -Runtime win-arm64 -NoColor` | Echter Release-Lösungsbuild bestanden; 0 Warnungen / 0 Fehler |
| `Verify.ps1 -NoColor` | Vollständiger Lauf bestanden: Lösungsbuild, **51** Skript-, **201** Core- und **199** Präsentationsprüfungen; Windows-UI-Prüfungen unter Linux ausdrücklich übersprungen |
| Normaler Startpfad | Für x64 und Arm64 die MSBuild-Eigenschaften `RunCommand`, `TargetPath` und den leeren `RuntimeIdentifier` ausgewertet; die ermittelten Dateien existieren im jeweiligen Build-Ordner |
| PowerShell-Skripte | **51** Assertions bestanden (bisher 34); alle fünf PS1-Dateien ohne Parserfehler |
| Statische Struktur-/Ressourcenprüfung | **1438** bestanden; **267** Ressourcenschlüssel je Sprache |
| Patch | `git diff --check` fehlerfrei; Anwendung auf unveränderter Patchbasis geprüft |

Die Windows-App wurde hier nicht gestartet. Die tatsächliche WPF-Ausführung,
Windows PowerShell 5.1 und die Farbdarstellung im verwendeten Windows-Terminal
bleiben unter Windows zu prüfen. Die C#-Testquellen und deren Prüfanzahlen sind
unverändert. Die vier Publish-Varianten wurden für diesen Folgepatch nur in der
Skript-Orchestrierung geprüft, nicht erneut veröffentlicht.

## Ergänzung: Gemeinsame Build-/Verify-Skripte und ARM64

Stand: 10. November 2026. Patchbasis: `f25655d` auf `dev`. Prüfung mit dem in
`global.json` festgelegten .NET SDK **10.0.114**, Runtime **10.0.14** und PowerShell
**7.6.9** unter Linux. Die Skripte verwenden weiterhin Syntax für Windows
PowerShell 5.1; dessen Laufzeit ist hier nicht verfügbar.

| Prüfung | Ergebnis |
| --- | --- |
| `Verify.ps1 -NoColor` | Bestanden: Lösungsbuild, Skript-, Core- und Präsentationsprüfungen; WPF-Ausführung unter Linux ausdrücklich übersprungen |
| PowerShell-Orchestrierung | **34** Assertions bestanden; zusätzlich Syntaxprüfung der vier PS1-Dateien und Wiederherstellung des Exitcodes des Aufrufers geprüft |
| Core | **201** bestanden; Windows-spezifische Tests übersprungen, weiterhin **490** bei den bisher dokumentierten Voraussetzungen erwartet |
| Präsentation und Einstellungen | **199** bestanden; unveränderte C#-Testquellen |
| UI-Testprogramm | Für `win-x64` und `win-arm64` mit passenden Projektplattformen kompiliert; jeweils 0 Warnungen / 0 Fehler. Weiterhin **413** Assertions bei Ausführung unter Windows erwartet |
| Vier Publish-Varianten | x64/ARM64, jeweils framework-dependent und self-contained, über `Build.ps1` erfolgreich erstellt |
| Paketinhalt | PE-Architektur der EXE geprüft (`0x8664` / `0xAA64`), LICENSE unverändert, deutsche und chinesische Ressourcen vorhanden; `hostfxr.dll` ausschließlich in self-contained |
| Runtime-Auswahl | Beide self-contained Pakete enthalten .NET und Windows Desktop **10.0.14**; beide framework-dependent Pakete deklarieren **10.0.0** als Framework-Mindestversion |
| Statische Struktur-/Ressourcenprüfung | **1438** bestanden; **267** Ressourcenschlüssel je Sprache |
| Patch | `git diff --check` fehlerfrei; Anwendung auf unveränderter Patchbasis geprüft |

Die Skriptprüfungen arbeiten in einem temporären Verzeichnis mit einem Ersatz für
`dotnet`. Sie prüfen alle vier Publish-Kombinationen, Architektur/Plattform und
Konfiguration in Build sowie allen drei Testprogrammen, Pfade mit Leerzeichen und
Sonderzeichen, gezielte Bereinigung, Skip-Schalter und sofortigen Abbruch bei
SDK-, Build-, Publish- oder Testfehlern. Sie starten keine Windows-Anwendung und
ersetzen keinen nativen ARM64- oder WPF-Testlauf. Die Skriptanzahl ist getrennt von
den unveränderten drei C#-Testgruppen dokumentiert.

Der echte Core-Lauf fand außerdem eine zusätzliche unbedingte
`hashProgress.Report(0)`-Meldung: Sie zeigte auch ohne aktivierte Hashes die Phase
`Reading` an. Entfernt wurde ausschließlich diese redundante Meldung. Die bereits
vorhandene, an aktivierte Hashes gebundene Startmeldung bleibt erhalten. Die
bestehende Assertion für abgeschaltete Analysen deckt die Korrektur ab; Anzahl und
Inhalt der C#-Tests wurden dafür nicht geändert.

Ein paralleler MSBuild-Lösungsbuild brach in dieser Linux-Umgebung ohne konkrete
MSBuild-Fehlermeldung ab. Der gemeinsame Lösungsbuild läuft nun lokal und in CI
seriell mit `-m:1`. Der abschließende Verify-Lauf und ein erneuter ARM64-Publish
bestanden ohne einen zusätzlichen `DOTNET_PROCESSOR_COUNT`-Override. Die ersten
vier Publish-Prüfungen waren mit diesem Umgebungswert auf 1 erfolgt.

Noch unter Windows auszuführen: `Verify.ps1 -Runtime win-x64` bzw.
`Verify.ps1 -Runtime win-arm64` auf der passenden Architektur; ebenso ein Lauf
unter Windows PowerShell 5.1. Die veröffentlichten EXEs sind zusätzlich aus dem
Explorer zu starten und mit hellem/dunklem Design, Symbol und Splash anzusehen.
Die geänderte Runtime-Auswahl ist noch keine bestätigte Behebung des gemeldeten
Theme-Unterschieds. CI-/Release-Workflows und das WiX-Burn-Setup sind als Plan in
`docs/BUILD-RELEASE.md` festgehalten und noch nicht implementiert.

## Ergänzung: Dokumentation der nativen Shell-Aufrufe

Stand: 10. November 2026. Patchbasis: `a349320` auf `dev`.

Die fünf P/Invoke-Deklarationen in `FileShellService` sind mit Zweck, Parametern, Rückgabevertrag und Microsoft-Referenzen dokumentiert. Ergänzt sind der Vertrag von `IFileShellService`, COM-Threadbindung, die PIDL-Freigabe sowie die entsprechende Methodenübersicht unter Architektur → Native Windows-API-Anbindung → Dateikontextmenü und Explorer in der README.

Geprüft: 1431 statische Struktur-/Ressourcenprüfungen bestanden; alle zwölf XML-Dokumentationsblöcke der beiden bearbeiteten C#-Dateien sind wohlgeformt. Nach Entfernung der Kommentare stimmt der C#-Inhalt mit der Patchbasis überein; Methodenkörper, Signaturen und Importattribute bleiben unverändert. `git diff --check` ist fehlerfrei.

Reiner Dokumentationspatch: kein neuer Build und kein erneuter Testlauf. Testquellen, Testanzahlen und deren bisher dokumentierter Ausführungsstand bleiben unverändert (Core 201 unter Linux / 490 unter den dokumentierten Windows-Voraussetzungen erwartet; Präsentation 199; UI 413 erwartet).

## Ergänzung: Explizite Datenquelle für Vorlagenzellen

Stand: 9. November 2026. Folgepatch auf `FileScope-shortcuts-owner-fix.patch`; vorangegangene Änderungen sind nicht nochmals enthalten. .NET SDK 10.0.100 unter Linux.

| Prüfung | Ergebnis |
| --- | --- |
| Release-Rebuild einschließlich Core, App, XAML und UI-Testprogramm | Bestanden, 0 Warnungen / 0 Fehler |
| Statische Struktur- und Ressourcenprüfung | 1431 bestanden; weiterhin 267 Schlüssel je Sprache |
| Core / Präsentation | Quellen und Prüfungen unverändert; für diesen WPF-Patch nicht erneut ausgeführt |
| WPF / UI / MVVM | 413 Assertions erwartet: bisher 396, zusätzlich 17. Testprogramm kompiliert, hier nicht ausgeführt. |

Gemeldet wurde ein sporadisches `Cannot find element that provides DataContext` für `ContentPresenter.Content` mit `BindingExpression:(no path)`. Der konkrete Benutzerablauf ist weiterhin unbekannt. Die folgende Ursache passt zum Fehlertext, ist aber noch keine bestätigte Reproduktion des gemeldeten Vorfalls:

- In [.NET 10 `DataGridTemplateColumn.LoadTemplateContent`](https://github.com/dotnet/wpf/blob/v10.0.0/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/DataGridTemplateColumn.cs) erhält ein neu erzeugter Presenter eine leere Bindung auf `Content`.
- Für genau diese Eigenschaft sucht [`BindingExpression`](https://github.com/dotnet/wpf/blob/v10.0.0/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Data/BindingExpression.cs) den Datenkontext beim übergeordneten Element. Fehlt dieses beim letzten Bindungsversuch, meldet WPF `NoDataContext`.
- `CellBoundTemplateColumn` ersetzt diese Bindung unmittelbar nach der Erzeugung durch eine einseitige Bindung auf `DataContext` mit der jeweiligen Zelle als expliziter Quelle. Ein Zeilenobjekt wird nicht dauerhaft als Quelle festgehalten. Die Basisklasse wählt weiterhin Templates und Template-Selector; `DataGridCell` trennt verworfene Presenter wie bisher durch Ersetzen ihres Inhalts ab.

Nur die drei Vorlagenspalten verwenden die Ableitung: Analysestatus, Signaturauswahl und Signaturstatus. Es werden keine Bindingmeldungen unterdrückt und weder Virtualisierung noch native Fluent-Templates abgeschaltet.

Die 17 neuen Assertions entstehen aus neun Prüfstellen, davon vier für jeweils drei Produktionsspalten. Sie prüfen die Spaltenzuordnung, bewusst noch nicht eingebaute Presenter, einen geänderten und einen vorübergehend leeren Zellkontext, die tatsächliche Darstellung beider Badges und der Signaturauswahl, die Editing-Template-Rückfallregel sowie leere/gefilterte und wiederhergestellte Ansichten mit wechselnder Signaturspaltensichtbarkeit. Ein Binding-Trace prüft auch das Schließen des Fensters. Die vorhandenen Theme- und Signaturprüfungen laufen weiterhin mit den tatsächlichen Vorlagen.

Unter Windows noch ausführen: `.\Verify.ps1`. Anschließend beim bisherigen Arbeitsablauf besonders Scrollen, Filterwechsel, Ein-/Ausblenden von Signaturspalten und Designwechsel beobachten. Ohne diesen Lauf wird die ursprüngliche sporadische Meldung ausdrücklich nicht als abschließend behoben gewertet.

## Ergänzung: Tastenkürzelübersicht und Owner-Testkorrektur

Stand: 9. November 2026. Folgepatch auf `FileScope-ui-selection-path-fixes.patch` (Git-Ausgangspunkt `189fde4`). .NET SDK 10.0.100 unter Linux.

| Prüfung | Ergebnis |
| --- | --- |
| Release-Rebuild einschließlich Core, App, XAML und UI-Testprogramm | Bestanden, 0 Warnungen / 0 Fehler |
| Statische Struktur- und Ressourcenprüfung | 1430 bestanden; 267 Schlüssel je Sprache |
| Core / Präsentation | Quelltext und zuletzt bestätigte 201 / 199 Assertions unverändert; für diesen Dialogpatch nicht erneut ausgeführt |
| WPF / UI / MVVM | 396 Assertions erwartet: bisher 379, zusätzlich 17 zur Tastenkürzelübersicht. Testprogramm kompiliert, hier nicht ausgeführt. |

`CheckPathPresentation` wies im vorherigen Patch `Owner` zu, bevor das Hauptfenster gezeigt wurde. Die korrigierte Reihenfolge lautet `window.Show()`, `settings.Owner = window`, `settings.Show()`. Die bestehenden Prüfbedingungen bleiben erhalten; der Fehler wird weder abgefangen noch durch Entfernen des Besitzers umgangen. Die übrigen Owner-Zuweisungen wurden auf dieselbe Voraussetzung geprüft.

Die neue Übersicht verwendet vorhandene Lokalisierung und Fluent-Ressourcen. `ApplicationCommands.Help` verbindet den tatsächlichen Knopf und F1 mit demselben modalen Dialog. Die 17 zusätzlichen UI-Assertions entstehen aus zwölf Prüfstellen, davon zwei für jeweils drei Sprachen und eine für beide Designs. Geprüft werden Command-/Tastenzuordnung, die vorhandenen Strg+O-/Strg+L-/Esc-Bindungen, Button-Aufruf über den Automation-Peer, F1-Command-Routing, Besitzer und Modalität, Schließen, Übersetzungen aller zwölf Kürzelpaare, Themewechsel, Scrollbarkeit bei Mindestgröße, unveränderte Einstellungen und Bindingfehler. Die Fenster werden auch in diesen Tests erst nach Anzeige des Hauptfensters mit ihrem Besitzer verbunden.

Ein erster inkrementeller Linux-Build meldete einen doppelten BAML-Ressourcenschlüssel; der anschließende vollständige Rebuild räumte die generierten Zwischenstände auf. Die Projektdateien und ihre Ressourcendefinitionen wurden dafür nicht verändert.

Unter Windows noch ausführen: `.\Verify.ps1`. Anschließend Knopf und F1, Schließen mit Esc/Enter, BildAuf/BildAb und Strg+Pos1/Strg+Ende bei Tabellenfokus sowie die Übersicht in beiden Designs und bei höherer Bildschirmskalierung prüfen. Die tatsächliche WPF-Laufzeit und Darstellung sind hier weiterhin nicht ausführbar.

## Ergänzung: Menüauswahl und Pfaddarstellung

Stand: 9. November 2026. Patchbasis: `189fde4` (`move files by signature`). .NET SDK 10.0.100 unter Linux.

| Prüfung | Ergebnis |
| --- | --- |
| Release-Build einschließlich Core, App, XAML und UI-Testprogramm | Bestanden, 0 Warnungen / 0 Fehler |
| Präsentation und Einstellungen | 199 bestanden; 5 zusätzliche Prüfungen zu Standardwert, explizitem Abschalten, Neustart und erneutem Einschalten der Pfadbreitenautomatik |
| Statische Struktur- und Ressourcenprüfung | 1300 bestanden; 235 Schlüssel je Sprache |
| Core | Quelltext und 201 unter Linux zuletzt bestätigte Assertions unverändert; für diesen UI-Patch nicht erneut ausgeführt |
| WPF / UI / MVVM | 379 Assertions erwartet; Testprogramm kompiliert, hier nicht ausgeführt. Bisher 363, zusätzlich 16. |

Der gemeldete `ElementNotEnabledException` trat beim Aufrufen eines nach Sprachwechsel weiterverwendeten Menüeintrags auf. Der bisherige Command-Parameter hing am `PlacementTarget.DataContext` eines recyclebaren DataGridRow. Der Popup-DataContext verweist nun auf das Haupt-ViewModel; alle Aktionen lösen ihre Ziele anhand der tatsächlichen Auswahl auf. Die bestehende Sprachwechselprüfung fordert zusätzlich `IsEnabled` und ausführbares Command, bevor sie weiterhin denselben echten Menüeintrag über den Automation-Peer aufruft. Der Test umgeht den fehlschlagenden Aufruf nicht.

Die 16 zusätzlichen UI-Assertions bestehen aus einer Prüfung für Verschieben/Trennlinie an erster Stelle, zwei zur gesperrten bzw. wieder aktivierten Einzeldateiaktion und 13 zum Pfadkopf und zur Breitenoption (11 Prüfstellen, eine davon für drei Sprachen). Sie prüfen die echte Mehrfachauswahl, leeren Detailtext bei keiner/mehreren Dateien, Pfadaktualisierung nach Verschieben, das reale Options-Häkchen, Speicherung, das Beibehalten anderer manueller Breiten, Sprachwechsel und „Breiten neu anpassen“ bei ausgeschalteter Pfadautomatik sowie die Rückkehr des Pfads ans Ende nach Zurücksetzen der Spaltenreihenfolge. Bestehende Menütests wurden auf Auswahl als Befehlsziel umgestellt; die Sicherheitsprüfungen bleiben bestehen.

Der Schutz vor wiederholten Status-Unterordnern ist bereits im unveränderten `FileMoveService` enthalten und durch den bestehenden Core-Test abgedeckt. POS1/ENDE behalten das native WPF-Verhalten innerhalb einer Zeile; STRG+POS1/STRG+ENDE springen an Anfang/Ende der Tabelle. Die README nennt diese Kombinationen und konkrete Haltepunkte zur Trennung von Dateiöffnung, Lesen, Hashberechnung und Metadaten-/Signaturprüfungen. An der Analyse oder ihren Dateizugriffen wurde nichts geändert.

Unter Windows noch ausführen: `.\Verify.ps1`. Zusätzlich den offenen Menüeintrag nach Sprachwechsel, Mehrfachauswahl, eine weit nach vorne verschobene Pfadspalte und einen sehr langen Pfad im Detailkopf ansehen. Der nicht reproduzierbare zusätzliche XAML-Laufzeitfehler ist ohne Fehlermeldung/Stacktrace nicht zugeordnet und wird hier nicht als behoben behauptet.

## Ergänzung: Dateien nach Signaturstatus verschieben

Stand: 9. November 2026. Zweiter Patch, anzuwenden nach dem Fortschrittspatch auf `f3148e7` (`readme updated`). Ausführung mit .NET SDK 10.0.100 unter Linux.

| Prüfung | Ergebnis |
| --- | --- |
| Release-Build einschließlich Core, App, XAML und UI-Testprogramm | Bestanden, 0 Warnungen / 0 Fehler |
| Core | 201 bestanden; 13 zusätzliche Prüfungen mit echten temporären Dateien, Symlinks und Hardlinks. Native Windows-Prüfungen hier übersprungen; ausgehend vom bisherigen Windows-Umfang mit identischen Voraussetzungen 490 erwartet. |
| Präsentation und Einstellungen | 194 bestanden; 19 zusätzliche Assertions für Auswahlgruppen, Pfadwechsel, Export und alle drei Sprachen |
| Statische Struktur- und Ressourcenprüfung | 1284 bestanden; 232 Schlüssel je Sprache |
| WPF / UI / MVVM | 363 Assertions erwartet: 343 nach dem ersten Patch, zusätzlich 20. Testprogramm kompiliert, hier nicht ausgeführt. |

Die Core-Prüfungen decken gleichnamige Dateien in verschiedenen Elternordnern, unveränderte Inhalte, belegte Ziele ohne Überschreiben, wiederholtes Verschieben, fehlende Quellen, blockierte Zielordner, bereits geladene Zielpfade und Abbruch vor bzw. zwischen Dateien ab. Symlink-Prüfungen stellen sicher, dass weder verlinkte Quelldateien noch verlinkte Zielordner verschoben bzw. verfolgt werden. Ein Hardlink-Test prüft, dass der andere Name erhalten bleibt. Link-Prüfungen können unter Windows abhängig von Dateisystem und Berechtigungen übersprungen werden.

Die Präsentationsprüfungen unterscheiden gemischte, ungeprüfte und fehlgeschlagene Zeilen, ausgewählte Mehrfachsignaturen und gültige Online-/Offline-Ergebnisse. Sie prüfen den Erhalt von Hashes und Signaturauswahl beim Pfadwechsel sowie den neuen Pfad in Details und Export. Alle lokalisierten Ordnernamen werden auf Eindeutigkeit, Windows-taugliche Namen und höchstens acht Zeichen geprüft; jeder Berichteintrag enthält Ergebnis und beide Pfade.

Die UI-Prüfungen verbinden die echte DataGrid-Mehrfachauswahl mit dem Kontextmenü. Ein injizierter Verschiebedienst vermeidet Änderungen an echten Benutzerdateien; er prüft Arbeitsthread, gesperrte Befehle, erfolgreiche und übersprungene Einträge, Abbruch mit Teilergebnis sowie aktualisierte Pfade und Duplikaterkennung. Der reale Berichtsdienst öffnet zusätzlich ein modales Fenster mit Besitzer; die Tests prüfen den Schließen-Knopf, Skalierbarkeit, Scrollbalken, Sprachen, Themes und Bindingfehler. Die 20 Assertions ergeben sich aus 17 Prüfstellen, davon einer für drei Sprachen und einer für beide Designs.

Unter Windows noch ausführen: `.\Verify.ps1`. Manuell an einem Testordner prüfen: Rechtsklick auf eine neue bzw. schon markierte Zeile, Mehrfachauswahl, Menütaste / Umschalt+F10, tatsächliche Verschiebung und Namenskollision, Abbruch sowie den langen Bericht bei höherer Bildschirmskalierung. Keine produktiven Dateien wurden hier verschoben; Windows-Laufzeitverhalten und Darstellung sind noch nicht bestätigt.

## Ergänzung: Gepufferter Fortschritt und Analysephasen

Stand: 9. November 2026. Patchbasis: `f3148e7` (`readme updated`). Ausführung mit .NET SDK 10.0.100 unter Linux.

| Prüfung | Ergebnis |
| --- | --- |
| Release-Build einschließlich Core, App, XAML und UI-Testprogramm | Bestanden, 0 Warnungen / 0 Fehler |
| Core | 188 bestanden; 5 neue Prüfungen für aktivierte Phasen und deren Reihenfolge. Native Windows-Prüfungen übersprungen; 477 statt zuletzt dokumentierter 472 unter Windows erwartet. |
| Präsentation und Einstellungen | 175 bestanden; 10 neue Assertions für Puffer, konkurrierende Meldungen, verspätete Reporter und lokalisierte Phasen |
| Statische Struktur- und Ressourcenprüfung | 1195 bestanden; 206 Schlüssel je Sprache |
| WPF / UI / MVVM | 343 Assertions erwartet, davon 11 neue. Kompiliert, hier nicht ausgeführt. |

Die Pufferprüfungen senden 20.000 Fortschrittsmeldungen, ohne Synchronisationskontext-Aufträge zu erzeugen; sie prüfen auch, dass Resultate erhalten bleiben, nur einmal abgeholt werden und ein alter Reporter die neue Datei nicht überschreibt. UI-Prüfungen verwenden einen kontrolliert wartenden Analysedienst, echte Fenster/Bindungen und den Dispatcher-Timer. Sie prüfen Phasentexte einschließlich Online-Sperrprüfung, UI-Thread-Zugehörigkeit, reduzierte Änderungsmeldungen, pausierte Filter, einmaliges abschließendes Filtern/Sortieren und sofortigen Endzustand. Die vorhandenen Abbruchprüfungen bleiben erhalten. Die statische Prüfung berücksichtigt zusätzlich die bereits vorhandenen sechs Workspace-Tasks für beide Architekturen mit/ohne eigene Runtime.

Unter Windows noch ausführen: `.\Verify.ps1`. Anschließend einen großen Durchlauf mit und ohne gesetzten Filter vergleichen und eine größere Datei mit Online-Sperrprüfung beobachten. Konkrete Laufzeitgewinne und die sichtbare Verringerung des Flackerns wurden hier nicht auf Windows gemessen.

## Ergänzung: UTC-Zeitstempel im Exportdateinamen

Stand: 8. November 2026. Patchbasis: `19ae3eb` (`publish self-contained or not`). Der Vorschlag im Speichern-Dialog verwendet `DateTime.UtcNow` mit kulturunabhängiger Formatierung und Windows-tauglichen Bindestrichen in der Uhrzeit. CSV und JSON verwenden denselben Namensstamm; die Endung ergänzt weiterhin der gewählte Dateityp.

Release-Build von App und Core mit .NET SDK 10.0.100 unter Linux: bestanden, 0 Warnungen / 0 Fehler. Der Windows-Speichern-Dialog wurde hier nicht ausgeführt. Keine Prüfbedingungen hinzugefügt oder entfernt; die dokumentierten Testzahlen bleiben unverändert. Die drei Testgruppen wurden für diese Änderung des Namensvorschlags nicht erneut ausgeführt.

## Ergänzung: Splashscreen und Dateikontextmenü

Stand: 8. November 2026. Patchbasis: `972d0d6` (`app icon`). Ausführung mit .NET SDK 10.0.100 unter Linux.

| Prüfung | Ergebnis |
| --- | --- |
| Release-Build einschließlich App, Core, XAML und UI-Testprogramm | Bestanden, 0 Warnungen / 0 Fehler |
| Core | 183 bestanden; native Windows-Prüfungen übersprungen. Prüfumfang unverändert, zuletzt unter Windows dokumentiert: 472. |
| Präsentation und Einstellungen | 165 bestanden; Prüfumfang unverändert |
| Statische Struktur- und Ressourcenprüfung | 1172 bestanden; 202 Ressourcenschlüssel je Sprache |
| Splash-Einbettung | Die PNG-Ressource aus der gebauten App-Assembly ist bytegenau identisch mit dem 74.472 Byte großen Quellbild. Generierter `Show(true)`-Aufruf vor `App.InitializeComponent()` geprüft. |
| WPF / UI / MVVM | 332 Assertions erwartet: bisher 304, zusätzlich 27 zum Dateikontextmenü und eine zur Wiederverwendung der vorhandenen Theme-Palette. Kompiliert, hier nicht ausgeführt. |

Die neuen UI-Prüfungen öffnen echte Zeilen-Kontextmenüs und rufen Menüeinträge über ihre Automation-Peers auf. Ein injizierter Shell-Testdienst zeichnet Aktion und Pfad auf, statt Explorer oder Eigenschaftsdialoge zu öffnen. Geprüft werden die angeklickte statt der vorher ausgewählten Zeile, Mehrfachauswahl, erneutes Öffnen für eine andere Zeile, Sonderzeichen und Unicode im Pfad, ungültige Command-Parameter, sichtbare Fehlermeldungen, unveränderte Analyseergebnisse und Durchlaufstatistik, drei Sprachen, beide Themes, erfolgreicher Wiederholungsversuch, Mehrfachstartschutz und eine späte Explorer-Antwort nach „Leeren“. Der reale Shell-Dienst wird nur mit einer inzwischen gelöschten Datei aufgerufen; dabei müssen seine Vorprüfungen ohne Shell-Fenster abbrechen. Die 27 Assertions entstehen aus 22 Prüfstellen, davon zwei für jeweils drei Sprachen und eine für beide Themes.

Der Splash ist die unveränderte 256-Pixel-PNG-Variante des vorhandenen Icons. Es gibt keine Mindestanzeigedauer, kein eigenes Splash-Fenster und keine Änderung an den Publish-Optimierungen. `docs/build-icon.ps1` kann dieses PNG künftig zusammen mit dem Icon aus der ursprünglichen Vorlage speichern.

Beim Review der vorhandenen Änderungen wurde die Palette-Erzeugung hinter die bereits vorhandene Übereinstimmungsprüfung verschoben; der normale Start mit der Palette aus `App.xaml` lädt dadurch keine unbenutzte Ersatzpalette mehr. Dokumentation und EditorConfig-Kommentar nennen jetzt die bereits eingebundenen Threading-Analyzer. Die statischen Prüfungen berücksichtigen den vorhandenen ARM64-Publish-Task. Eine bereits vorhandene Lambda in `CoreChecks` wurde für `IDE0200` durch die gleichwertige Methodengruppe ersetzt; der Core-Prüfumfang bleibt gleich.

Unter Windows noch ausführen: `.\Verify.ps1`. Manuell in x64 und ARM64: Splash beim ersten und bei weiteren Starts ansehen, Rechtsklick sowie Menütaste / Umschalt+F10 auf Ergebniszeilen prüfen, tatsächliche Windows-Eigenschaften öffnen und eine tief verschachtelte Datei mit Leerzeichen/Unicode im Explorer markieren. Auch nach Themewechsel und bei höherer Bildschirmskalierung ansehen. Diese Windows-Laufzeit- und Darstellungsprüfungen sowie das PowerShell-Bildskript wurden hier nicht ausgeführt.

## Ergänzung: Über-Dialog

Stand: 7. November 2026. Patchbasis: `faadfa3` (`test`). Ausführung mit .NET SDK 10.0.100 unter Linux.

| Prüfung | Ergebnis |
| --- | --- |
| Release-Build einschließlich App, Core, WPF-XAML und UI-Testprogramm | Bestanden, 0 Warnungen / 0 Fehler |
| Core | 183 bestanden; native Windows-Prüfungen übersprungen. Unveränderter Prüfumfang, zuletzt unter Windows dokumentiert: 472. |
| Präsentation und Einstellungen | 165 bestanden; Prüfumfang unverändert |
| Statische Struktur- und Ressourcenprüfung | 1141 bestanden; 197 Ressourcenschlüssel je Sprache |
| Lizenz und Versionsmetadaten der gebauten App-Assembly | Eingebettete Lizenz und Kopie im Build-Ordner bytegenau mit `LICENSE` verglichen; `AssemblyInformationalVersion` einschließlich Commit-Zusatz gelesen. |
| WPF / UI / MVVM | 304 Assertions erwartet, davon 19 neue Prüfungen für den Über-Dialog. Kompiliert, hier nicht ausgeführt. |

Der Dialog wird über den echten Info-Knopf geöffnet und als modales, dem Hauptfenster zugeordnetes Fenster geprüft. Weitere Prüfungen betreffen die Version aus der App-Assembly statt aus dem Testhost, Offline-Lizenz und Copyright, Schließen, beide Linkziele, Fehler und erneuten Erfolg beim Browseraufruf, alle drei Sprachen, beide Designs, Scrollen bei kleiner Fenstergröße, unveränderte Einstellungen und WPF-Bindungsfehler. Der Browserdienst wird durch einen kleinen Testdienst ersetzt; die Tests starten keinen Browser. Die 19 zusätzlichen Assertions ergeben sich aus 14 Prüfstellen, darunter zwei Prüfungen für jeweils drei Sprachen und eine Prüfung für beide Designs.

Die `LICENSE` wird aus der einen Repository-Datei eingebettet und als Datei in die Build-/Publish-Ausgabe übernommen. Der Über-Dialog lädt keine Online-Inhalte; nur das ausdrückliche Anklicken eines Links ruft den Browserdienst auf. Die Aussage zum Dateizugriff unterscheidet bewusst einmaliges Öffnen, gemeinsamen Hash-Lesedurchlauf und zusätzliche Header-/Windows-Prüfungen.

Unter Windows noch ausführen: `.\Verify.ps1`. Zusätzlich den Über-Dialog in beiden Designs und mit höherer Bildschirmskalierung ansehen, die Offline-Lizenz aufklappen und die beiden Links manuell öffnen. Die tatsächliche WPF-Darstellung und der Standardbrowseraufruf wurden hier nicht ausgeführt.

## Korrektur: Badge-Prüfung beim Themewechsel

Stand: 7. November 2026. Patchbasis: `c172d27` (`readme update, test: wip`).

Der gemeldete Windows-Lauf bricht beim ersten Dunkel-Wechsel mit `an existing success badge follows the current palette / Dark` ab. Die vorherigen Prüfungen für Auswahl, sofortiges Speichern und dynamische Textfarben bestehen. Der bisherige Test hält jedoch eine Badge-Instanz aus der Tabelle über den Themewechsel hinweg fest. WPF kann dabei Zellansichten neu aufbauen und alte Bindungen trennen; die zwischengespeicherte Instanz muss dann nicht mehr der angezeigten Zelle entsprechen. Diese Annahme ist im Test korrigiert; ob sie den gemeldeten Laufzeitfehler vollständig erklärt, ist hier ohne Windows nicht reproduzierbar.

Nach jedem Wechsel wird die Statusspalte der unveränderten Ergebniszeile in den sichtbaren Bereich gebracht und nach Layout/Dispatcher-Verarbeitung erneut abgefragt. Geprüft werden der aktuell sichtbare, geladene Badge im Hauptfenster, sein Zeilenbezug, Text, Erfolgston und die weiterhin unabhängig vorgegebene Palettenfarbe. Die Meldung enthält zusätzlich Soll-/Istfarbe, Ton und Fensterzugehörigkeit. Produktionscode und Farbpaletten bleiben unverändert.

- Release-Build des WPF-Testprogramms einschließlich App, Core und XAML mit .NET SDK 10.0.100 unter Linux: bestanden, 0 Warnungen / 0 Fehler.
- Statische Struktur- und Ressourcenprüfung: 1066 bestanden, 179 Schlüssel je Sprache.
- Unveränderte Prüfzahlen: Core zuletzt 472 unter Windows bzw. 183 unter Linux; Präsentation zuletzt 165. Diese beiden Testgruppen wurden für die reine UI-Testkorrektur nicht erneut ausgeführt. WPF/UI weiterhin 285 erwartet, keine Assertions hinzugefügt oder entfernt.
- Erneuter Windows-Lauf mit `.\Verify.ps1` erforderlich; WPF-Laufzeittests sind hier nicht ausführbar.

## Ergänzung: Theme-Umschaltung und Test-Harnesses

Stand: 6. November 2026. Patchbasis: `7fd5020` (`status label`). Ausführung mit .NET SDK 10.0.100 unter Linux.

| Prüfung | Ergebnis |
| --- | --- |
| Release-Build einschließlich WPF-XAML | Bestanden, 0 Warnungen / 0 Fehler |
| Core-Prüfungen | 183 bestanden; native Windows-Prüfungen übersprungen. Prüfumfang unverändert, zuletzt unter Windows dokumentiert: 472. |
| Präsentation und Einstellungen | 165 bestanden, davon 18 neue Theme-Prüfungen |
| Statische Struktur- und Ressourcenprüfung | 1066 Prüfungen bestanden; 179 Schlüssel je Sprache |
| Erweitertes WPF-Testprogramm | Kompiliert, 0 Warnungen / 0 Fehler; hier nicht ausgeführt. 285 Assertions erwartet, davon 30 neue Theme-Prüfungen. |

`Verify.ps1` bindet für Präsentationstests nur den WPF-unabhängigen Theme-Vertrag ein. Alle bestehenden Options-Tests erhalten einen injizierten Testdienst. Der WPF-Testhost verwendet wie `App.xaml` Hell einschließlich der passenden Palette. Der Strukturcheck unterstützt beide Paletten und kontrolliert gleiche Schlüssel, vollständige eigene dynamische Ressourcen und die Bindung des Design-Auswahlschalters. Die bisherigen Formatierungstests wurden an die im Basis-Commit entfernten Abschlusspunkte der Statusmeldungen angepasst.

Die neuen Präsentationsprüfungen decken beide gespeicherten Designs, alte und ungültige Einstellungen, sofortiges Speichern, unveränderte andere Einstellungsbereiche, wiederholte/ungültige Auswahl, Fehler beim Anwenden und Speichern, anschließende Erholung, drei Sprachen und einen echten JSON-Neustart ab. Dabei behoben: Der reine Designwechsel speichert nun sofort; fehlende oder ungültige Designwerte verwenden konsistent das aktuelle Anwendungsdesign.

Die acht neuen Prüfungen des WPF-Theme-Dienstes kontrollieren Initialisierung ohne Palette, Synchronisierung einer unpassenden Palette, Wechsel in beide Richtungen, Einfügen ohne vorhandene Palette, wiederholte Anwendung ohne neue Ressourcen, ungültige Werte, Dispatcher-Bindung und Erhalt anderer Ressourcen. Die Initialisierung und der Einfügepfad synchronisieren nun auch tatsächlich Palette und nativen `ThemeMode`.

Weitere 22 UI-Prüfungen bedienen die reale ComboBox in geöffneten Haupt- und Optionsfenstern: drei Wechsel, aktualisierte Text- und Badge-Farben, realisierte Listeneinträge, Spaltenreihenfolge und Virtualisierung, unveränderte Analyseergebnisse/Filter/Auswahl, lokalisierte Anzeige und zugänglicher Name, Wiederherstellung in einem neuen Optionsfenster sowie keine WPF-Bindungsfehler. Die erwartete Gesamtzahl von 285 ergibt sich aus dem bisherigen erwarteten Umfang von 255 plus diesen 30 Prüfungen; sie bezeichnet keinen hier ausgeführten Windows-Lauf.

Unter Windows noch ausführen: `.\Verify.ps1`. Danach Hell/Dunkel mehrfach mit geöffnetem Optionsfenster wechseln, die App schließen und erneut starten. Die zuletzt gewählte Darstellung muss erhalten bleiben, auch wenn vor dem Schließen keine andere Option geändert wurde. Eine visuelle Prüfung der nativen Fluent-Controls und Fensterdarstellung bleibt erforderlich.

## Ergänzung: Statustrenner und Diagnose-Testdatei

Stand: 6. November 2026. Patchbasis: `9271c44` (`wip: presentation test`), einschließlich Durchlaufstatistik und Theme-Umschalter.

Der Mittelpunkt ` · ` steht jetzt vor der Durchlaufstatistik und vor dem optionalen Diagnosehinweis. Die bestehenden UI-Prüfungen kontrollieren diese Trenner bei Abschluss, Abbruch und Sprachwechsel; es wurden keine Assertions hinzugefügt. Der dokumentierte Umfang bleibt unverändert: Core 472 im letzten dokumentierten Windows-Lauf beziehungsweise 183 unter Linux, Präsentation zuletzt 147 bestanden, UI 255 erwartet. Die README unterscheidet diese Zahlen vom aktuellen Ausführungsstand.

Der Release-Build der App besteht mit .NET SDK 10.0.100 unter Linux: 0 Warnungen / 0 Fehler. Der aktuelle UI-Test-Harness scheitert bereits an acht vorhandenen Aufrufen von `OptionsViewModel` ohne den neu benötigten `IThemeService`. Der Präsentations-Harness bindet nun `ThemeService.cs` ein, zielt jedoch weiter auf `net10.0` ohne WPF und kann dessen `Application`-Typ nicht auflösen. Diese offenen Anpassungen stammen aus der Theme-Umstellung der Patchbasis. Die WPF-Laufzeitprüfung bleibt unter Windows auszuführen, sobald die Harnesses wieder kompilieren.

Die separat mitgelieferte Datei `FileScope-Diagnose-NE-abgeschnitten.bin` enthält 66 Bytes: einen unvollständigen MZ-/NE-Header ohne ausführbaren Programmteil. Für die manuelle Prüfung **EXE-Header / Plattform** einschalten und die Datei in FileScope öffnen. Erwartete Diagnose: `Programm-Header: Truncated NE header.` Die Datei bleibt lesbar und hashbar; gezielt schlägt die optionale Headerauswertung fehl. Die Zeile kann daher weiterhin als abgeschlossene Analyse erscheinen, während Diagnosespalte und Statushinweis sichtbar werden.

Die Testdatei wurde mit dem echten `FileAnalysisService` geprüft: Dateigröße 66 Bytes, erwartete Headerdiagnose, korrekter SHA-256 und keine Headerdiagnose bei ausgeschalteter Headerprüfung. Die deutsche Diagnoseprojektion wurde ebenfalls vor der Theme-Umstellung geprüft. SHA-256: `8D9C309693716CF2FCDA524D1BB5D20F8828DCAF178D51A8B8C38089DCBA8622`. Diese separate Artefaktprüfung erhöht die drei Testgruppen nicht.

## Ergänzung: Durchlaufstatistik

Stand: 6. November 2026. Patchbasis: `dda03f7` (`readme`). Ausführung mit .NET SDK 10.0.100 unter Linux.

| Prüfung | Ergebnis |
| --- | --- |
| Release-Build der Projektmappe einschließlich WPF-XAML | Bestanden, 0 Warnungen / 0 Fehler |
| Core-Prüfungen | 183 bestanden; native Windows-Prüfungen übersprungen. Prüfumfang unverändert, letzter dokumentierter Windows-Lauf: 472. |
| Präsentation und Einstellungen | 147 bestanden, davon 8 neue Prüfungen für Durchlaufwerte und Formatierung |
| Statische Struktur- und Ressourcenprüfung | 1046 Prüfungen bestanden; 176 Schlüssel je Sprache |
| Erweitertes WPF-Testprogramm | Kompiliert, 0 Warnungen / 0 Fehler; hier nicht ausgeführt. 255 Assertions erwartet. |

Die Präsentationsprüfungen decken leere Dateien, unbekannte Größen, eine leere Ergebnisliste, unveränderliche Abschlusswerte, ganzzahlige Summen oberhalb von `Int64`, Laufzeiten über einen Tag und alle drei Sprachen ab. Zehn neue UI-Prüfungen prüfen Abschlussmeldung und Diagnosehinweis gemeinsam, ausschließlich neue Dateien bei einer weiteren Aufnahme, erneute Analyse trotz Filter, doppelte Pfade ohne neuen Durchlauf, Leeren sowie Abbruch nach einem abgeschlossenen und vor zwei weiteren Dateiergebnissen.

Die erwartete UI-Anzahl setzt sich aus dem zuletzt dokumentierten Windows-Lauf mit 223 Assertions, 22 Ergänzungen für die Diagnoseanzeige und 10 Ergänzungen für die Durchlaufstatistik zusammen. Sie ist keine Behauptung eines hier ausgeführten Windows-Laufs. Die README nennt für alle drei Gruppen Anzahl und Ausführungsstand.

Unter Windows noch ausführen: `.\Verify.ps1`. Anschließend einen gemischten Durchlauf mit lesbaren und nicht lesbaren Dateien, einen Abbruch und „Alle neu prüfen“ testen. Die Statusleiste muss Dateianzahl, Dauer, bekannte Bytesumme und gegebenenfalls unbekannte Größen sowie Diagnosedetails zeigen. Ein Sprachwechsel darf die Werte nur neu formatieren; „Leeren“ entfernt sie. Die Dauer umfasst die Analyse, nicht die vorgelagerte Suche und Aufnahme oder den Export. Auch bei kleiner Fensterbreite muss die Abschlussmeldung durch Zeilenumbruch lesbar bleiben.

## Ergänzung: Diagnoseanzeige und Entwurfsmuster

Stand: 6. November 2026. Patchbasis: `2ccf6ca` (`readme`).

| Prüfung | Ergebnis |
| --- | --- |
| Release-Build der Projektmappe einschließlich WPF-XAML | Bestanden, 0 Warnungen / 0 Fehler |
| Core-Prüfungen unter Linux | 183 bestanden; native Windows-Prüfungen übersprungen |
| Präsentation und Einstellungen | 139 bestanden, einschließlich neuer Standardposition, Erhalt gespeicherter Reihenfolgen und Zurücksetzen |
| Statische Struktur- und Ressourcenprüfung | 1039 Prüfungen bestanden; 174 Schlüssel je Sprache |
| Erweitertes WPF-Testprogramm | Kompiliert, 0 Warnungen / 0 Fehler; hier nicht ausgeführt |

Die neuen UI-Prüfungen decken leere und saubere Ergebnisse, optionale Metadatendiagnosen, Dateifehler, Signaturwechsel, Filter, Sprachwechsel, erneute Prüfung, manuelle Spaltenpositionen und Leeren ab. Der Statustext wird einschließlich Diagnoseanzahl am echten TextBlock geprüft; längere Meldungen dürfen umbrechen. Ein bisheriger Präsentationstest setzte fälschlich voraus, dass die letzte Standardspalte immer `Diagnostic` sei; er prüft nun die tatsächlich verschobene Spalte. Die bisherigen Sichtbarkeitstests wurden an die automatische Diagnoseanzeige angepasst.

Unter Windows noch ausführen: `.\Verify.ps1`. Anschließend eine Datei mit Diagnosedetails und eine ohne auswerten, die betroffene Zeile ausfiltern sowie „Alle neu prüfen“ und „Leeren“ verwenden. Auch bei minimaler Fensterbreite muss der Statushinweis lesbar bleiben. Eine gespeicherte Spaltenreihenfolge wird bewusst beibehalten; „Reihenfolge zurücksetzen“ muss Diagnose direkt hinter Analyse einordnen. WPF-Laufzeit und visuelle Darstellung konnten hier nicht ausgeführt werden.

## Ergänzung: Ordner per Drag-and-drop

Stand: 6. November 2026. Patchbasis: `080a4ac` (`documentation`).

| Prüfung | Ergebnis |
| --- | --- |
| Release-Build der Projektmappe mit .NET SDK 10.0.100 unter Linux, einschließlich WPF-XAML | Bestanden, 0 Warnungen / 0 Fehler |
| Core-Prüfungen | 183 bestanden, darunter Verzeichnislink zurück zum Startordner, ausdrücklich ausgewählter Link als Startordner, Datei-Symlinks mit gültigem/fehlendem Ziel und zwei Hardlink-Pfade derselben Datei |
| Präsentation und Einstellungen | 136 Prüfungen bestanden |
| Statische Struktur- und Ressourcenprüfung | 1035 Prüfungen bestanden; 173 Schlüssel je Sprache |
| Erweitertes WPF-Testprogramm | Kompiliert, 0 Warnungen / 0 Fehler; hier nicht ausgeführt |

Die neuen WPF-Prüfungen verwenden einen injizierten Bestätigungsdienst: Zusage/Ablehnung, keine Rückfrage bei reinen Datei-Drops, gemischte Drops, normalisierte und überlappende Ordnerpfade, leere Ordner, ein nach der Rückfrage verschwundener Startordner, Abbruch mit verspätetem Scan-Ergebnis sowie alle drei Dialogsprachen. Die produktive MessageBox bleibt außerhalb des ViewModels. Link-Testdateien werden nur in einem temporären Testordner erzeugt und anschließend entfernt; nicht unterstützte oder nicht erlaubte Link-Erstellung wird ausdrücklich als übersprungen gemeldet.

Unter Windows noch ausführen: `.\Verify.ps1` und einen echten Explorer-Drop auf freie Fläche, Tabelle und Suchfeld prüfen. Bei Ordnern erscheint genau eine MessageBox mit Pfadvorschau; „Nein“ verwirft den gesamten Drop, „Ja“ startet die rekursive Auswertung. Dabei auch gemischte Datei-/Ordnerauswahl, Junctions sowie Abbruch während der Suche prüfen. Der native Windows-Dialog, Explorer-Drag-and-drop und Windows-spezifische Link-Erstellung konnten in dieser Linux-Umgebung nicht ausgeführt werden.

## Voriger Prüfstand

Stand: 5. November 2026. Patchbasis: `5dd59b8ca7bf1524ee9423945bbfdfd549687d80` (`csv export, open folder`).

Aktuelle Erweiterung: Filter „Alles außer gültigen Signaturen“, doppelte Scrollbalkendicke und gespeicherte Auswahl des CSV-Trennzeichens (Semikolon als Standard, Komma wählbar). Core (139) und Präsentation/Einstellungen (127) wurden ausgeführt; das erweiterte WPF-Testprogramm wurde kompiliert. Der [vorige Review der Analyzer-/Dark-Commits](docs/REVIEW-2026-11-04.md) bleibt erhalten. Windows-Laufzeittests stehen weiterhin aus.

## Hier tatsächlich ausgeführt

| Prüfung | Ergebnis |
| --- | --- |
| Statische Prüfung: XML, Ressourcen, Binding-Wurzeln, Projektstruktur, Spalten, Workspace-Tasks und Test-Theme | 1014 Prüfungen bestanden |
| Drei Ressourcensätze mit jeweils 168 Schlüsseln; gleiche Formatplatzhalter | Bestanden |
| Genau zwei Produktionsprojekte; Kernbibliothek ohne WPF-Abhängigkeit | Bestanden |
| Alle 39 Spalten in XAML und Optionskatalog; begrenzte automatische Breiten | Bestanden |
| Workspace-JSON: drei eindeutige PowerShell-Tasks, existierende Skripte, Workspace-Pfade und win-x64-Ziel | Bestanden, statisch |
| Release-Build beider Projekte mit .NET SDK 10.0.100 unter Linux, einschließlich WPF-XAML | Bestanden, 0 Warnungen / 0 Fehler |
| `CoreChecks.cs` mit .NET 10 unter Linux | 139 Prüfungen bestanden; native Windows-Aufrufe übersprungen |
| `PresentationChecks.cs` mit .NET 10 unter Linux | 127 Prüfungen bestanden |
| Native Strukturen einschließlich SHFILEINFOW: Marshal-Größen und vorhandene kritische Offsets | x64 zur Laufzeit geprüft; x86/ARM64 nicht ausgeführt |
| WPF-Testprogramm mit `UiChecks.cs` kompiliert | Bestanden, 0 Warnungen / 0 Fehler; nicht ausgeführt |
| Self-contained Publish für win-x64, Trimming aus | Beim vorherigen Metadaten-Patch geprüft; für diese Erweiterung nicht erneut ausgeführt |

Der PE-Prüfer wurde mit eigens kodierten PE32-/PE32+-Dateien für i386, Alpha/Alpha64, ARM/ARMNT, AMD64, ARM64/ARM64EC/ARM64X und einen unbekannten Maschinentyp ausgeführt. Weitere Fälle: Dateien ohne PE-Header, DOS/NE, beschädigte PE-Header, AnyCPU, 32-Bit-Pflicht/-Präferenz, DLL-Flags, gemischter/nativer CLR-Code, Erhalt der Streamposition und begrenztes Lesen. Eine echte gebaute .NET-DLL wurde auf PE-/CLR-Header und Versionsinformationen geprüft. Metadatenfehler und Abbruch wurden im vollständigen Analyseablauf geprüft.

`python tools/check_structure.py` reproduziert die statischen Prüfungen, führt aber keine C#-Implementierung aus. Die C#-Prüfungen wurden über temporäre Harness-Projekte entsprechend `Verify.ps1` gebaut und gestartet. Die lokalen Builds verwendeten `-m:1 -p:UseSharedCompilation=false`, um die Build-Prozesse dieser Umgebung zu isolieren.

Die neuen ausführbaren Prüfungen umfassen Unterordner, Unicode- und versteckte Dateien, leere/fehlende Ordner, Abbruch und einen symbolischen Verzeichnislink zurück zum übergeordneten Ordner. CSV-Tests prüfen UTF-8-BOM, Kommas, Anführungszeichen, Zeilenumbrüche, unveränderte Hashwerte, leere Felder, ungültige Zeilenbreiten, vollständiges Ersetzen sowie Erhalt einer bestehenden Zieldatei bei Fehler und Abbruch. Alle 39 Spalten werden projiziert; Exportauswahl, Reihenfolge, Sprache, Hashformat, Architektur und ausgewählte Signatur werden geprüft. Auch leere Exportauswahl, JSON-Wiederherstellung und unbekannte/doppelte gespeicherte Feld-IDs sind abgedeckt.

Die zusätzlichen WPF-Prüfungen für Ribbon-Bindungen, Options-Häkchen, rekursives Hinzufügen mit Duplikaterkennung, Export trotz aktivem Filter, Schutz geladener Quelldateien, Dialogabbruch, Fehlerbehandlung und Cancel/Clear während Suche/Export/Hinzufügen wurden nur kompiliert.

Für das neue Trennzeichen werden je 1500 Zeilen mit Semikolon und Komma geschrieben und mit einem unabhängigen BCL-Leser (`TextFieldParser`) wieder eingelesen. Die Daten enthalten beide Trennzeichen, eingebettete Anführungszeichen und CRLF, Unicode, Rand-Leerzeichen und führende Nullen. Zusätzlich geprüft: Standard für alte Einstellungen, unbekannte Trennzeichen, Erhalt einer bestehenden Datei bei ungültiger Wahl sowie Beibehalten der Auswahl bei Sprachwechsel und anderen Einstellungen. Dies ist kein Test mit Excel.

Neue WPF-Prüfungen (nur kompiliert): Filter über alle Signaturstatus, Kombination mit Suche, sofortige Aktualisierung beim Signaturwechsel, unveränderte Bedeutung von „Prüfbedarf“, echte Trennzeichen-ComboBox, gerenderte Scrollbalkendicke, Maus-Hit-Test des vergrößerten Schiebers und Scrollbefehle in beiden Richtungen. Die Scrollbalken übernehmen `DefaultScrollBarStyle` des .NET-10-Fluent-Themes; dessen Templates, Scrollbereich und Farben bleiben erhalten.

## Noch nicht ausgeführt

Die Umgebung ist Linux. **Die PowerShell-/VS-Code-Tasks selbst, WPF-Laufzeittests, Ordner-/Speicherdialoge, Scrollbalken mit echter Maus, Excel-Import, die visuelle Kontrolle sowie die nativen Windows-Dateityp- und Signaturaufrufe wurden hier nicht ausgeführt.** Der frühere Publish wurde auf Windows nicht gestartet; für diese Erweiterung wurde kein neuer Publish erstellt. Ausgeliefert wird der Quellpatch; eine auf Windows getestete Binärausgabe liegt nicht bei.

Der erfolgreiche Build ersetzt keine Abnahme der Darstellung und Bedienung unter Windows. Den Task `FileScope: Verify` dort einmal ausführen; die abschließende Sichtprüfung bleibt manuell.

## Mitgelieferte ausführbare Prüfungen

Nach Installation des .NET 10 SDK auf Windows:

```powershell
.\Verify.ps1
```

Das Skript baut zuerst beide Produktionsprojekte. Anschließend kompiliert und startet es temporäre Testprogramme mit den Dateien aus `tests`:

| Testdatei | Abgedeckte Fälle |
| --- | --- |
| `CoreChecks.cs` | Fünf bekannte Hashwerte, leere Datei, ein gemeinsamer Lesedurchlauf, nicht-seekbarer Stream, mehrere Puffer, Abbruch vor/während des Lesens, Ressourcenfreigabe bei Fehlern, unveränderliche Optionen, Unicode-Pfade, Dateigröße/-datum, Metadatenmodus, fehlende Datei, Ordner, Dateisperre unter Windows, native ABI-Größen und unsignierte PE-Datei. ASN.1-Metadaten für klassische und RFC-3161-Zeitstempel; beschädigte, leere und mehrdeutige Daten; Trennung von ausgelesenem Datum und bestätigtem Zeitstempel; niemals aktuelle Prüfzeit als Ersatz. PE32/PE32+, verschiedene Maschinen und CLR-Flags, native Dateien und DLLs, echte Versionsdaten, fehlende Metadaten, beschädigte Header, begrenzte Lesezugriffe, Streambesitz, unabhängige Prüfoptionen, Teilergebnisse bei Metadatenfehlern und Abbruch. |
| `PresentationChecks.cs` | Bekannte Hex-/Base64-/Base64url-Vektoren inklusive führender Nullbytes und Padding; alle Digest-Längen; unveränderte Analysewerte und nicht-Hash-Felder; Details und Suche; Formatwechsel während/nach einer Analyse; Spaltenpermutationen und Grenzen; drei Sprachen; Speichern/Laden; fehlende, doppelte, unbekannte oder ungültige Einstellungen; beschädigte JSON-Datei und Schreibfehler. Mehrere Signaturen mit eigenen Zertifikaten/Status/Zeitstempeln; dasselbe Zertifikat in mehreren Signaturen; fehlende Metadaten ohne Übernahme aus einer anderen Signatur; Fenstermaße, Monitorgrenzen und Erhalt aller übrigen Einstellungen beim Speichern. Alle neun Prüf-Häkchen über JSON speichern/laden; alle ausgeschaltet; fehlende einzelne Felder und ältere Einstellungen ohne Prüfauswahl. Neue Metadaten in Suche/Details, lokalisiertes AnyCPU, Diagnose, Reset und unveränderte Metadaten bei Hashformatwechsel. |
| `UiChecks.cs` | Reales XAML und Optionsfenster initialisieren, Sprachbindungen, Dateiaufnahme und Deduplizierung, Filter, Clear/Cancel und verspätete Fortschrittsmeldungen. Gespeicherte Prüf-Häkchen im Haupt-ViewModel wiederherstellen, keine Schreibvorgänge beim Laden, direktes Speichern bei Änderung, Spaltensichtbarkeit und Verwendung der Auswahl in der Analyse; Ausgrauen der Online-Sperrprüfung sowie Leeren erhalten die Auswahl. Spalten-Häkchen, vollständige Reihenfolge einschließlich ausgeblendeter Spalten über die Fixierungsgrenze, Formatwechsel über die ComboBox ohne Analyseaufruf, Filteraktualisierung, unveränderte Resultate sowie automatische Breiten innerhalb der Grenzen. Echte Signatur-ComboBox und bedingte Spaltensichtbarkeit; Suche/Statusfilter nach Auswahlwechsel; Reset und Leeren; Fenstermaße und Zustand über Schließen/Neustart einschließlich minimiertem Schließen. Scrollkorrektur am tatsächlichen Fluent-Knopf; gemeldete negative Bruchwerte und Sonderwerte durch eine echte Width-Bindung; fraktionale horizontale Scrollpositionen; aktive Binding-Fehlerprotokollierung sowie Erhalt von Vorlage, Virtualisierung, fixierten Spalten und automatischen Breiten. Tatsächliche Metadaten-Checkbox-Bindings, unabhängige Gruppen-Sichtbarkeit und Weitergabe der Auswahl bei Drop/Neuanalyse. |

Die automatisierten UI-Tests laufen auf einem STA-Thread mit WPF-Dispatcher und echten ViewModels. Die meisten Prüfungen initialisieren die View ohne ein sichtbares Fenster. Die Fensterzustands- und Scrollprüfungen öffnen kurz Testfenster ohne Aktivierung und ohne Taskleisten-Eintrag, um reale RestoreBounds und Zustandwechsel zu prüfen. Sie ersetzen weder einen physischen Drag-and-drop-Test aus Explorer noch eine visuelle Prüfung.

Mit `-SignedFile 'C:\Pfad\zur\signierten-datei.exe'` kann zusätzlich eine bekannte eingebettet signierte Datei geprüft werden. Dabei werden die gefundenen Signaturindizes, Statuswerte, Hashalgorithmen und Zeitstempelinformationen ausgegeben. Die erwartete Anzahl und die tatsächlichen Datumswerte müssen mit der bekannten Testdatei verglichen werden. Dieser Test verwendet standardmäßig den lokalen Cache und setzt keine pauschale Vertrauenswürdigkeit voraus.

## Sinnvolle manuelle Abnahme unter Windows

1. App starten und Dateien aus Explorer auf freien Fensterbereich, Suchfeld und DataGrid ziehen. Neue Zeilen müssen jeweils erscheinen; während eines laufenden Durchlaufs wird eine weitere Ablage abgelehnt.
2. Alle Hashoptionen auswählen und eine Datei mit exakt den drei ASCII-Bytes `abc` prüfen. Erwartetes SHA-256: `BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD`.
3. Eine große Datei bearbeiten, abbrechen und erneut analysieren. Dann während eines Durchlaufs „Leeren“ drücken: nach Abschluss des Abbruchs müssen alle Zeilen verschwunden bleiben.
4. Mehrere Zeilen laden, nach Name/Größe/Datum sortieren und Filter kombinieren. Alle nicht ausgewählten Zeilen sollen denselben hellen Hintergrund haben; Auswahl und Fokus müssen erkennbar bleiben.
5. Englisch, Deutsch und 简体中文 durchschalten, auch mit vorhandenen Ergebnissen. Überschriften, Filter und Status sollen sich ändern; Hashwerte und originale Zertifikatsangaben bleiben gleich.
6. Eine bekannte eingebettet signierte Datei prüfen und Unterzeichner/Fingerabdruck mit den Windows-Dateieigenschaften vergleichen. Offline kann „Sperrstatus unbekannt“ korrekt sein. Mit Online-Sperrprüfung erneut prüfen.
7. Eine Kopie einer bekannten signierten Datei in einem für die Signatur relevanten Datenbereich verändern und prüfen, dass kein gültiges Ergebnis erscheint. Ein gültiger Authenticode-Zeitstempel darf dagegen ein inzwischen abgelaufenes Unterzeichnerzertifikat weiterhin legitimieren.
8. Darstellung bei 100 %, 150 % und 200 % Skalierung sowie allen drei Sprachen kontrollieren; lange Hashwerte und Pfade über horizontales Scrollen, Tooltip oder Detailfeld lesen/kopieren.
9. Ohne Dateien sowie mit vorhandenen Ergebnissen jede Hashoption und die Signaturprüfung ab-/anwählen. Die zugehörigen Spalten müssen sofort verschwinden/erscheinen. Name, Analysestatus, Größe, Datum, Pfad und Diagnose bleiben sichtbar. Bereits ermittelte Werte bleiben beim Ausblenden erhalten; neue Berechnungen über „Alle neu prüfen“ starten.
10. Zahnrad → Optionen öffnen. Sichtbare und ausgeblendete Spalten ganz nach oben/unten und schrittweise verschieben. Schließen, Spalten wieder einblenden und die gewählte Position prüfen. Auch die ersten zwei fixierten Positionen lassen sich im Optionsfenster frei belegen. Danach eine Tabellenüberschrift mit der Maus verschieben und die synchronisierte Optionsliste kontrollieren.
11. Hex groß/klein, Hex mit Doppelpunkten, Base64 und Base64url durchschalten, auch bei laufender Analyse und aktiver Suche. Bestehende Werte, Details und Kopierausgabe müssen sofort wechseln, ohne neuen Lesedurchlauf. Seriennummer und Windows-Statuscode bleiben unverändert. Sprache umschalten und die Übersetzungen im Optionsdialog prüfen.
12. Kurze und lange Dateinamen/Pfade, alle Hash-Längen und alle Formate prüfen. Spalten müssen innerhalb ihrer Mindest-/Höchstbreiten wachsen. „Breiten neu anpassen“, Sprach-/Formatwechsel und Leeren müssen auch Verkleinern erlauben. Lange Werte über Tooltip und Details vollständig lesen. App schließen/neu öffnen: Reihenfolge, Sprache und Hashformat müssen erhalten bleiben.

13. Eine Datei mit mehreren eingebetteten Signaturen laden, etwa SHA-1 und SHA-256. Beide Einträge mit den Windows-Dateieigenschaften vergleichen. Zwischen ihnen umschalten: Status, Unterzeichner, Fingerabdrücke, Zertifikatsgültigkeit und Zeitstempel müssen gemeinsam wechseln. Datei-Hashes bleiben gleich. Auch fehlgeschlagene Zusatzsignaturen müssen sichtbar sein.
14. Je eine Datei mit klassischem Zeitstempel, mit RFC-3161-Zeitstempel und ohne Zeitstempel prüfen; Datum mit einer unabhängigen Anzeige vergleichen. Aktuelle Prüfzeit oder „Gültig ab“ dürfen niemals als Ersatz erscheinen. Offline nicht bestätigte Zeitstempel behalten die entsprechende Kennzeichnung. Signaturprüfung ausschalten: Auswahl- und Zeitstempelspalten verschwinden. Mehrfach signierte Zeile nur ausfiltern: Auswahlspalte bleibt für die geladenen Daten vorhanden.
15. Fenster normal vergrößern/verkleinern und schließen/neu öffnen. Maximiert sowie aus Normal/Maximiert jeweils minimiert schließen/neu öffnen. Normale Maße und letzter nicht minimierter Zustand müssen erhalten bleiben. Auf einem Nebenmonitor schließen und nach Wechsel zu einem kleineren Primärmonitor starten: zentriert bzw. maximiert auf dem Primärmonitor, erreichbar innerhalb der Arbeitsfläche. Sprache, Hashformat und Spaltenreihenfolge bleiben erhalten.

16. Viele Spalten einblenden und wiederholt horizontal von links nach rechts und zurück scrollen, auch nach Größenänderung und Verschieben/Ausblenden von Spalten. Bei 100 %, 125 %, 150 % und 175 % Skalierung in Visual Studio die Binding-Fehleransicht prüfen: keine negativen Breitenfehler für `CellsPanelHorizontalOffset` mehr. Die Tabelle behält ihre Darstellung und die ersten beiden fixierten Spalten.

17. Eine gemischte Prüfauswahl setzen (einschließlich ausgeschaltetem SHA-256), Signaturprüfung und Online-Sperrprüfung ändern und die App neu starten. Alle neun Häkchen und die zugehörige Spaltensichtbarkeit müssen wiederhergestellt werden. Dasselbe mit allen Häkchen aus sowie mit gewählter, aber ausgegrauter Online-Sperrprüfung testen. Anschließend Sprache, Hashformat, Spaltenreihenfolge und Fenstergröße ändern und erneut starten: Alle Bereiche müssen ihre Einstellungen behalten. „Leeren“ darf die Prüfauswahl nicht zurücksetzen.

18. Verarbeitungs- und Signaturstatus müssen dieselbe abgerundete Form, Abstände und Schrift verwenden. Gültige Signaturen erscheinen grün; fehlendes Vertrauen, unbekannter Sperrstatus und nicht unterstützte Prüfung gelb; ungültige, abgelaufene, gesperrte und fehlgeschlagene Prüfungen rot; ungeprüfte/unsignierte Dateien grau. Bei mehreren Signaturen wechseln Text und Farbe gemeinsam mit der Auswahl. Alle drei Sprachen, lange Beschriftungen mit Tooltip, Sortieren, Kopieren per Ctrl+C, Spaltenausblendung sowie Scrollen mit wiederverwendeten Zeilen kontrollieren.

19. `FileScope.code-workspace` öffnen. Build über Ctrl+Shift+B starten; Publish und Verify über „Terminal → Task ausführen“. Einmal aus einem Pfad mit Leerzeichen prüfen. Publish muss unter `artifacts/publish/win-x64` einschließlich Sprachordnern landen.
20. Beide neuen Häkchen zunächst einzeln, dann gemeinsam aktivieren. EXE/DLL mit Versionsressourcen, eine .NET-AnyCPU-DLL, eine native Datei sowie eine Textdatei ablegen. Versionsfelder mit den Windows-Dateieigenschaften vergleichen; Textdateien müssen leere PE-Felder behalten. Eine PE-Datei in `.bin` umbenennen und erneut prüfen: PE-Angaben bleiben gleich, die Windows-Dateitypbezeichnung darf sich ändern.
21. Beide Metadaten-Häkchen setzen, App neu starten und die Wiederherstellung prüfen. Ausschalten blendet jeweils die zugehörigen Spalten aus. Für bestehende Zeilen nach dem Einschalten „Alle neu prüfen“ verwenden. Neue Spalten unter Optionen umordnen, nach Metadaten suchen und Werte kopieren; Sprach- und Hashformatwechsel dürfen die Originalwerte nicht verändern.

Für unternehmenseigene Vertrauensrichtlinien, besondere Signaturformate und sehr große Dateimengen sind zusätzliche reale Testdateien sinnvoll.

22. „Ordner hinzufügen“ mit Unterordnern, versteckten Dateien und bereits geladenen Dateien prüfen. Duplikate werden übersprungen. Einen leeren Ordner wählen sowie den Dialog abbrechen. Bei nicht lesbaren Unterordnern und Verzeichnisverknüpfungen muss ein Hinweis erscheinen. Suche abbrechen; während des Hinzufügens oder Analysierens auch „Leeren“ prüfen.
23. Unter Optionen → CSV exportieren einige Felder wählen, einschließlich eines ausgeblendeten Architekturfelds. Reihenfolge und Hashformat ändern, Dateien ausfiltern und exportieren: Alle geladenen Zeilen müssen mit den gewählten Feldern vorhanden sein. Bei mehreren Signaturen folgt der Export der Auswahl in der Zeile. Alle Felder abwählen: Exportknopf deaktiviert. App neu starten: Auswahl erhalten.
24. CSV mit Kommas, Anführungszeichen, Zeilenumbrüchen, Umlauten und chinesischen Zeichen importieren. Vorhandene CSV überschreiben sowie den Speicherdialog abbrechen. Ein schreibgeschütztes Ziel ausprobieren: Fehlermeldung, vorhandene Datei unverändert, Ergebnisse weiter verfügbar. Einen geladenen Dateipfad als Ziel eingeben: Export muss abgelehnt werden. Einen größeren Export mit „Abbrechen“ beenden.
25. „Alles außer gültigen Signaturen“ mit gültigen, unsignierten, ungeprüften, nicht unterstützten und fehlgeschlagenen Signaturen prüfen. Nur gültige ausblenden; bei mehreren Signaturen zwischen gültig und nicht gültig umschalten. Suche und Analysestatusfilter müssen weiterhin gemeinsam wirken.
26. Horizontale und vertikale Scrollbalken in Tabelle, Optionen und Detailfeld mit der Maus ziehen, auf die Spur sowie Pfeile klicken und das Mausrad verwenden. Darstellung bei 100 %, 150 % und 200 % Skalierung kontrollieren. Die Dicke muss verdoppelt sein; Scrollweg, fixierte Spalten und Inhalte dürfen nicht skaliert werden.
27. Unter Optionen → CSV exportieren zunächst Semikolon, dann Komma wählen und jeweils exportieren. In Excel über Daten → Aus Text/CSV mit demselben Trennzeichen importieren; Hash-/Seriennummer-/Versionsspalten als Text behandeln. App neu starten und Sprache/Feldauswahl/Fenstergröße ändern: Das gewählte Trennzeichen bleibt erhalten. Direktes Öffnen zusätzlich mit den eigenen Excel-/Regionaleinstellungen prüfen.
