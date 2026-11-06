# FileScope

WPF-Dateianalyse für Windows mit .NET 10: Dateien auf das Fenster ziehen oder ganze Ordner hinzufügen, Prüfsummen und eingebettete Authenticode-Signaturen prüfen, Versionsinformationen und PE-Plattform lesen, Ergebnisse filtern, kopieren und als CSV speichern.

Die Projektmappe enthält **genau zwei Projekte**. Der vollständige Quelltext liegt bei. Es werden keine Drittanbieter-NuGet-Pakete verwendet.

## Effiziente Dateianalyse – ein Lesedurchlauf für alle Prüfsummen

**FileScope ist darauf ausgelegt, vollständige Lesedurchläufe durch Dateien auf ein Minimum zu reduzieren.** Unabhängig davon, wie viele Prüfsummenalgorithmen aktiviert sind, liest FileScope den Dateiinhalt für die Hashberechnung nur **ein einziges Mal vollständig**.

- **Ein gemeinsamer Lesedurchlauf:** MD5, SHA-1, SHA-256, SHA-384 und SHA-512 werden gleichzeitig aus denselben eingelesenen Datenblöcken berechnet. Auch bei fünf aktivierten Algorithmen ist nur ein vollständiger Lesedurchlauf erforderlich.
- **Ein zentral verwalteter Datei-Handle:** Jede Datei wird für die Analyse einmal durch FileScope geöffnet. Der Stream bleibt während der Analyse geöffnet und wird für die Prüfsummenberechnung, die PE-/NE-Header-Analyse, die Windows-Versionsabfrage und die Windows-Signaturprüfung wiederverwendet. Für Versionsinformationen greift nur bei fehlender Handle-API die bisherige Pfad-Abfrage als Kompatibilitätslösung.
- **Streaming statt vollständigem Laden:** Die Hashberechnung verarbeitet die Datei blockweise mit einem gemeinsamen 128-KiB-Puffer. Dadurch bleibt der Speicherbedarf weitgehend unabhängig von der Dateigröße.
- **Gezielte zusätzliche Lesezugriffe:** Die optionale PE-/NE-Header-Analyse liest benötigte Headerbereiche erneut. `GetFileVersionInfoByHandle` erhält für Versionsinformationen denselben offenen Handle. Windows kann dabei zusätzliche Ressourcen und gegebenenfalls MUI-Sprachdateien lesen; Handle-Wiederverwendung bedeutet keine Garantie für nur einen Lesezugriff.
- **Native Signaturprüfung:** Für Authenticode erhält `WinVerifyTrust` den bereits geöffneten Datei-Handle. Windows kann für die kryptografische Prüfung zusätzliche Leseoperationen ausführen, gegebenenfalls auch über den gesamten Dateiinhalt.

**Wichtig:** Die Aussage über den einmaligen vollständigen Lesedurchlauf bezieht sich auf die von FileScope selbst implementierte Prüfsummenberechnung. Optionale Metadaten- und Signaturprüfungen können weitere Lesezugriffe verursachen. Insbesondere lässt sich nicht garantieren, dass Windows intern jede Datei nur einmal liest.

Durch die gemeinsame Verarbeitung eignet sich FileScope besonders für die Prüfsummenberechnung großer Dateien und umfangreicher Dateisammlungen.

## Starten

Voraussetzung: Windows und ein installiertes **.NET 10 SDK**. Für Visual Studio den Workload **.NET-Desktopentwicklung** sowie eine Version mit .NET-10-Unterstützung verwenden. Es wird ein workspace für Visual Studio Code mit vordefinierten Tasks mitgeliefert.

Visual Studio:

1. Klonen oder herunterladen, und das ZIP vollständig entpacken.
2. `FileScope.sln` öffnen und `FileScope.App` als Startprojekt festlegen.
3. Mit F5 starten.

Visual Studio Code:

1. `FileScope.code-workspace` öffnen
2. Strg + Umschalt + B startet den vordefinierten Build task (Release); es gibt noch weitere Tasks.
3. Der Ausgabeordner erscheint in der Konsole, dort die Exe ausführen.

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

## VS-Code-Workspace-Tasks

`FileScope.code-workspace` öffnen. Unter **Terminal → Task ausführen** stehen drei Aufgaben:

| Task | Skriptaufruf |
| --- | --- |
| `FileScope: Build (Release)` | `Build.ps1 -Configuration Release`; Standard-Build mit Ctrl+Shift+B |
| `FileScope: Publish (win-x64)` | `Build.ps1 -Configuration Release -Publish -Runtime win-x64` |
| `FileScope: Verify` | `Verify.ps1`; baut die Lösung und führt alle für Windows vorgesehenen Prüfungen aus |

Die Tasks starten `powershell.exe` ohne Profil und verwenden den benannten Workspace-Ordner als Arbeitsverzeichnis. Auch Pfade mit Leerzeichen funktionieren, da Argumente getrennt übergeben werden. Das .NET-10-SDK muss im `PATH` des VS-Code-Prozesses liegen. Publish erzeugt eine eigenständige Ausgabe unter `artifacts/publish/win-x64`.

## Bedienung

| Funktion | Verhalten |
| --- | --- |
| Dateien ablegen | Eine oder mehrere Dateien auf einen beliebigen Bereich des Fensters ziehen. Alternativ „Dateien öffnen“ / Ctrl+O. |
| Ordner hinzufügen | Links oben neben „Dateien öffnen“. Einen Ordner auswählen und alle darin enthaltenen Dateien einschließlich Unterordner analysieren. Bereits geladene Pfade werden übersprungen. |
| Exportieren | In derselben Dateigruppe. Alle geladenen Dateien über „Speichern unter“ als CSV oder JSON exportieren, einschließlich ausgefilterter Zeilen. Exportiert werden vorhandene Ergebnisse, ohne erneute Analyse. |
| Exportfelder | Unter Zahnrad → Optionen → Exportieren die gewünschten Felder anhaken. Die Auswahl wird gespeichert und ist unabhängig von der Sichtbarkeit der Tabellenspalten. |
| CSV-Trennzeichen | Im selben Reiter Semikolon oder Komma wählen. Standard ist Semikolon; die Auswahl wird gespeichert und bleibt beim Sprachwechsel erhalten. |
| Prüfoptionen | MD5, SHA-1, SHA-256, SHA-384 und SHA-512 separat auswählen. Beim ersten Start: SHA-256 und Signaturprüfung. Danach wird die gespeicherte Auswahl aller Prüf-Häkchen wiederhergestellt. |
| Sichtbare Spalten | Hashspalten folgen sofort ihren Häkchen. Die Signatur- und Zertifikatsspalten folgen gemeinsam der Signaturprüfung. Dateieigenschaften und PE-Spalten folgen ihren jeweiligen Häkchen. Ausblenden behält bestehende Ergebnisse; neu gewählte Werte lassen sich mit „Alle neu prüfen“ berechnen. |
| Verarbeitung | Ein Dateistream, ein gemeinsamer Lesedurchlauf für alle gewählten Hashes; Dateien werden nacheinander im Hintergrund bearbeitet. |
| Zertifikate | Von Windows gemeldete primäre und zusätzliche eingebettete Authenticode-Signaturen jeweils prüfen; Unterzeichner, Inhaber, Aussteller, Seriennummer, Gültigkeitsdaten und Fingerabdrücke der gewählten Signatur anzeigen. |
| Signaturauswahl | Sobald mindestens eine geladene Datei mehrere Signaturen hat, erscheint eine Auswahlspalte pro Zeile. Die Einträge nennen Signaturnummer, Unterzeichner und Dateisignatur-Hashalgorithmus. Auch dasselbe Zertifikat kann mehrfach vorkommen. Status, Zertifikatsdaten, Datum, Details, Suche und Sortierung folgen der Auswahl; keine erneute Analyse. Bei einzelnen Signaturen ist die Auswahl deaktiviert. |
| Signaturzeitstempel | Authenticode-Gegensignatur oder RFC-3161-Zeitstempel in UTC. Fehlende, nicht auslesbare und nicht bestätigte Zeitstempel sind ausdrücklich beschriftet. |
| Dateimetadaten | Zwei unabhängig gespeicherte Häkchen: „Dateieigenschaften“ und „PE-Header / Plattform“. Anfangs beide aus. Versionsfelder und Windows-Dateityp beziehungsweise PE-Maschinentyp, PE-Format und .NET-Plattform ermitteln. |
| Online-Sperrprüfung | Optional. Ohne Häkchen werden ausschließlich lokale/zwischengespeicherte Ketten- und Sperrinformationen verwendet. |
| Alle neu prüfen | Alle geladenen Dateien mit der aktuellen Auswahl erneut verarbeiten, einschließlich ausgefilterter Dateien. Vorherige Ergebnisse dieser Dateien werden dabei ersetzt. |
| Suche | Textsuche über Pfad, Größe, Datum, Hashwerte, Status, Zertifikatsangaben, Dateieigenschaften, PE-Angaben und Diagnose, ohne Groß-/Kleinschreibung. |
| Filter | Textsuche, Analysestatus und Signaturstatus werden mit UND verknüpft. |
| Alles außer gültigen Signaturen | Blendet ausschließlich den Status „gültig“ aus. Enthält auch unsignierte, ungeprüfte, nicht unterstützte und fehlgeschlagene Prüfungen. Bei mehreren Signaturen gilt die aktuelle Auswahl in der Zeile. „Prüfbedarf“ behält seine bisherige engere Bedeutung ohne unsignierte/ungeprüfte Dateien. |
| Sortierung | Spaltenüberschrift anklicken; bei Bedarf mit Shift weitere Sortierspalten hinzufügen. Größen und Datumsfelder werden nach ihren Werten sortiert. |
| Statusfelder | Verarbeitungs- und Signaturstatus verwenden dieselbe abgerundete `StatusBadge`-Komponente. Grün: fertig/gültig; Blau: Verarbeitung läuft; Gelb: abgebrochen oder Warnfall; Rot: Fehler/ungültige Prüfung; Grau: wartend, nicht geprüft oder keine eingebettete Signatur. Die übersetzte Beschriftung und der vollständige Tooltip bleiben maßgeblich. |
| Tabelle | Schreibgeschützt und horizontal scrollbar. Zeilen ohne wechselnde Hintergrundfarben mit den nativen WPF-Tabellenvorlagen. Die ersten zwei Spalten der gewählten Reihenfolge bleiben links stehen. |
| Scrollbalken | Beide Richtungen sind doppelt so dick, einschließlich Schieber und Maus-Trefffläche. Die nativen Fluent-Vorlagen werden nur quer zur Scrollrichtung skaliert: 24 statt 12 geräteunabhängige Pixel. Gilt auch für Listen, Detailfeld und Menüband. |
| Spaltenreihenfolge | Zahnrad → Optionen: Alle 39 Spalten per Auf/Ab, ganz nach oben/unten oder Zurücksetzen anordnen, auch ausgeblendete Spalten. Verschieben der Tabellenüberschriften synchronisiert diese Liste. |
| Spaltenbreiten | Automatisch nach Überschrift und sichtbarem Inhalt, innerhalb fester Mindest-/Höchstbreiten je Spalte. Lange Werte sind über Tooltip, Details und Kopieren vollständig verfügbar. Manuelles Vergrößern bleibt möglich; „Breiten neu anpassen“ schaltet wieder auf Automatik. |
| Hashdarstellung | Unter Optionen: Hex groß, Hex klein, Hex mit Doppelpunkten, Base64 oder Base64url ohne Padding. Datei-Hashes und Zertifikatsfingerabdrücke wechseln sofort, auch in Details, Suche und Kopierausgabe. Keine erneute Dateianalyse. |
| Kopieren | Zeilen auswählen und Ctrl+C verwenden. Unter „Ausgewählte Datei“ ist der komplette Detailtext markierbar. |
| Abbrechen | Esc oder „Abbrechen“. Fertige Ergebnisse bleiben erhalten, noch offene Zeilen werden als abgebrochen markiert. |
| Leeren / Clear | Ctrl+L oder „Leeren“. Entfernt alle Zeilen und Filter, sobald kein Vorgang mehr läuft. Während der Verarbeitung deaktiviert; bei Bedarf zuerst abbrechen und das Ende abwarten. |
| Sprachen | Im Optionsfenster: Englisch, Deutsch, vereinfachtes Chinesisch. Umschalten ohne Neustart, auch bei bestehenden Ergebnissen und laufender Verarbeitung. |
| Fenster | Normale Breite/Höhe und Maximierungszustand werden beim Schließen gespeichert. Ein minimiert geschlossenes Fenster merkt sich den letzten normalen/maximierten Zustand. Neustart zentriert auf dem Primärmonitor; zu große Maße werden auf dessen Arbeitsfläche begrenzt. |
| Design | Festes Fluent-Dark-Theme über `App.xaml`. Kein Theme-Umschalter und keine eigenen Vorlagen für Zeilen/Zellen. |

Während der Verarbeitung sind neue Dateiaufnahmen, Prüfoptionen und „Leeren“ gesperrt. Eine aktive native Windows-Signaturprüfung ist nicht mitten im Aufruf abbrechbar; „Abbrechen“ wartet auf deren Rückkehr. Danach ist „Leeren“ wieder verfügbar. Das Fenster bleibt bedienbar.

Mehrfach abgelegte **normalisierte Pfade** werden unter Windows ohne Beachtung der Groß-/Kleinschreibung dedupliziert. Unterschiedliche Pfade mit demselben Dateiinhalt bleiben separate Zeilen. Zum rekursiven Einlesen von Ordnern „Ordner hinzufügen“ verwenden; per Drag-and-drop abgelegte Ordner werden weiterhin als eigene Fehlerzeilen gemeldet. Sind sämtliche Prüf-Häkchen abgewählt, werden nur Pfad, Dateigröße und Änderungsdatum ermittelt.

Die gewählten Prüfoptionen gelten als unveränderlicher Schnappschuss für einen Durchlauf. Ein späterer Sprach- oder Hashformatwechsel verändert keine Analysewerte. Die fünf Hash-Häkchen, Signaturprüfung, Online-Sperrprüfung, Dateieigenschaften und PE-Header werden bei jeder Änderung zusammen mit Sprache, Hashformat, Spaltenreihenfolge, Fenstergröße und Maximierungszustand in `%LOCALAPPDATA%\FileScope\settings.json` gespeichert. Ergebnisse und manuell veränderte Breiten gelten nur für die laufende Sitzung. Ungültige Einstellungen fallen auf Standardwerte zurück; Schreibfehler werden im Optionsfenster angezeigt, ohne die Dateianalyse zu blockieren.

Auch eine vollständig abgewählte Prüfauswahl wird gespeichert. Die Online-Sperrprüfung behält ihren gewählten Wert, wenn sie durch abgeschaltete Signaturprüfung ausgegraut ist; sie wird erst bei aktivierter Signaturprüfung verwendet. „Leeren“ setzt die Prüf-Häkchen nicht zurück.

Im Code liegen die Bindungswerte in `MainViewModel`: `ChecksumOptions[*].IsSelected`, `InspectCertificates`, `OnlineRevocation`, `InspectFileProperties` und `InspectPeHeader`. Die Startwerte sind zentral in `AnalysisPreferences` in `Services/PreferencesStore.cs` definiert. Ältere Einstellungen ohne den Abschnitt `Analysis` verwenden die bisherigen Standards. Fehlende neue Metadaten-Häkchen sind ausgeschaltet; alle bisherigen Einstellungen bleiben erhalten. `OptionsViewModel` speichert alle Bereiche gemeinsam, damit eine Änderung der Prüf-Häkchen keine anderen Einstellungen überschreibt.

Neue Spalten werden bei einer bereits gespeicherten Reihenfolge hinten ergänzt und lassen sich unter Optionen einsortieren. Die Signaturauswahlspalte bleibt bei aktiver Signaturprüfung sichtbar, solange irgendeine geladene Datei mehrere Signaturen hat, auch wenn diese gerade ausgefiltert ist. Nach Leeren oder einer erneuten Analyse ohne Mehrfachsignaturen verschwindet sie.

Die Breitenautomatik misst den sichtbaren Inhalt und wächst bei Bedarf bis zur jeweiligen Obergrenze. Ein Sprach-/Formatwechsel, „Leeren“ oder „Breiten neu anpassen“ startet die Messung neu und erlaubt auch schmalere Spalten. Das Filtern allein setzt manuell gewählte Breiten nicht zurück.

Beim horizontalen Scrollen kann WPF für `CellsPanelHorizontalOffset` einen geringfügig negativen Wert berechnen. Die Fluent-Vorlage bindet diesen Wert direkt an die Breite ihres internen „Alles auswählen“-Knopfs, auch bei ausgeblendeten Zeilenüberschriften. `DataGridScrollFix` ergänzt beim Laden der Tabelle ausschließlich für diese Bindung eine Begrenzung auf null. Positive Breiten bleiben erhalten; die native Vorlage, fixierte Spalten, Virtualisierung und automatische Spaltenbreiten bleiben bestehen. Die Binding-Fehlerausgabe wird nicht abgeschaltet.

## Ordnersuche und Export

Die Ordnersuche berücksichtigt auch versteckte Dateien. Verzeichnisverknüpfungen (Junctions und symbolische Links) innerhalb des ausgewählten Ordners werden übersprungen, damit keine Schleifen entstehen. Nicht lesbare Unterordner werden gemeldet; lesbare Geschwisterordner werden weiter durchsucht. Bereits vorhandene Pfade werden nicht erneut analysiert. „Abbrechen“ beendet die Suche ohne neue Zeilen; beim späteren Hinzufügen oder Analysieren bleiben bereits hinzugefügte Zeilen erhalten und können mit „Alle neu prüfen“ verarbeitet werden. „Leeren“ ist erst nach Abschluss oder beendetem Abbruch verfügbar und entfernt dann alle Zeilen.

Der Exportdialog bietet CSV (`*.csv`, Standard) und JSON (`*.json`) als Dateitypen an. Ohne eingegebene Endung ergänzt der Dialog die Endung des gewählten Dateityps. Eine explizite Endung `.csv` oder `.json` bestimmt das Format, unabhängig von Groß-/Kleinschreibung. Andere Endungen werden mit einem Hinweis abgewiesen. Die Überschreibabfrage gilt für den tatsächlich zurückgegebenen Dateinamen; danach wird nichts umbenannt. Eine geladene Quelldatei darf nicht als Exportziel verwendet werden. Zunächst wird eine temporäre Datei im Zielordner geschrieben; erst nach erfolgreichem Abschluss ersetzt sie die Zieldatei. Abbruch und Schreibfehler erhalten eine schon vorhandene Exportdatei.

Alle **39 Felder** des Spaltenkatalogs sind auswählbar, auch PE-Maschinentyp, PE-Format und .NET-Plattform. Beim ersten Start sind alle Exportfelder gewählt. Die Auswahl wird als `ExportColumns` mit stabilen Spalten-IDs in der vorhandenen `settings.json` gespeichert; eine vollständig leere Auswahl bleibt leer und deaktiviert den Exportknopf. Reihenfolge, Sprache und Hashformat folgen den aktuellen Optionen. Pro Datei werden die Daten der jeweils ausgewählten Signatur exportiert. Es werden alle geladenen Zeilen in der Reihenfolge des Hinzufügens ausgegeben; Tabellenfilter und die Sortierung der Ansicht ändern diese Auswahl nicht. Fehlende Messergebnisse bleiben leer, Status und Diagnose bleiben erhalten.

Das CSV-Format verwendet **UTF-8 mit BOM**, standardmäßig **Semikolon** als Trennzeichen und CRLF als Zeilenenden. Unter Optionen → Exportieren lässt sich auch Komma wählen. `CsvDelimiter` wird in `settings.json` gespeichert; ältere Einstellungen ohne dieses Feld verwenden Semikolon. Die Wahl wird vor dem Export zusammen mit den Daten festgehalten, sodass ein späterer Optionswechsel eine laufende Ausgabe nicht verändert. Alle Felder stehen in doppelten Anführungszeichen; enthaltene Anführungszeichen werden verdoppelt. Zeilenumbrüche, Umlaute, chinesische Zeichen und Hashwerte bleiben erhalten.

Excel berücksichtigt beim direkten Öffnen einer CSV regionale Einstellungen. Wenn die Spalten nicht richtig getrennt werden, über **Daten → Aus Text/CSV** importieren und das beim Export gewählte Trennzeichen einstellen. Für Hashes, Seriennummern sowie Versionsfelder den Datentyp Text wählen, damit Excel führende Nullen oder Versionsnummern nicht automatisch umformatiert. Siehe [Microsoft: Text- und CSV-Dateien importieren](https://support.microsoft.com/en-us/excel/get-started/import-or-export-text-txt-or-csv-files).

JSON wird als eingerücktes Array von Objekten in **UTF-8 ohne BOM** geschrieben, ein Objekt pro geladener Datei. Die Eigenschaftsnamen sind stabile Spalten-IDs (z. B. `Name`, `SHA256`, `PeMachine`), unabhängig von der gewählten Sprache. Auswahl und Reihenfolge der Felder entsprechen CSV. Alle Werte sind Zeichenketten mit denselben Anzeigewerten wie im CSV-Export; fehlende Werte sind `""`. Damit bleiben führende Nullen, Versionsnummern und das gewählte Hashformat erhalten. Der CSV-Trennzeichenwähler beeinflusst JSON nicht.

```json
[
  { "Name": "Beispiel.exe", "SHA256": "0000ABCDEF", "PeMachine": "AMD64 (x64) (0x8664)" }
]
```

Die Exportarchitektur verwendet **Strategy**: `ResultExporter` implementiert `IResultExporter`, wählt die registrierte `IResultExportStrategy` anhand des Formats und übernimmt temporäre Datei, atomaren Austausch, Abbruch und Aufräumen. `CsvExportStrategy` und `JsonExportStrategy` schreiben nur in den bereitgestellten Stream. Die Strategien werden in `App.OnStartup` zusammengestellt. `ResultExportTable` enthält stabile IDs, übersetzte Überschriften und Werte, ohne WPF-Abhängigkeit; `ResultExportProjection` erstellt die Momentaufnahme vor dem Hintergrundexport. Neue Exportfelder werden im zentralen Katalog `ColumnOption.CreateDefaults` ergänzt. Die Ordnersuche liegt getrennt in `IFolderScanner` / `FolderScanner`.

## Projekte und Entwurfsmuster

| Projekt / Komponente | Aufgabe |
| --- | --- |
| `FileScope.App` | WPF-Fenster, gruppiertes Menüband aus nativen Controls, DataGrid, ViewModels, Commands, Lokalisierung, Dateiauswahldialog und Drag-and-drop-Behavior. |
| `FileScope.Core` | Geschäftslogik: Modelle, Analysevertrag, Streaming-Prüfsummen, Strategiefabrik, Dateieigenschaften, PE-Header und Windows-Signaturprüfung. Keine WPF-Abhängigkeit. |
| `MainViewModel` | Koordiniert Benutzeraktionen, Status und gefilterte Ergebnisansicht. Berechnet selbst keine Hashwerte. |
| `FileAnalysisService` | Orchestriert Dateizugriff, Prüfsummen, optionale Metadatenprüfer und Signaturstrategie. Liefert typisierte Resultate ohne UI-Texte zurück. |
| `IChecksumStrategy` / Factory | Strategy und Factory für inkrementelle Hashberechnungen. Pro Datei werden neue, anschließend entsorgte Strategieinstanzen erzeugt. |
| `IFilePropertiesInspector` / `IPeHeaderInspector` | Getrennte, injizierte Strategien für VERSIONINFO/Dateityp und PE-/CLR-Header. Ergebnisse sind UI-unabhängige Core-Modelle. |
| `ISignatureInspector` | Austauschschnittstelle für Signaturprüfung; implementiert durch `WindowsAuthenticodeInspector`. |
| `App.xaml.cs` | Composition Root: Abhängigkeiten werden per Konstruktor injiziert. Kein Service Locator und kein DI-Container erforderlich. |
| `RelayCommand` / `AsyncRelayCommand` | Command Pattern; asynchrone Ausführung mit Fehlerbehandlung und Schutz vor Mehrfachstart. |
| `FileDropBehavior` | Übersetzt Drag-and-drop-Ereignisse in ein ViewModel-Command. Auch über Textfeldern und Tabelle wirksam. |
| `OptionsViewModel` / `OptionsWindow` | Lokalisierte Optionen, vollständige Spaltenreihenfolge und Command-basierte Verschiebung. |
| `ColumnLayoutBehavior` | Überträgt stabile Spalten-IDs auf die `DisplayIndex`-Werte und synchronisiert manuelles Verschieben zurück; koordiniert die begrenzte automatische Breitenmessung. |
| `StatusBadge` / `StatusToneConverter` | Wiederverwendbares UI-Element mit Text und Farbrolle. Eine zentrale Zuordnung übersetzt Verarbeitungs- und Signaturzustände in diese Rollen; Rundung, Abstände und Palette sind im gemeinsamen Control definiert. |
| `DigestFormatter` | Formatiert die vorhandenen Digest-Bytes; `FileRowViewModel` hält die dargestellten Werte im Cache. Die Core-Ergebnisse bleiben unverändert. |
| `IPreferencesStore` / `PreferencesStore` | Speichert kleine Benutzereinstellungen über eine temporäre Datei mit anschließendem Ersetzen der Zieldatei. |
| `BindingProxy` | Liefert den Fenster-DataContext an DataGrid-Spalten, die selbst keinen DataContext erben. Damit bleibt die Sichtbarkeit per Binding an die Prüfoptionen gekoppelt. |

MVVM trennt View, ViewModel und Domänenmodelle. Code-behind beschränkt sich auf Fensterlebenszyklus einschließlich Größenwiederherstellung, Öffnen des Optionsdialogs und Sichtbarhalten der ausgewählten Listenzeile. `INotifyPropertyChanged` sowie `ICollectionView` aktualisieren und filtern die Darstellung.

Das Menüband ist eine gruppierte native WPF-Befehlsleiste im Fluent-Stil, **kein zusätzlicher Ribbon-Control-Anbieter**. Der WPF-Tabellentyp heißt `DataGrid`; `DataGridView` gehört zu Windows Forms.

Die Kernbibliothek zielt auf `net10.0`; ihre Hash- und Orchestrierungslogik lässt sich auch ohne Windows verwenden. Die WPF-App zielt auf `net10.0-windows`. Die native Authenticode-Strategie meldet außerhalb von Windows ausdrücklich „UnsupportedPlatform“.

## Dateieigenschaften und Programm-Header

**Dateieigenschaften** liest die vorhandenen `VERSIONINFO`-Felder: Dateibeschreibung, Sprache, Datei- und Produktversion, Produktname, Hersteller, Originaldateiname, interner Name, Copyright, Marken und Kommentare. Fehlende Felder bleiben leer. „Dateityp“ kommt aus der Windows-Zuordnung der Dateiendung; es ist keine Inhaltsprüfung und kann von der tatsächlichen Datei abweichen. Ohne Windows wird die Endung angezeigt. Versionsfelder, Dateisprache und Windows-Typbezeichnung bleiben in ihrer Original-/Systemsprache; die Spaltenüberschriften wechseln mit der App-Sprache. Allgemeine Office-/PDF-Dokumenteigenschaften oder Bild-EXIF sind nicht Teil dieser Auswertung.

`IFilePropertiesInspector` erhält denselben `FileStream` wie die Prüfsummen- und Signaturprüfung. `WindowsFileVersionReader` kapselt `GetFileVersionInfoByHandle`, die Auswertung des zurückgegebenen Puffers und dessen Freigabe. Die Auswahl der Sprach-/Codepage-Tabelle und die MUI-Flags folgen der bisherigen .NET-Abfrage. Normale Dateien ohne Versionsressourcen bleiben ohne Metadatenfehler. Native Lesefehler erhalten eine Diagnose; sie lösen keine zweite Pfad-Abfrage aus. Auf anderen Betriebssystemen oder bei fehlendem Windows-Export bleibt `FileVersionInfo.GetVersionInfo(path)` als Kompatibilitätslösung erhalten.

Bei NE-Dateien kann Windows den nativen Dateizeiger verschieben, obwohl `FileStream.Position` unverändert bleibt. Beim Ausleihen über `SafeFileHandle` synchronisiert .NET zunächst den nativen Dateizeiger mit der verwalteten Position. Der Inspector sichert diesen synchronisierten Zustand und stellt beide Positionen nach der Versionsabfrage wieder her, auch beim Abbruch oder einem Lesefehler. Der geliehene Stream bleibt geöffnet.

`Verify.ps1` vergleicht unter Windows die neue Abfrage mit `FileVersionInfo`, einschließlich Sprache, PE-/NE-Ressourcen und fehlenden Versionsdaten. Die Prüfungen verwenden absichtlich einen nicht existierenden Dateinamen, um einen unbemerkten Rückgriff auf diesen Pfad auszuschließen, und kontrollieren beide Dateipositionen sowie den nächsten Lesezugriff. Eigene Dateien können zusätzlich angegeben werden:

```powershell
.\Verify.ps1 -VersionFile "D:\Testdateien\VBRUN300.DLL", "D:\Testdateien\DMSGEW.EXE"
```

**EXE-Header / Plattform** prüft DOS-/PE-/NE-Kennung und liest den COFF-Maschinentyp, beispielsweise `i386 (0x014C)`, `AMD64 (x64)`, `ARM`, `ARM64` oder `Alpha`. Unbekannte Werte bleiben als Hexcode sichtbar. Die Erkennung richtet sich nach dem Dateiinhalt, nicht nach `.exe` oder `.dll`. Dateien ohne unterstützten PE- oder NE-Header liefern leere Header-Felder; beschädigte erkannte Header erhalten eine Diagnose.

Die zusätzlichen Spalten zeigen `PE32`/`PE32+` und bei vorhandenen CLR-Headern die .NET-Plattform. Ein IL-only-i386-PE kann `AnyCPU`, `AnyCPU (32 Bit bevorzugt)` oder explizit `i386` sein. Native Dateien haben keine .NET-Plattform. Maschinentyp und CLR-Flags beschreiben die Header und sind keine Garantie für Lauffähigkeit auf einem bestimmten Windows-System. Es wird ausschließlich gelesen; keine Assembly wird geladen oder ausgeführt.

**Windows-16-Bit-Dateien** verwenden NE (New Executable), nicht „PE16“. EXE und DLL werden als `NE (Win16)` mit `x86 (16-bit)` erkannt. NE enthält keinen COFF-Maschinentyp; deshalb wird kein PE-Hexcode erfunden. OS/2-, DOS-4.x- und Windows/386-Ziele bleiben unterscheidbar; bei Windows/386 und unbekannten Zielwerten wird keine 16-Bit-Codebreite unterstellt. Ein bloßes `MZ` am Dateianfang reicht nicht zur Win16-Erkennung, da auch PE, LE und LX einen DOS-Stub besitzen. Reine DOS-MZ-, LE- und LX-Dateien liefern weiterhin keine PE-/NE-Metadaten; ihre Prüfsummen sind davon unabhängig.

Die NE-Erweiterung liest den festen Header und prüft die Grenzen von Segment- und Einsprungtabellen. Sie durchsucht weder Nutzdaten noch Ressourcen und übernimmt den vorhandenen Stream samt Positionswiederherstellung. `NeHeaderReader` kapselt das NE-Layout getrennt vom vorhandenen PE-/CLR-Parser. Die internen Namen `InspectPeHeader`, `PeMachine` und `PeFormat` bleiben für gespeicherte Einstellungen und Exporte bestehen. Die sichtbaren Bezeichnungen sind nun „EXE-Header / Plattform“, „Architektur“ und „Programmformat“. CSV und JSON enthalten die erkannten NE-Werte automatisch. VERSIONINFO wird getrennt durch die Windows-API ausgewertet; ein eigener Parser für Win16-Versionsressourcen ist nicht erforderlich.

Die Werte erscheinen auch in Details, Kopierausgabe und Textsuche. Neu aktivierte Prüfungen gelten für den nächsten Durchlauf; für vorhandene Zeilen „Alle neu prüfen“ verwenden. Ein erwarteter Lesefehler einer optionalen Metadatenprüfung wird in der Diagnosespalte ergänzt; bereits berechnete Hashes und die übrigen Prüfungen bleiben erhalten. Die Verarbeitungsanzeige kann deshalb „Fertig“ zeigen, während eine optionale Auswertung eine Diagnose enthält.

## Zertifikatsprüfung: genaue Bedeutung

Verwendet wird `WinVerifyTrust` mit der Windows-Authenticode-Richtlinie. Dadurch werden Dateiintegrität und Vertrauenskette durch Windows beurteilt; ein auslesbares Zertifikat allein wird nie als gültige Signatur gewertet. Windows berücksichtigt dabei auch unterstützte Signaturzeitstempel. Ein aktuell abgelaufenes Unterzeichnerzertifikat kann bei gültigem Zeitstempel trotzdem zu einer gültigen Signatur gehören.

- **Gültig · Sperrprüfung aus Cache:** Windows hat unter der lokalen Vertrauensrichtlinie und den vorhandenen Cache-Daten erfolgreich geprüft. Dies ist keine Zusicherung aktueller Online-Sperrinformationen.
- **Gültig · Online-Sperrprüfung:** Netzwerkzugriff für die Prüfung ist erlaubt. Windows darf trotzdem noch gültige Cache-Daten nutzen; eine frische Netzabfrage wird nicht erzwungen.
- **Sperrstatus unbekannt:** Die benötigten Sperrinformationen fehlen oder sind nicht erreichbar. Dies wird nicht als „gültig“ dargestellt.
- **Keine eingebettete Signatur** und **Signaturformat nicht unterstützt** bleiben getrennte Ergebnisse.
- Fehlerhafte, nicht vertrauenswürdige, gesperrte und zeitlich ungültige Zertifikate erhalten getrennte Statuswerte. Der originale Windows-Rückgabecode wird als Hexwert angezeigt.

Der Umfang sind **die primäre und alle zusätzlichen eingebetteten Authenticode-Signaturen, die Windows über `WINTRUST_SIGNATURE_SETTINGS` meldet**. Jede wird separat geprüft, auch wenn die primäre Prüfung fehlschlägt. Text und Farbe des Signaturstatus folgen der ausgewählten Signatur. Die Tabelle zeigt deren Prüfstatus, kein pauschales „alle Signaturen gültig“. Zertifikate aus der Vertrauenskette und Zeitstempel-Unterzeichner werden nicht als zusätzliche Dateisignaturen angeboten. Separate `.cat`-Zuordnungen, Signaturen innerhalb von Archiven, PDF-/Office-Signaturen sowie der Import eigenständiger CER/PFX-Zertifikate sind nicht implementiert. Hashwerte können unabhängig davon für beliebige lesbare Dateien berechnet werden. „Keine eingebettete Signatur“ bedeutet deshalb nicht, dass eine Datei niemals anderweitig signiert ist.

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

- [FileVersionInfo](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.fileversioninfo?view=net-10.0)
- [GetFileVersionInfoByHandle](https://learn.microsoft.com/en-us/windows/win32/menurc/getfileversioninfobyhandle)
- [VerQueryValueW](https://learn.microsoft.com/en-us/windows/win32/api/winver/nf-winver-verqueryvaluew)
- [PE- und COFF-Format](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format)
- [Windows-Dateityp: SHGetFileInfoW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow)
- [VS-Code-Workspace-Tasks](https://code.visualstudio.com/docs/editing/workspaces/multi-root-workspaces)

## Code-Stil

Die `.editorconfig` ist nach Themen und Regel-IDs geordnet; jede Einstellung trägt einen kurzen Kommentar. [Einstellungen und Erklärungen nebeneinander](docs/EDITORCONFIG.md). Die engeren Testausnahmen stehen in `tests/.editorconfig`.

### Fenstergröße der Optionen

Das Optionsfenster speichert seine normale Breite/Höhe und den Maximierungszustand beim Schließen separat unter `OptionsWindow` in `settings.json`. Beim erneuten Öffnen (auch nach einem Programmneustart) werden diese Werte wiederhergestellt. Minimiertes Schließen erhält die letzte normale bzw. maximierte Ansicht. Ältere Einstellungen verwenden weiterhin die Standardgröße; die Größe wird wie beim Hauptfenster auf die verfügbare primäre Arbeitsfläche begrenzt. Die Position bleibt auf das Besitzerfenster zentriert.

NE-Layoutreferenzen: [Open Watcom: exeos2.h](https://github.com/open-watcom/open-watcom-v2/blob/master/bld/watcom/h/exeos2.h), [Wine: IMAGE_OS2_HEADER](https://github.com/wine-mirror/wine/blob/master/include/winnt.h).
