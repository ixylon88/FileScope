# FileScope

FileScope ist eine Windows-App, die Prüfsummen, digitale Signaturen und technische Dateieigenschaften in einer gemeinsamen Übersicht zusammenführt.

## Auf einen Blick

- **Einzelne Dateien oder ganze Ordner analysieren:** Dateien und Ordner per Drag-and-drop hinzufügen oder auswählen; Ordner werden einschließlich Unterordnern eingelesen.
- **Prüfsummen berechnen:** MD5, SHA-1, SHA-256, SHA-384 und SHA-512 frei auswählen und gemeinsam berechnen.
- **Digitale Signaturen prüfen:** Eingebettete Authenticode-Signaturen einschließlich Mehrfachsignaturen prüfen, optional mit <u>Online-Sperrprüfung</u> der Zertifikate; Unterzeichner, Zertifikatsdaten und Zeitstempel anzeigen.
- **Programmdateien einordnen:** Versions- und Herstellerangaben, Architektur und .NET-Plattform auslesen; auch ältere NE-/Win16-EXE und -DLL erkennen.
- **Ergebnisse gezielt auswerten:** Über alle Angaben suchen, nach Analyse- und Signaturstatus filtern, sortieren und Details kopieren.
- **Ergebnisse weiterverwenden:** Vorhandene Analysewerte mit frei wählbaren Feldern als CSV oder JSON exportieren, ohne die Quelldateien erneut zu analysieren.
- **Ansicht anpassen:** Spalten und Hashdarstellung konfigurieren; Deutsch, Englisch oder vereinfachtes Chinesisch wählen.
- **Windows x64** und **Windows on ARM64** wird unterstützt (Letzteres wurde auf Parallels getestet).

Die Quelldateien werden zur Analyse ausschließlich gelesen; untersuchte Programme werden dabei nicht ausgeführt.

Die App basiert auf WPF und .NET 10. Die Projektmappe enthält **genau zwei Projekte**. Der vollständige Quelltext liegt bei. Bis auf einen Microsoft-Analyzer (Codeanalyse) werden keine Drittanbieter-NuGet-Pakete verwendet.

## Effiziente Dateianalyse – ein Lesedurchlauf für alle Prüfsummen

**FileScope ist darauf ausgelegt, vollständige Lesedurchläufe durch Dateien auf ein Minimum zu reduzieren.** Unabhängig davon, wie viele Prüfsummenalgorithmen aktiviert sind, liest FileScope den Dateiinhalt für die Analyse nur **ein einziges Mal vollständig**. Auf Windows verhindert die gewählte Freigabeart paralleles Schreiben/Löschen während der Analyse. 

- **Ein gemeinsamer Lesedurchlauf:** MD5, SHA-1, SHA-256, SHA-384 und SHA-512 werden gleichzeitig aus denselben eingelesenen Datenblöcken berechnet. Auch bei fünf aktivierten Algorithmen ist nur ein vollständiger Lesedurchlauf erforderlich.
- **Ein zentral verwalteter Datei-Handle:** Jede Datei wird für die Analyse einmal durch FileScope geöffnet. Der Stream bleibt während der Analyse geöffnet und wird für die Prüfsummenberechnung, die PE-/NE-Header-Analyse, die Windows-Versionsabfrage und die Windows-Signaturprüfung wiederverwendet. Für Versionsinformationen greift nur bei fehlender Handle-API die bisherige Pfad-Abfrage als Kompatibilitätslösung.
- **Streaming statt vollständigem Laden:** Die Hashberechnung verarbeitet die Datei blockweise mit einem gemeinsamen 128-KiB-Puffer. Dadurch bleibt der Speicherbedarf weitgehend unabhängig von der Dateigröße.
- **Gezielte zusätzliche Lesezugriffe:** Die optionale PE-/NE-Header-Analyse liest benötigte Headerbereiche erneut. `GetFileVersionInfoByHandle` erhält für Versionsinformationen denselben offenen Handle. Windows kann dabei zusätzliche Ressourcen und gegebenenfalls MUI-Sprachdateien lesen; Handle-Wiederverwendung bedeutet keine Garantie für nur einen Lesezugriff.
- **Native Signaturprüfung:** Für Authenticode erhält `WinVerifyTrust` den bereits geöffneten Datei-Handle. Windows kann für die kryptografische Prüfung zusätzliche Leseoperationen ausführen, gegebenenfalls auch über den gesamten Dateiinhalt.

**Wichtig:** Die Aussage über den einmaligen vollständigen Lesedurchlauf bezieht sich auf die von FileScope selbst implementierte Prüfsummenberechnung. Optionale Metadaten- und Signaturprüfungen können weitere Lesezugriffe verursachen. Insbesondere lässt sich nicht garantieren, dass Windows intern jede Datei nur einmal liest. Weiterhin kann FileScope beim Einlesen verschiedener Hardlink-Pfade denselben Dateiinhalt mehrfach lesen.

Durch die gemeinsame Verarbeitung eignet sich FileScope besonders für die Prüfsummenberechnung großer Dateien und umfangreicher Dateisammlungen.

## Starten

Voraussetzung: Windows und ein installiertes **.NET 10 SDK**. Für Visual Studio den Workload **.NET-Desktopentwicklung** sowie eine Version mit .NET-10-Unterstützung verwenden. Es wird ein workspace für Visual Studio Code mit vordefinierten Tasks mitgeliefert.

Visual Studio:

1. Klonen, oder herunterladen und das ZIP vollständig entpacken.
2. `FileScope.sln` öffnen und `FileScope.App` als Startprojekt festlegen.
3. Mit F5 starten.

Visual Studio Code:

1. Klonen, oder herunterladen und das ZIP vollständig entpacken.
1. `FileScope.code-workspace` öffnen
1. Strg + Umschalt + B startet den vordefinierten Build task (Release); es gibt noch weitere Tasks.
1. Der Ausgabeordner erscheint in der Konsole, dort die Exe ausführen.

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

### VS-Code-Workspace-Tasks

`FileScope.code-workspace` öffnen. Unter **Terminal → Task ausführen** stehen drei Aufgaben:

| Task | Skriptaufruf |
| --- | --- |
| `FileScope: Build (Release)` | `Build.ps1 -Configuration Release`; Standard-Build mit Ctrl+Shift+B |
| `FileScope: Publish (win-x64)` | `Build.ps1 -Configuration Release -Publish -Runtime win-x64` |
| `FileScope: Publish (win-arm64)` | `Build.ps1 -Configuration Release -Publish -Runtime win-x64` |
| `FileScope: Publish self-contained (win-x64)` | `Build.ps1 -Configuration Release -Publish --self-contained true -Runtime win-x64` |
| `FileScope: Publish self-contained (win-arm64)` | `Build.ps1 -Configuration Release -Publish --self-contained true -Runtime win-x64` |
| `FileScope: Verify` | `Verify.ps1`; baut die Lösung und führt alle für Windows vorgesehenen Prüfungen aus |

Die Tasks starten `powershell.exe` ohne Profil und verwenden den benannten Workspace-Ordner als Arbeitsverzeichnis. Auch Pfade mit Leerzeichen funktionieren, da Argumente getrennt übergeben werden. Das .NET-10-SDK muss im `PATH` des VS-Code-Prozesses liegen. Publish erzeugt eine eigenständige Ausgabe unter `artifacts/publish/win-x64`.

### Eigenständige Windows-Ausgabe

#### Windows (x64)
```powershell
.\Build.ps1 -Publish -Runtime win-x64
```

#### Windows (ARM64)
```powershell
.\Build.ps1 -Publish -Runtime win-arm64
```

Die Ausgabe liegt unter `artifacts/publish/win-x64` bzw. `artifacts/publish/win-arm64`. Den **gesamten** Ordner weitergeben, einschließlich `de` und `zh-Hans`; diese enthalten die Sprachressourcen. Durch einen self-contained publish ist auf dem Zielrechner keine separate .NET-Desktop-Runtime erforderlich. Die Ausgabe wird nicht getrimmt, da WPF und dynamische Ressourcen Reflection verwenden. ARM64 bzw. x86 können über den Runtime-Parameter gewählt werden.

**Plattformunterstützung**: Windows x64 und Windows ARM64. Die ARM64-Version wurde unter Windows 11 in Parallels Desktop erfolgreich getestet. Weitere Zielarchitekturen können über den Runtime-Parameter gewählt werden.

## Bedienung

| Funktion | Verhalten |
| --- | --- |
| Dateien oder Ordner ablegen | Eine oder mehrere Dateien und/oder Ordner auf einen beliebigen Bereich des Fensters ziehen. Für Ordner bestätigt eine gemeinsame Rückfrage die Auswertung einschließlich Unterordnern; „Nein“ verwirft den gesamten Drop. Alternativ „Dateien öffnen“ / Ctrl+O oder „Ordner hinzufügen“. |
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
| Dateikontextmenü | Rechtsklick auf eine Ergebniszeile: „Eigenschaften“ öffnet den Windows-Dateidialog, „Im Explorer anzeigen“ öffnet den übergeordneten Ordner und markiert genau diese Datei. Das Menü gilt für die angeklickte Zeile, auch bei einer Mehrfachauswahl; über die Menütaste / Umschalt+F10 ist es ebenfalls erreichbar. |
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
| Design | Hell/Dunkel unter **Optionen → Design** umschalten; die Auswahl wird gespeichert. Native Fluent-Controls ohne eigene Vorlagen für Zeilen/Zellen. |
| Über FileScope | Info-Knopf unter „Optionen“: Programmversion, Funktionsübersicht, Erklärung des gemeinsamen Lesedurchlaufs sowie Repository und MIT-Lizenz. |

## Funktionen im Detail

### Startanzeige

Beim Start erscheint das orange FS-Symbol als kleiner Splashscreen. Sobald das Hauptfenster bereitsteht, blendet WPF ihn automatisch in etwa 300 ms aus. Es gibt keine künstliche Mindestanzeigedauer und keine zusätzliche Initialisierung für ein eigenes Splash-Fenster. Ein animiertes Einblenden ist beim eingebauten WPF-Splash nicht vorgesehen.

Ein langsamer erster Start kann unter anderem vom Laden der Runtime und der Assemblies sowie von JIT-Kompilierung abhängen; daraus allein lässt sich die Ursache in einer ARM64-VM nicht ableiten. Erst mehrere Kalt- und Warmstarts vergleichen. Der Splash macht den Start sichtbar, verkürzt ihn aber nicht. `ReadyToRun` kann JIT-Arbeit beim Start reduzieren, vergrößert dafür die Programmdateien; die Publish-Einstellungen bleiben unverändert. Siehe [WPF-Splashscreen](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/app-development/how-to-add-a-splash-screen-to-a-wpf-application) und [ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run).

### Dateien im Explorer untersuchen

Das Kontextmenü einer Ergebniszeile verwendet die nativen Fluent-Menüs und folgt dem gewählten Design und der Sprache. „Eigenschaften“ zeigt die Windows-Eigenschaften; „Im Explorer anzeigen“ springt auch bei tief verschachtelten Dateien direkt zum richtigen Ordner und markiert die Datei. Die Aktion startet die untersuchte Datei nicht. Freie Tabellenflächen und Spaltenüberschriften haben kein Dateimenü.

Wurde eine Datei inzwischen verschoben oder gelöscht oder verweigert Windows die Aktion, erscheint ein eigener Hinweis mit dem Pfad unter dem Analysestatus. Analyseergebnisse und Durchlaufstatistik bleiben erhalten. Ein erfolgreicher erneuter Aufruf oder „Leeren“ entfernt diesen Hinweis. Die Explorer-Anfrage läuft im Hintergrund; eine noch laufende Anfrage kann nicht doppelt gestartet werden. Windows darf für diese ausdrücklich angeforderten Shell-Aktionen eigene zusätzliche Dateizugriffe durchführen.

### Über FileScope

Der **Über**-Knopf mit Info-Symbol öffnet einen Dialog über dem Hauptfenster. Er zeigt die Version des laufenden Programms einschließlich vorhandener Versionszusätze, eine kurze Funktionsliste und die genaue Bedeutung des gemeinsamen Dateizugriffs: FileScope öffnet jede Datei pro Analyse einmal; alle ausgewählten Hashes teilen sich einen vollständigen Lesedurchlauf. Header-, Versions- und Signaturprüfungen können zusätzlich lesen, Windows gegebenenfalls auch ergänzende Ressourcen.

Repository und Online-Lizenz werden erst beim Anklicken im Standardbrowser geöffnet. Der vollständige englische MIT-Lizenztext einschließlich Copyright ist außerdem aufklappbar und offline les- und kopierbar. Er stammt direkt aus der `LICENSE` im Repository, wird in die Anwendung eingebettet und zusätzlich in den Build-/Publish-Ordner kopiert. Kann der Browser nicht geöffnet werden, erscheint ein Hinweis im Dialog. Sprache und Design folgen der Anwendung; der Inhalt bleibt auch bei kleiner Fenstergröße scrollbar. **Schließen** oder Escape schließen den Dialog.

### Helles und dunkles Design

Unter **Optionen → Design** lässt sich während der Sitzung zwischen Hell und Dunkel wechseln. Die geöffneten Fenster, Hinweisfarben und Status-Badges passen sich an. Die Auswahl wird sofort gespeichert und beim nächsten Start wiederhergestellt; Analyseergebnisse, Filter und Spaltenreihenfolge bleiben erhalten. Fehlt in älteren Einstellungen ein gültiger Designwert, bleibt das aktuelle Anwendungsdesign erhalten, beim regulären Start standardmäßig Hell.

### Ordnersuche und Export

Die Ordnersuche berücksichtigt auch versteckte Dateien. Verzeichnisverknüpfungen (Junctions und symbolische Links) innerhalb des ausgewählten Ordners werden übersprungen, damit keine Schleifen entstehen. Nicht lesbare Unterordner werden gemeldet; lesbare Geschwisterordner werden weiter durchsucht. Bereits vorhandene Pfade werden nicht erneut analysiert. „Abbrechen“ beendet die Suche ohne neue Zeilen; beim späteren Hinzufügen oder Analysieren bleiben bereits hinzugefügte Zeilen erhalten und können mit „Alle neu prüfen“ verarbeitet werden. „Leeren“ ist erst nach Abschluss oder beendetem Abbruch verfügbar und entfernt dann alle Zeilen.

Beim Ablegen von Ordnern zeigt eine MessageBox vor Beginn der Suche deren Anzahl und bis zu fünf Pfade an. „Ja“ bestätigt die rekursive Auswertung aller abgelegten Ordner, „Nein“ verwirft den gesamten Drop einschließlich direkt mit abgelegter Dateien. Reine Datei-Drops benötigen keine Rückfrage. Mehrfach abgelegte oder überlappende Pfade erzeugen keine doppelten Ergebniszeilen.

#### Symbolische Links, Junctions und Hardlinks

| Linkart | Verhalten bei der Ordnersuche |
| --- | --- |
| Verzeichnis-Symlink, Junction oder anderer Verzeichnis-Reparse-Point innerhalb eines Startordners | Wird gezählt und übersprungen. Damit werden auch Links zurück auf übergeordnete Ordner nicht rekursiv verfolgt. |
| Ein ausdrücklich ausgewählter oder abgelegter Startordner ist selbst ein Verzeichnislink | Sein Ziel wird als Startordner durchsucht; Verzeichnislinks innerhalb dieses Baums werden weiterhin übersprungen. |
| Symbolischer Link auf eine Datei | Bleibt ein eigener Dateipfad. Beim Analysieren wird sein Ziel geöffnet; ein nicht erreichbares oder zyklisch verlinktes Ziel erhält die übliche Fehlerdiagnose in seiner Zeile. |
| Hardlink auf eine Datei | Jeder unterschiedliche Pfad bleibt eine eigene Zeile und wird separat analysiert. Eine Zusammenfassung anhand der physischen Datei-ID findet nicht statt. |

Unter Windows können Hardlinks keine Verzeichnisse referenzieren und daher keine rekursiven Verzeichnisschleifen erzeugen. Der Windows-Komponentenspeicher `WinSxS` verwendet viele solcher Dateiverknüpfungen. Beim Einlesen verschiedener Hardlink-Pfade kann FileScope denselben Dateiinhalt mehrfach lesen; die Größen dieser Zeilen dürfen deshalb nicht als tatsächlich zusätzlich belegter Speicherplatz interpretiert werden. Siehe [Microsoft: Hardlinks und Junctions](https://learn.microsoft.com/en-us/windows/win32/fileio/hard-links-and-junctions) und [Windows-Komponentenspeicher](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/manage-the-component-store).

#### Diagnosedetails

Die Spalte **Diagnose** steht standardmäßig direkt hinter **Analyse**. Sie ist ausgeblendet, solange keine geladene Zeile Diagnosedetails enthält, und erscheint bei der ersten Diagnose automatisch. Maßgeblich sind die Diagnosen aller geladenen Zeilen, einschließlich ausgefilterter Zeilen und der jeweils ausgewählten Signatur. Die Statusleiste ergänzt die Anzahl betroffener Zeilen und bricht längere Meldungen um, damit der Hinweis sichtbar bleibt. Eine erneute Prüfung ohne Diagnosen oder „Leeren“ entfernt Spalte und Hinweis wieder. Gespeicherte Spaltenpositionen bleiben erhalten; **Optionen → Spaltenreihenfolge → Reihenfolge zurücksetzen** übernimmt die neue Standardposition. CSV-/JSON-Exportauswahl und Diagnosewerte bleiben von der automatischen Sichtbarkeit unabhängig.

#### Durchlaufstatistik

Nach einer Analyse zeigt die Statusleiste die **verarbeiteten Dateien**, die **Dauer** und die **Summe der bekannten Dateigrößen in Bytes** an. Die Dauer umfasst den Analysedurchlauf einschließlich der Signaturprüfung und ihrer Wartezeiten; die vorgelagerte Ordnersuche, Dateiaufnahme und ein späterer Export gehören nicht dazu. Der vorhandene Hinweis auf Diagnosedetails bleibt am Ende der Meldung stehen. Der Mittelpunkt ` · ` trennt sowohl die Durchlaufwerte als auch den optionalen Diagnosehinweis vom vorherigen Meldungsteil.

Beim Hinzufügen weiterer Dateien zählt nur der neue Durchlauf; „Alle neu prüfen“ bildet die Statistik für sämtliche geladenen Zeilen neu, unabhängig von Tabellenfiltern. Ein Abbruch zeigt die bis dahin abgeschlossenen Dateiergebnisse einschließlich Fehlerergebnissen an. Noch nicht abgeschlossene Dateien gehen nicht in die Summe ein. Fehlt eine Dateigröße, nennt die Statusleiste zusätzlich die Anzahl dieser Dateien. „Leeren“ entfernt die Statistik.

Die Größen stammen aus den bereits ermittelten Ergebnissen; die Dateien werden dafür nicht erneut geöffnet. Die Summe zählt pro analysiertem Pfad und beschreibt weder den tatsächlich belegten Speicherplatz noch die insgesamt gelesenen I/O-Bytes. Unterschiedliche Hardlink-Pfade zählen daher jeweils mit ihrer Dateigröße.

#### Exportformate

Der vorgeschlagene Dateiname enthält die UTC-Zeit beim Öffnen des Speichern-Dialogs, beispielsweise `FileScope_2026-11-08T16-25-13Z.csv` beziehungsweise `.json`. `Z` kennzeichnet UTC; die Uhrzeit verwendet Bindestriche, weil Doppelpunkte in Windows-Dateinamen nicht zulässig sind. Das Format bleibt bei jedem Sprachwechsel gleich. Der Vorschlag kann im Dialog geändert werden.

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

### Dateieigenschaften und Programm-Header

**Dateieigenschaften** liest die vorhandenen `VERSIONINFO`-Felder: Dateibeschreibung, Sprache, Datei- und Produktversion, Produktname, Hersteller, Originaldateiname, interner Name, Copyright, Marken und Kommentare. Fehlende Felder bleiben leer. „Dateityp“ kommt aus der Windows-Zuordnung der Dateiendung; es ist keine Inhaltsprüfung und kann von der tatsächlichen Datei abweichen. Ohne Windows wird die Endung angezeigt. Versionsfelder, Dateisprache und Windows-Typbezeichnung bleiben in ihrer Original-/Systemsprache; die Spaltenüberschriften wechseln mit der App-Sprache. Allgemeine Office-/PDF-Dokumenteigenschaften oder Bild-EXIF sind nicht Teil dieser Auswertung.

**EXE-Header / Plattform** prüft DOS-/PE-/NE-Kennung und liest den COFF-Maschinentyp, beispielsweise `i386 (0x014C)`, `AMD64 (x64)`, `ARM`, `ARM64` oder `Alpha`. Unbekannte Werte bleiben als Hexcode sichtbar. Die Erkennung richtet sich nach dem Dateiinhalt, nicht nach `.exe` oder `.dll`. Dateien ohne unterstützten PE- oder NE-Header liefern leere Header-Felder; beschädigte erkannte Header erhalten eine Diagnose.

Die zusätzlichen Spalten zeigen `PE32`/`PE32+` und bei vorhandenen CLR-Headern die .NET-Plattform. Ein IL-only-i386-PE kann `AnyCPU`, `AnyCPU (32 Bit bevorzugt)` oder explizit `i386` sein. Native Dateien haben keine .NET-Plattform. Maschinentyp und CLR-Flags beschreiben die Header und sind keine Garantie für Lauffähigkeit auf einem bestimmten Windows-System. Es wird ausschließlich gelesen; keine Assembly wird geladen oder ausgeführt.

**Windows-16-Bit-Dateien** verwenden NE (New Executable), nicht „PE16“. EXE und DLL werden als `NE (Win16)` mit `x86 (16-bit)` erkannt. NE enthält keinen COFF-Maschinentyp; deshalb wird kein PE-Hexcode erfunden. OS/2-, DOS-4.x- und Windows/386-Ziele bleiben unterscheidbar; bei Windows/386 und unbekannten Zielwerten wird keine 16-Bit-Codebreite unterstellt. Ein bloßes `MZ` am Dateianfang reicht nicht zur Win16-Erkennung, da auch PE, LE und LX einen DOS-Stub besitzen. Reine DOS-MZ-, LE- und LX-Dateien liefern weiterhin keine PE-/NE-Metadaten; ihre Prüfsummen sind davon unabhängig.

Die Werte erscheinen auch in Details, Kopierausgabe und Textsuche. Neu aktivierte Prüfungen gelten für den nächsten Durchlauf; für vorhandene Zeilen „Alle neu prüfen“ verwenden. Ein erwarteter Lesefehler einer optionalen Metadatenprüfung wird in der Diagnosespalte ergänzt; bereits berechnete Hashes und die übrigen Prüfungen bleiben erhalten. Die Verarbeitungsanzeige kann deshalb „Fertig“ zeigen, während eine optionale Auswertung eine Diagnose enthält.

### Zertifikatsprüfung: genaue Bedeutung

Verwendet wird `WinVerifyTrust` mit der Windows-Authenticode-Richtlinie. Dadurch werden Dateiintegrität und Vertrauenskette durch Windows beurteilt; ein auslesbares Zertifikat allein wird nie als gültige Signatur gewertet. Windows berücksichtigt dabei auch unterstützte Signaturzeitstempel. Ein aktuell abgelaufenes Unterzeichnerzertifikat kann bei gültigem Zeitstempel trotzdem zu einer gültigen Signatur gehören.

- **Gültig · Sperrprüfung aus Cache:** Windows hat unter der lokalen Vertrauensrichtlinie und den vorhandenen Cache-Daten erfolgreich geprüft. Dies ist keine Zusicherung aktueller Online-Sperrinformationen.
- **Gültig · Online-Sperrprüfung:** Netzwerkzugriff für die Prüfung ist erlaubt. Windows darf trotzdem noch gültige Cache-Daten nutzen; eine frische Netzabfrage wird nicht erzwungen.
- **Sperrstatus unbekannt:** Die benötigten Sperrinformationen fehlen oder sind nicht erreichbar. Dies wird nicht als „gültig“ dargestellt.
- **Keine eingebettete Signatur** und **Signaturformat nicht unterstützt** bleiben getrennte Ergebnisse.
- Fehlerhafte, nicht vertrauenswürdige, gesperrte und zeitlich ungültige Zertifikate erhalten getrennte Statuswerte. Der originale Windows-Rückgabecode wird als Hexwert angezeigt.

Der Umfang sind **die primäre und alle zusätzlichen eingebetteten Authenticode-Signaturen, die Windows über `WINTRUST_SIGNATURE_SETTINGS` meldet**. Jede wird separat geprüft, auch wenn die primäre Prüfung fehlschlägt. Text und Farbe des Signaturstatus folgen der ausgewählten Signatur. Die Tabelle zeigt deren Prüfstatus, kein pauschales „alle Signaturen gültig“. Zertifikate aus der Vertrauenskette und Zeitstempel-Unterzeichner werden nicht als zusätzliche Dateisignaturen angeboten. Separate `.cat`-Zuordnungen, Signaturen innerhalb von Archiven, PDF-/Office-Signaturen sowie der Import eigenständiger CER/PFX-Zertifikate sind nicht implementiert. Hashwerte können unabhängig davon für beliebige lesbare Dateien berechnet werden. „Keine eingebettete Signatur“ bedeutet deshalb nicht, dass eine Datei niemals anderweitig signiert ist.

**Zum Datum:** „Gültig ab“ ist der Gültigkeitsbeginn des Zertifikats. Ein X.509-Zertifikat enthält kein allgemeines separates Feld für das Datum seiner eigenen Unterzeichnung. Die Spalte zeigt stattdessen die Zeit aus dem Zeitstempel der Dateisignatur: ältere PKCS#9-Gegensignaturen oder RFC-3161-Token. Ein Zeitstempel bestätigt bei erfolgreicher Prüfung, dass die Signatur zu dieser Zeit bereits vorlag; er ist nicht zwingend der exakte Moment der ursprünglichen Unterzeichnung. Selbst angegebene `signingTime`-Attribute des Dateisignierers, die aktuelle Uhrzeit und der Gültigkeitsbeginn werden nicht als Ersatz verwendet.

Die Beschriftung „bestätigt“ verlangt einen erfolgreichen Windows-Prüfstatus, einen fehlerfrei geprüften Gegensignierer und einen ausgelesenen Zeitstempel, der zur von Windows verwendeten Prüfzeit passt. Andernfalls bleibt ein lesbares Datum „nicht bestätigt“; das ist keine Behauptung, dass der Zeitstempel ungültig sei. Offline fehlende Vertrauens-/Sperrinformationen können eine Bestätigung verhindern.

Die angezeigten Zertifikatsfingerabdrücke beziehen sich auf das Zertifikat; die Hashspalten beziehen sich auf die Datei. Der Zertifikatssignaturalgorithmus ist ebenfalls der Algorithmus des Zertifikats und nicht automatisch der Dateisignatur. MD5/SHA-1 sind für den Vergleich vorhandener/älterer Prüfsummen enthalten; SHA-256 ist voreingestellt.

Dateien werden nur gelesen. Während der Analyse bleibt derselbe Lesehandle offen; auf Windows verhindert die Freigabeart paralleles Schreiben/Löschen. Der native Prüfer erhält denselben Handle. Die untersuchten Programme werden nicht gestartet, Archive nicht entpackt und Zertifikate nicht installiert. Bei eingeschalteter Online-Sperrprüfung kann Windows Zertifizierungsstellen kontaktieren.

## Architektur

### KISS: einfache Infrastruktur

FileScope orientiert sich am KISS-Prinzip („Keep It Simple“): Die Infrastruktur bleibt dem überschaubaren Aufgabenbereich der Anwendung angemessen. Schnittstellen und Entwurfsmuster erfüllen konkrete Aufgaben, etwa austauschbare Analyseverfahren, die Trennung von WPF und Geschäftslogik oder gezielte Tests.

- **Abhängigkeiten:** Konstruktor-Injektion und die direkte Zusammenstellung in `App.OnStartup` machen die Abhängigkeiten sichtbar. Dafür werden weder ein DI-Container noch ein Generic Host benötigt.
- **Einstellungen:** `UserPreferences`, `AnalysisPreferences` und `WindowPreferences` sind bereits die Datenmodelle. `PreferencesStore` übernimmt über `IPreferencesStore` das Laden und Speichern dieser Daten als JSON mit `System.Text.Json`; `OptionsViewModel` koordiniert die Bedienung und Speicherung. Für die lokale Einstellungsdatei genügt diese Aufteilung. Eine zusätzliche Konfigurationsschicht mit `Microsoft.Extensions.Configuration` oder weitere Vermittler im Composition Root bringen für den aktuellen Funktionsumfang keinen erkennbaren Nutzen.
- **Fehlerdiagnose:** Ein Logging-Framework wird derzeit nicht verwendet. Analysefehler und optionale Metadatenprobleme erscheinen in den Ergebniszeilen und Diagnosedetails; Einstellungsfehler werden im Optionsfenster gemeldet. So bleiben diese Fehler unmittelbar bei der betreffenden Aktion sichtbar.

Automatisierte Tests und Logging erfüllen unterschiedliche Aufgaben: Tests prüfen bekannte Fälle und schützen vor Regressionen. Ein Laufzeitprotokoll kann zusätzlich helfen, zeitabhängige oder rechnerspezifische Fehler nachzuvollziehen, die sich in der Testumgebung nicht reproduzieren lassen. Auch umfangreiche Tests ersetzen diese Beobachtungsmöglichkeit nicht. Gezieltes Logging lässt sich ergänzen, sobald ein konkreter Diagnosebedarf dies rechtfertigt.

### Projekte und Entwurfsmuster

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
| `AboutWindow` / `IExternalLinkService` | Reine Programminformationen, Version aus der App-Assembly und eingebetteter Lizenztext. Ein kleiner austauschbarer Dienst öffnet HTTPS-Projektlinks; UI-Prüfungen ersetzen ihn, ohne einen Browser zu starten. |
| `IThemeService` / `ThemeService` | WPF-unabhängiger Vertrag für das Design; die konkrete Implementierung koordiniert die Farbpalette mit dem nativen Fluent-`ThemeMode`. |
| `IFileShellService` / `FileShellService` | Öffnet auf ausdrücklichen Benutzerwunsch Windows-Dateieigenschaften oder markiert die Datei im Explorer. UI-Prüfungen ersetzen den Dienst, ohne Shell-Fenster zu starten. |
| `ColumnLayoutBehavior` | Überträgt stabile Spalten-IDs auf die `DisplayIndex`-Werte und synchronisiert manuelles Verschieben zurück; koordiniert die begrenzte automatische Breitenmessung. |
| `StatusBadge` / `StatusToneConverter` | Wiederverwendbares UI-Element mit Text und Farbrolle. Eine zentrale Zuordnung übersetzt Verarbeitungs- und Signaturzustände in diese Rollen; Rundung, Abstände und Palette sind im gemeinsamen Control definiert. |
| `DigestFormatter` | Formatiert die vorhandenen Digest-Bytes; `FileRowViewModel` hält die dargestellten Werte im Cache. Die Core-Ergebnisse bleiben unverändert. |
| `IPreferencesStore` / `PreferencesStore` | Speichert kleine Benutzereinstellungen über eine temporäre Datei mit anschließendem Ersetzen der Zieldatei. |
| `BindingProxy` | Liefert den Fenster-DataContext an DataGrid-Spalten, die selbst keinen DataContext erben. Damit bleibt die Sichtbarkeit per Binding an die Prüfoptionen gekoppelt. |

MVVM trennt View, ViewModel und Domänenmodelle. Code-behind beschränkt sich auf Darstellungsaufgaben: Fensterlebenszyklus einschließlich Größenwiederherstellung, Öffnen der Options- und Infodialoge, Anzeige fester Programminformationen und Sichtbarhalten der ausgewählten Listenzeile. Der Infodialog benötigt dafür kein zusätzliches ViewModel; Browseraufrufe liegen hinter `IExternalLinkService`. `INotifyPropertyChanged` sowie `ICollectionView` aktualisieren und filtern die Darstellung.

Das Menüband ist eine gruppierte native WPF-Befehlsleiste im Fluent-Stil, **kein zusätzlicher Ribbon-Control-Anbieter**. Der WPF-Tabellentyp heißt `DataGrid`; `DataGridView` gehört zu Windows Forms.

Die Kernbibliothek zielt auf `net10.0`; ihre Hash- und Orchestrierungslogik lässt sich auch ohne Windows verwenden. Die WPF-App zielt auf `net10.0-windows`. Die native Authenticode-Strategie meldet außerhalb von Windows ausdrücklich „UnsupportedPlatform“.

### Dateiaufnahme und Verarbeitung

Während der Verarbeitung sind neue Dateiaufnahmen, Prüfoptionen und „Leeren“ gesperrt. Eine aktive native Windows-Signaturprüfung ist nicht mitten im Aufruf abbrechbar; „Abbrechen“ wartet auf deren Rückkehr. Danach ist „Leeren“ wieder verfügbar. Das Fenster bleibt bedienbar.

Mehrfach abgelegte **normalisierte Pfade** werden unter Windows ohne Beachtung der Groß-/Kleinschreibung dedupliziert. Unterschiedliche Pfade mit demselben Dateiinhalt bleiben separate Zeilen. Ordner werden über „Ordner hinzufügen“ oder nach Bestätigung per Drag-and-drop mit derselben `IFolderScanner`-Strategie rekursiv eingelesen. `IConfirmationService` kapselt die MessageBox außerhalb des ViewModels und wird im Composition Root injiziert. Bei einem gemischten Drop werden erst nach abgeschlossener Suche Dateien hinzugefügt; ein Suchabbruch verwirft auch die direkt mit abgelegten Dateien. Ein nicht mehr zugänglicher Startordner wird gemeldet, während die übrigen Startordner weiter durchsucht werden. Sind sämtliche Prüf-Häkchen abgewählt, werden nur Pfad, Dateigröße und Änderungsdatum ermittelt.

`MainViewModel` misst jeden Analysedurchlauf mit `Stopwatch` und hält dessen Abschlusswerte als unveränderliche `AnalysisRunSummary` fest. Die UI-unabhängige Präsentationsklasse summiert vorhandene Ergebnisse, unterscheidet unbekannte Größen von leeren Dateien und formatiert den Abschluss in der gewählten Sprache. `decimal` erhält ganzzahlige Bytesummen auch oberhalb der `Int64`-Grenze exakt. Ein Sprachwechsel formatiert denselben Schnappschuss neu, ohne Zeit oder Größen erneut zu ermitteln.

### Prüfoptionen und Einstellungen

Die gewählten Prüfoptionen gelten als unveränderlicher Schnappschuss für einen Durchlauf. Ein späterer Sprach- oder Hashformatwechsel verändert keine Analysewerte. Die fünf Hash-Häkchen, Signaturprüfung, Online-Sperrprüfung, Dateieigenschaften und PE-Header werden bei jeder Änderung zusammen mit Sprache, Design, Hashformat, Spaltenreihenfolge, Fenstergröße und Maximierungszustand in `%LOCALAPPDATA%\FileScope\settings.json` gespeichert. Ergebnisse und manuell veränderte Breiten gelten nur für die laufende Sitzung. Ungültige Einstellungen fallen auf Standardwerte zurück; Schreibfehler werden im Optionsfenster angezeigt, ohne die Dateianalyse zu blockieren.

Auch eine vollständig abgewählte Prüfauswahl wird gespeichert. Die Online-Sperrprüfung behält ihren gewählten Wert, wenn sie durch abgeschaltete Signaturprüfung ausgegraut ist; sie wird erst bei aktivierter Signaturprüfung verwendet. „Leeren“ setzt die Prüf-Häkchen nicht zurück.

Im Code liegen die Bindungswerte in `MainViewModel`: `ChecksumOptions[*].IsSelected`, `InspectCertificates`, `OnlineRevocation`, `InspectFileProperties` und `InspectPeHeader`. Die Startwerte sind zentral in `AnalysisPreferences` in `Services/PreferencesStore.cs` definiert. Ältere Einstellungen ohne den Abschnitt `Analysis` verwenden die bisherigen Standards. Fehlende neue Metadaten-Häkchen sind ausgeschaltet; alle bisherigen Einstellungen bleiben erhalten. `OptionsViewModel` speichert alle Bereiche gemeinsam, damit eine Änderung der Prüf-Häkchen keine anderen Einstellungen überschreibt.

#### Fenstergröße der Optionen

Das Optionsfenster speichert seine normale Breite/Höhe und den Maximierungszustand beim Schließen separat unter `OptionsWindow` in `settings.json`. Beim erneuten Öffnen (auch nach einem Programmneustart) werden diese Werte wiederhergestellt. Minimiertes Schließen erhält die letzte normale bzw. maximierte Ansicht. Ältere Einstellungen verwenden weiterhin die Standardgröße; die Größe wird wie beim Hauptfenster auf die verfügbare primäre Arbeitsfläche begrenzt. Die Position bleibt auf das Besitzerfenster zentriert.

#### Theme-Umschaltung

`IThemeService` und `AppTheme` liegen getrennt von der WPF-Implementierung. Dadurch können die Präsentationstests `OptionsViewModel` mit einem kontrollierten Testdienst ausführen, ohne WPF einzubinden. `ThemeService` ersetzt auf dem UI-Dispatcher ausschließlich die eigene Palette und aktualisiert zugleich `Application.ThemeMode`; die übrigen Ressourcen bleiben erhalten. Auch beim ersten Einsatz ohne Palette werden beide Teile synchronisiert. Ein erneuter Aufruf mit demselben bereits initialisierten Design erzeugt keine neuen Ressourcen.

Die beiden XAML-Paletten stellen dieselben Ressourcenschlüssel bereit. `DynamicResource` aktualisiert bestehende Text- und Badge-Farben beim Austausch. `OptionsViewModel` wendet eine neue Auswahl zuerst an und speichert sie danach gemeinsam mit den übrigen Einstellungen. Scheitert das Anwenden, bleiben Auswahl und gespeicherter Wert erhalten. Scheitert nur das Speichern, bleibt das neue Design in der Sitzung aktiv und das Optionsfenster zeigt den Speicherfehler.

### Tabellendarstellung

`MainViewModel` hält die Zeilen mit Diagnosedetails in einer eigenen Menge. Änderungen am Analyseergebnis und an der Signaturauswahl aktualisieren diese Menge, ohne für jede Änderung die ganze Tabelle zu durchsuchen. `ShowDiagnosticColumn` steuert die Spaltensichtbarkeit über den vorhandenen `BindingProxy`; `DiagnosticRowCount` liefert die Anzahl für den lokalisierten Statustext. Die Filteransicht verändert diese Menge nicht.

Neue Spalten werden bei einer bereits gespeicherten Reihenfolge hinten ergänzt und lassen sich unter Optionen einsortieren. Die Signaturauswahlspalte bleibt bei aktiver Signaturprüfung sichtbar, solange irgendeine geladene Datei mehrere Signaturen hat, auch wenn diese gerade ausgefiltert ist. Nach Leeren oder einer erneuten Analyse ohne Mehrfachsignaturen verschwindet sie.

Die Breitenautomatik misst den sichtbaren Inhalt und wächst bei Bedarf bis zur jeweiligen Obergrenze. Ein Sprach-/Formatwechsel, „Leeren“ oder „Breiten neu anpassen“ startet die Messung neu und erlaubt auch schmalere Spalten. Das Filtern allein setzt manuell gewählte Breiten nicht zurück.

Das Dateikontextmenü liegt im `DataGridRow`-Stil und verwendet die nativen WPF-Vorlagen. Da ein Popup außerhalb des visuellen Fensterbaums liegt, bezieht es seine Zeile über `PlacementTarget.DataContext`; der vorhandene `BindingProxy` liefert die Commands des Haupt-ViewModels. Die Aktion hängt damit weder an einer vorher ausgewählten Zeile noch an einem alten DataContext eines wiederverwendeten Zeilencontainers. Shell-Fehler werden getrennt vom Analysestatus gehalten. Eine verspätete Explorer-Antwort wird nach Entfernen der Zeile oder Schließen des Fensters verworfen.

Beim horizontalen Scrollen kann WPF für `CellsPanelHorizontalOffset` einen geringfügig negativen Wert berechnen. Die Fluent-Vorlage bindet diesen Wert direkt an die Breite ihres internen „Alles auswählen“-Knopfs, auch bei ausgeblendeten Zeilenüberschriften. `DataGridScrollFix` ergänzt beim Laden der Tabelle ausschließlich für diese Bindung eine Begrenzung auf null. Positive Breiten bleiben erhalten; die native Vorlage, fixierte Spalten, Virtualisierung und automatische Spaltenbreiten bleiben bestehen. Die Binding-Fehlerausgabe wird nicht abgeschaltet.

### Ordnersuche und Exportstrategien

Die Exportarchitektur verwendet **Strategy**: `ResultExporter` implementiert `IResultExporter`, wählt die registrierte `IResultExportStrategy` anhand des Formats und übernimmt temporäre Datei, atomaren Austausch, Abbruch und Aufräumen. `CsvExportStrategy` und `JsonExportStrategy` schreiben nur in den bereitgestellten Stream. Die Strategien werden in `App.OnStartup` zusammengestellt. `ResultExportTable` enthält stabile IDs, übersetzte Überschriften und Werte, ohne WPF-Abhängigkeit; `ResultExportProjection` erstellt die Momentaufnahme vor dem Hintergrundexport. Neue Exportfelder werden im zentralen Katalog `ColumnOption.CreateDefaults` ergänzt. Die Ordnersuche liegt getrennt in `IFolderScanner` / `FolderScanner`.

### Programmheader

Die NE-Erweiterung liest den festen Header und prüft die Grenzen von Segment- und Einsprungtabellen. Sie durchsucht weder Nutzdaten noch Ressourcen und übernimmt den vorhandenen Stream samt Positionswiederherstellung. `NeHeaderReader` kapselt das NE-Layout getrennt vom vorhandenen PE-/CLR-Parser. Die internen Namen `InspectPeHeader`, `PeMachine` und `PeFormat` bleiben für gespeicherte Einstellungen und Exporte bestehen. Die sichtbaren Bezeichnungen sind nun „EXE-Header / Plattform“, „Architektur“ und „Programmformat“. CSV und JSON enthalten die erkannten NE-Werte automatisch. VERSIONINFO wird getrennt durch die Windows-API ausgewertet; ein eigener Parser für Win16-Versionsressourcen ist nicht erforderlich.

### Native Windows-API-Anbindung (P/Invoke)

Die Windows-Anbindung besteht aus C#-Deklarationen und Aufrufen nativer Systemfunktionen über P/Invoke. Die verschachtelten `Native`-Klassen bündeln die Importdeklarationen und Speicherlayouts; die Inspector-Klassen übersetzen deren Ergebnisse in die Core-Modelle. Die untersuchten EXE-/DLL-Dateien werden dabei als Daten gelesen. Geladen werden die benötigten Windows-Systembibliotheken aus `System32`.

| Aufgabe | Windows-API / Systembibliothek | Zuständige Klasse |
| --- | --- | --- |
| Eingebettete Signaturen prüfen | `WinVerifyTrust` in `wintrust.dll` | `WindowsAuthenticodeInspector` |
| Unterzeichner, Zertifikate und Zeitstempelattribute aus dem Prüfzustand lesen | `WTHelperProvDataFromStateData`, `WTHelperGetProvSignerFromChain`, `WTHelperGetProvCertFromChain` in `wintrust.dll` | `WindowsAuthenticodeInspector` |
| Versionsressourcen vom offenen Handle lesen und Felder abfragen | `GetFileVersionInfoByHandle`, `VerQueryValueW` in `version.dll` | `WindowsFileVersionReader` |
| Sprachname, Dateizeiger und Freigabe des Versionspuffers | `VerLanguageNameW`, `SetFilePointerEx`, `LocalFree` in `kernel32.dll` | `WindowsFileVersionReader` |
| Registrierte Bezeichnung einer Dateiendung abfragen | `SHGetFileInfoW` in `shell32.dll`; `CoInitializeEx` / `CoUninitialize` in `ole32.dll` | `FilePropertiesInspector` |
| Dateieigenschaften anzeigen und Datei im Explorer markieren | `SHObjectProperties`, `SHParseDisplayName`, `SHOpenFolderAndSelectItems` in `shell32.dll`; COM-Initialisierung in `ole32.dll` | `FileShellService` in der WPF-App |

Die meisten Importe verwenden `LibraryImport` mit zur Übersetzungszeit erzeugtem Marshalling-Code. `SHGetFileInfoW` verwendet bewusst `DllImport`, weil `SHFILEINFOW` die beiden Zeichenfelder direkt in der Struktur enthält und deren `ByValTStr`-Marshalling hier nicht vom Quellcodegenerator unterstützt wird. `GetFileVersionInfoByHandle` wird einmalig mit `NativeLibrary.TryLoad` / `TryGetExport` aufgelöst und als Delegate zwischengespeichert; so bleibt ein fehlender Export als Kompatibilitätsfall behandelbar.

#### Besitz und Lebensdauer nativer Ressourcen

| Ressource | Besitz und Freigabe |
| --- | --- |
| `FileStream` und sein Datei-Handle | `FileAnalysisService` öffnet und schließt den Stream. Die Strategien leihen ihn nur aus und laufen nacheinander. Der Aufrufer darf ihn während einer Prüfung weder schließen noch gleichzeitig anderweitig verwenden. |
| `WINTRUST_FILE_INFO`, dessen UTF-16-Pfad und `WINTRUST_SIGNATURE_SETTINGS` | `WindowsAuthenticodeInspector` reserviert die Eingabeblöcke. Im `finally` räumt `DestroyStructure` den marshalierten Pfad auf; `FreeHGlobal` gibt die selbst reservierten Blöcke frei. |
| WinTrust-Prüfzustand und daraus geliehene Zeiger | Windows besitzt die Unterzeichner- und Zertifikatsstrukturen. Nach jeder Signatur schließt `WTD_STATEACTION_CLOSE` den Prüfzustand, auch nach einer fehlgeschlagenen Prüfung. Bis dahin werden benötigte Werte in verwaltete Strings, Bytearrays und Modelle kopiert. |
| Versionspuffer und Zeiger aus `VerQueryValueW` | Der von Windows gelieferte Puffer wird nach dem Kopieren mit `LocalFree` freigegeben, auch wenn ein fehlgeschlagener Aufruf Speicher geliefert hat. Zeiger auf einzelne Felder gehören zum Gesamtpuffer und werden nicht einzeln freigegeben. |
| Geladene `version.dll` für den zwischengespeicherten Delegate | Eine Bibliotheksreferenz bleibt für die Prozesslebensdauer erhalten. Fehlt der Export oder scheitert die Einrichtung des Delegates, wird diese Referenz wieder freigegeben. |
| Shell-Elementliste (PIDL) für den Explorer-Aufruf | `SHParseDisplayName` liefert den Speicher. `FileShellService` gibt ihn im `finally` mit `Marshal.FreeCoTaskMem` frei und gleicht erfolgreiche COM-Initialisierung auf demselben Thread mit `CoUninitialize` aus. |

`WINTRUST_FILE_INFO` enthält einen rohen Handle-Wert. `GC.KeepAlive(stream)` hält deshalb den verwalteten Besitzer bis nach dem nativen Aufruf und dem Aufräumen erreichbar; gegen ein ausdrückliches, gleichzeitiges `Dispose` durch einen anderen Aufrufer schützt dies nicht. Die Lebensdauer des WinTrust-Prüfzustands und die des Datei-Handles sind getrennt. Siehe [WINTRUST_DATA: Prüfzustand schließen](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/ns-wintrust-wintrust_data) und [GetFileVersionInfoByHandle: Pufferfreigabe](https://learn.microsoft.com/en-us/windows/win32/menurc/getfileversioninfobyhandle).

#### Datenlayout, Rückgabewerte und Abbruch

Die Interop-Strukturen verwenden `LayoutKind.Sequential` und die native Standardausrichtung. `DWORD` und Windows-`BOOL` bleiben 32 Bit breit, Handles und Zeiger folgen der Prozessarchitektur. `FILETIME` wird durch zwei `DWORD`-Felder abgebildet, damit seine Ausrichtung erhalten bleibt. Die Strukturgrößen und wichtige Feldabstände werden in `CoreChecks` kontrolliert. Vor dem Kopieren von Versionsstrings werden Zeiger und Längen gegen den zurückgegebenen Puffer geprüft; bei `VerQueryValueW` zählen Stringlängen UTF-16-Zeichen, die Länge der Übersetzungstabelle dagegen Bytes. Siehe [VerQueryValueW](https://learn.microsoft.com/en-us/windows/win32/api/winver/nf-winver-verqueryvaluew).

`WinVerifyTrust` liefert seinen Prüfstatus unmittelbar als `LONG`; ausschließlich `0` bedeutet Erfolg. Der Code verwendet dafür weder `GetLastError` noch einen allgemeinen HRESULT-Erfolgstest. Bei der Versionsabfrage und den Dateizeigerfunktionen gilt dagegen der BOOL-/LastError-Vertrag: Ein Fehlercode wird direkt nach dem fehlgeschlagenen Aufruf gesichert. Erwartete Fehler optionaler Metadaten werden als Diagnose zurückgegeben, während bereits berechnete Hashes erhalten bleiben. Siehe [WinVerifyTrust: Rückgabewert](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/nf-wintrust-winverifytrust).

Native Prüfaufrufe laufen synchron. Abbruchwünsche werden an den vorgesehenen Grenzen vor bzw. nach diesen Aufrufen ausgewertet; ein laufender Windows-Aufruf wird nicht gewaltsam unterbrochen. Die Freigabe eigener Speicherblöcke, das Schließen des Prüfzustands und die Wiederherstellung der Position nach Versionsabfragen liegen deshalb in `finally`-Blöcken.

#### Signaturmetadaten und Online-Sperrprüfung

`WINTRUST_SIGNATURE_SETTINGS` wählt die eingebetteten Signaturen einzeln aus. Die erste Prüfung ermittelt zusätzlich die Anzahl weiterer Signaturen; jede Prüfung erhält einen eigenen WinTrust-Zustand. Das Auslesen eines Zertifikats hebt einen negativen Prüfstatus nicht auf. `AuthenticodeTimestampReader` verarbeitet ausschließlich kopierte ASN.1-Metadaten; die Vertrauensentscheidung stammt weiterhin aus der Windows-Prüfung.

Die Sperrprüfung bleibt in beiden Modi aktiv. Für `OfflineCache` setzt der Inspector zusätzlich `WTD_CACHE_ONLY_URL_RETRIEVAL` (`0x1000`). Im Online-Modus entfällt dieses Flag: Windows darf Netzwerkzugriffe ausführen und weiterhin gültige Cache-Daten verwenden. Die gesetzten Provider-Flags prüfen die Zertifikatskette ohne Stammzertifikat und schließen MD2-/MD4-Signaturen aus. Siehe [WINTRUST_DATA: Provider-Flags](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/ns-wintrust-wintrust_data).

#### Versionsressourcen

`IFilePropertiesInspector` erhält denselben `FileStream` wie die Prüfsummen- und Signaturprüfung. `WindowsFileVersionReader` kapselt `GetFileVersionInfoByHandle`, die Auswertung des zurückgegebenen Puffers und dessen Freigabe. Die Auswahl der Sprach-/Codepage-Tabelle und die MUI-Flags folgen der bisherigen .NET-Abfrage. Normale Dateien ohne Versionsressourcen bleiben ohne Metadatenfehler. Native Lesefehler erhalten eine Diagnose; sie lösen keine zweite Pfad-Abfrage aus. Auf anderen Betriebssystemen oder bei fehlendem Windows-Export bleibt `FileVersionInfo.GetVersionInfo(path)` als Kompatibilitätslösung erhalten.

Bei NE-Dateien kann Windows den nativen Dateizeiger verschieben, obwohl `FileStream.Position` unverändert bleibt. Beim Ausleihen über `SafeFileHandle` synchronisiert .NET zunächst den nativen Dateizeiger mit der verwalteten Position. Der Inspector sichert diesen synchronisierten Zustand und stellt beide Positionen nach der Versionsabfrage wieder her, auch beim Abbruch oder einem Lesefehler. Der geliehene Stream bleibt geöffnet.

#### Dateityp und COM-Initialisierung

Für die Dateitypbezeichnung kombiniert `ReadWindowsType` `SHGFI_TYPENAME` mit `SHGFI_USEFILEATTRIBUTES` und übergibt nur die Endung sowie `FILE_ATTRIBUTE_NORMAL`. Damit wird die registrierte Zuordnung abgefragt; der Aufruf benötigt weder die Quelldatei noch ein Dateisymbol. Die COM-Initialisierung gilt für den aufrufenden Thread. Sowohl `S_OK` als auch `S_FALSE` werden durch `CoUninitialize` ausgeglichen. Bei `RPC_E_CHANGED_MODE` nutzt der Code den vorhandenen COM-Kontext, ohne eine eigene erfolgreiche Initialisierung zu behaupten oder diesen fremden Kontext herunterzufahren. Siehe [SHGetFileInfoW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow) und [CoInitializeEx](https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-coinitializeex).

#### Dateikontextmenü und Explorer

Die Dateieigenschaften werden mit `SHObjectProperties` (`SHOP_FILEPATH`) auf dem WPF-Thread und mit dem Hauptfenster als Besitzer geöffnet. Die Pfadauflösung für den Explorer läuft dagegen zusammen mit der Auswahl auf einem COM-initialisierten Hintergrundthread. Bei `SHOpenFolderAndSelectItems` bedeutet `cidl = 0`, dass die PIDL die auszuwählende Datei bezeichnet; Windows öffnet deren übergeordneten Ordner. Damit entfällt das Zusammensetzen einer Explorer-Befehlszeile. Die Importe verwenden `LibraryImport`, UTF-16-Pfade und `System32`-Suchpfade; `IntPtr` bleibt für x64 und ARM64 passend. Siehe [SHObjectProperties](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shobjectproperties), [SHParseDisplayName](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shparsedisplayname) und [SHOpenFolderAndSelectItems](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shopenfolderandselectitems).

### Splashscreen und Bildressourcen

`Resources/FileScopeSplash.png` ist die unveränderte 256-Pixel-PNG-Variante aus dem vorhandenen App-Icon; dessen Vorlage ist `docs/Oranges FS-App-Symbol.png`. Die Build-Aktion `SplashScreen` bettet das Bild ein und lässt WPF den frühen Aufruf `Show(true)` vor `App.InitializeComponent()` erzeugen. Es braucht weder eine lose PNG-Datei neben der EXE noch einen eigenen Startup-Thread. Die automatische Ausblendung übernimmt WPF; Verzögerungen vor dem verwalteten Programmeinstieg kann dieser Splash noch nicht überbrücken.

Icon und Splash lassen sich aus derselben Vorlage erneuern:

```powershell
.\docs\build-icon.ps1 -InputPng '.\docs\Oranges FS-App-Symbol.png' -OutputIco '.\src\FileScope.App\Resources\FileScope.ico' -OutputSplash '.\src\FileScope.App\Resources\FileScopeSplash.png'
```

### Sprachressourcen

Alle Oberflächentexte stehen in `src/FileScope.App/Resources`:

| Datei | Sprache |
| --- | --- |
| `Strings.resx` | Englisch, Fallback |
| `Strings.de.resx` | Deutsch |
| `Strings.zh-Hans.resx` | Vereinfachtes Chinesisch |

`LocExtension` bindet XAML-Texte an den `LocalizationService`. Status- und Fehlercodes bleiben in der Kernbibliothek sprachneutral. Vorhandene Zeilen und Filterauswahlen werden beim Umschalten aktualisiert. Technische Ausnahmebeschreibungen von Betriebssystem/.NET und originale Zertifikatsfelder bleiben unverändert. Der Start verwendet die Windows-UI-Sprache, sofern unterstützt, sonst Englisch.

### Verwendete Entwurfsmuster

| Entwurfsmuster | Umsetzung in FileScope | Zweck |
| --- | --- | --- |
| **Strategy** | `IChecksumStrategy`, `ISignatureInspector`, `IFilePropertiesInspector`, `IPeHeaderInspector` sowie `IResultExportStrategy` mit `CsvExportStrategy` und `JsonExportStrategy` | Analyse- und Exportverfahren lassen sich hinter gemeinsamen Verträgen austauschen. Die Orchestrierung muss ihre Einzelheiten nicht kennen. |
| **Factory (einfache Fabrik)** | `IChecksumStrategyFactory` / `ChecksumStrategyFactory.Create` | Erzeugt für den gewählten Hashalgorithmus eine neue, passend konfigurierte Strategie. Der zustandsbehaftete Hash wird nicht zwischen Dateien geteilt. |
| **Command** | `RelayCommand` und `AsyncRelayCommand` implementieren `ICommand` | Buttons, Tastenkürzel und Drag-and-drop lösen ViewModel-Aktionen aus. Ausführbarkeit und Schutz vor parallelem Mehrfachstart liegen im Command. |
| **Observer** | `INotifyPropertyChanged`, `ObservableCollection` und Ereignisse wie `CultureChanged`, `ColumnOrderChanged` und `SignatureSelectionChanged` | Oberfläche und abhängige Komponenten reagieren auf Zustandsänderungen. Auch die Diagnoseanzeige wird aus den Zeilenereignissen aktualisiert. |
| **Adapter** | `WindowsAuthenticodeInspector`, `FilePropertiesInspector`, `FilePickerService` und `ConfirmationService` | Übersetzen Windows-/WPF-Aufrufe und deren Ergebnisse in die von der Anwendung verwendeten Schnittstellen und Modelle. |
| **Fassade** | `IFileAnalysisService` / `FileAnalysisService.AnalyzeAsync` | Bietet einen gemeinsamen Einstieg für Dateizugriff, Hashberechnung, Metadaten und Signaturprüfung und führt deren Ergebnisse zusammen. |

Ergänzend strukturieren **MVVM** als Architekturmuster und **Dependency Injection über Konstruktoren** die Anwendung. `App.OnStartup` bildet den **Composition Root**, an dem die konkreten Komponenten zusammengestellt werden; die Tests können dort eingesetzte Dienste durch Testimplementierungen ersetzen.

## Erweitern

### Code

**Weiterer Hash:** `ChecksumAlgorithm` erweitern, die Factory um eine passende `IChecksumStrategy` ergänzen und eine Option/Spalte im App-Projekt hinzufügen. `StreamChecksumService` muss dabei nicht geändert werden.

**Andere Signaturprüfung:** `ISignatureInspector` implementieren und im Composition Root austauschen oder eine zusammengesetzte Strategie einfügen. Neue, dauerhaft benötigte Resultatfelder gehören in die Core-Modelle.

**Weitere Sprache:** Gleichnamige Schlüssel in `Strings.<culture>.resx` ergänzen, die Sprache in `LocalizationService.Languages` aufnehmen und gegebenenfalls die Startsprachauswahl erweitern.

### Überprüfung

```powershell
.\Verify.ps1
```

Das Skript baut die Projektmappe und führt Core-, Formatierungs-/Einstellungs- sowie unter Windows WPF/MVVM-Prüfungen aus. Es erstellt dafür temporäre Test-Harness-Projekte im ignorierten `artifacts/verify`-Ordner und entfernt diese danach. **Die eigentliche Projektmappe bleibt bei zwei Projekten.** Die Tests benötigen keine Testframework-NuGet-Pakete.

Die UI-Prüfungen laden echtes WPF-XAML und arbeiten mit den tatsächlichen ViewModels, Bindungen und dem Dispatcher auf einem STA-Thread. Injizierte Testdienste liefern kontrollierte Analyseergebnisse und Dialogantworten. So lassen sich auch Abbruch, Sprachwechsel und Spaltensichtbarkeit automatisiert prüfen.

Dokumentierter Prüfumfang einschließlich Durchlaufstatistik, Theme-Umschaltung, Infodialog und Dateikontextmenü; gezählt werden einzelne Prüfbedingungen (Assertions):

| Gruppe | Anzahl und Ausführungsstand |
| --- | --- |
| Core | **472** im zuletzt dokumentierten Windows-Lauf; unveränderter Prüfumfang. Hier **183** unter Linux bestanden, native Windows-Prüfungen übersprungen. |
| Präsentation und Einstellungen | **165** bestanden, einschließlich 18 neuer Theme-Prüfungen. |
| WPF / UI / MVVM | **332** erwartet, einschließlich 27 neuer Prüfungen für das Dateikontextmenü und einer weiteren Theme-Prüfung; Testprogramm fehlerfrei kompiliert. Erneuter Windows-Lauf noch offen. |

Die UI-Anzahl ergibt sich aus den zuletzt unter Windows bestätigten 223 Prüfungen, 32 Ergänzungen für Diagnoseanzeige und Durchlaufstatistik, 31 Theme-Prüfungen, 19 Prüfungen für den Infodialog und 27 Prüfungen für das Dateikontextmenü. Diese verwenden auch den echten WPF-Dienst, geöffnete Fenster und die tatsächlichen Bedienelemente. Beim Infodialog werden unter anderem der modale Aufruf, Version und Offline-Lizenz, drei Sprachen, beide Designs und Fehler beim Browseraufruf geprüft. Das Kontextmenü wird mit echten Menüeinträgen und einem Testdienst für die Shell geprüft, einschließlich Zeilenzuordnung, Mehrfachauswahl, Sonderzeichen in Pfaden, Fehlern und verspäteten Antworten. Die Core-Anzahl kann abhängig von verfügbaren nativen Funktionen, Link-Berechtigungen und zusätzlichen Testdateien variieren. Die Zahlen werden bei Änderungen an den Prüfungen mitgeführt; der detaillierte Ausführungsstand steht in `VALIDATION.md`.

Die Badge-Prüfung ermittelt nach jedem Designwechsel die aktuell angezeigte Analysezelle derselben Ergebniszeile. So hängt sie nicht von der Lebensdauer einer durch WPF möglicherweise neu erzeugten Zellansicht ab. Die Anzahl der Prüfungen bleibt dabei unverändert.

**Optional:** Zusätzlich mit einer bekannten Datei, die tatsächlich eine eingebettete Authenticode-Signatur besitzt:

#### Signatur

```powershell
.\Verify.ps1 -SignedFile 'C:\Pfad\zur\signierten-datei.exe'
```

Der optionale Test liest diese Datei und prüft, dass Unterzeichnerdaten sowie der native Prüfrückgabecode ermittelt werden. Er verändert und startet sie nicht. Vertrauenswürdigkeit wird nicht pauschal angenommen, weil sie vom Rechner, den Stammzertifikaten und Sperrinformationen abhängt.

#### Version

`Verify.ps1` vergleicht unter Windows die neue Abfrage mit `FileVersionInfo`, einschließlich Sprache, PE-/NE-Ressourcen und fehlenden Versionsdaten. Die Prüfungen verwenden absichtlich einen nicht existierenden Dateinamen, um einen unbemerkten Rückgriff auf diesen Pfad auszuschließen, und kontrollieren beide Dateipositionen sowie den nächsten Lesezugriff. Eigene Dateien können zusätzlich angegeben werden:

```powershell
.\Verify.ps1 -VersionFile "D:\Testdateien\VBRUN300.DLL", "D:\Testdateien\DMSGEW.EXE"
```

Der tatsächliche Ausführungsstand der mitgelieferten Version steht in `VALIDATION.md`. Ein zusätzlicher statischer Strukturcheck kann optional mit `python tools/check_structure.py` ausgeführt werden. Er ersetzt keinen .NET-Build.

### Code-Stil

Die `.editorconfig` ist nach Themen und Regel-IDs geordnet; jede Einstellung trägt einen kurzen Kommentar. [Einstellungen und Erklärungen nebeneinander](docs/EDITORCONFIG.md). Die engeren Testausnahmen stehen in `tests/.editorconfig`.

### Technische Referenzen

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

NE-Layoutreferenzen: [Open Watcom: exeos2.h](https://github.com/open-watcom/open-watcom-v2/blob/master/bld/watcom/h/exeos2.h), [Wine: IMAGE_OS2_HEADER](https://github.com/wine-mirror/wine/blob/master/include/winnt.h).
