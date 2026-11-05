# Prüfstand der ausgelieferten Quellen

Stand: 5. November 2026. Patchbasis: `48e8e4ec92b04157e720a7a34a4da675d69543c8` (`test fix`).

Aktuelle Erweiterung: rekursive Ordnersuche, CSV-Export aller geladenen Zeilen und gespeicherte Exportfeldauswahl. Core (132) und Präsentation/Einstellungen (122) wurden ausgeführt; das erweiterte WPF-Testprogramm wurde kompiliert. Der [vorige Review der Analyzer-/Dark-Commits](docs/REVIEW-2026-11-04.md) bleibt erhalten. Windows-Laufzeittests stehen weiterhin aus.

## Hier tatsächlich ausgeführt

| Prüfung | Ergebnis |
| --- | --- |
| Statische Prüfung: XML, Ressourcen, Binding-Wurzeln, Projektstruktur, Spalten, Workspace-Tasks und Test-Theme | 995 Prüfungen bestanden |
| Drei Ressourcensätze mit jeweils 163 Schlüsseln; gleiche Formatplatzhalter | Bestanden |
| Genau zwei Produktionsprojekte; Kernbibliothek ohne WPF-Abhängigkeit | Bestanden |
| Alle 39 Spalten in XAML und Optionskatalog; begrenzte automatische Breiten | Bestanden |
| Workspace-JSON: drei eindeutige PowerShell-Tasks, existierende Skripte, Workspace-Pfade und win-x64-Ziel | Bestanden, statisch |
| Release-Build beider Projekte mit .NET SDK 10.0.100 unter Linux, einschließlich WPF-XAML | Bestanden, 0 Warnungen / 0 Fehler |
| `CoreChecks.cs` mit .NET 10 unter Linux | 132 Prüfungen bestanden; native Windows-Aufrufe übersprungen |
| `PresentationChecks.cs` mit .NET 10 unter Linux | 122 Prüfungen bestanden |
| Native Strukturen einschließlich SHFILEINFOW: Marshal-Größen und vorhandene kritische Offsets | x64 zur Laufzeit geprüft; x86/ARM64 nicht ausgeführt |
| WPF-Testprogramm mit `UiChecks.cs` kompiliert | Bestanden, 0 Warnungen / 0 Fehler; nicht ausgeführt |
| Self-contained Publish für win-x64, Trimming aus | Beim vorherigen Metadaten-Patch geprüft; für diese Erweiterung nicht erneut ausgeführt |

Der PE-Prüfer wurde mit eigens kodierten PE32-/PE32+-Dateien für i386, Alpha/Alpha64, ARM/ARMNT, AMD64, ARM64/ARM64EC/ARM64X und einen unbekannten Maschinentyp ausgeführt. Weitere Fälle: Dateien ohne PE-Header, DOS/NE, beschädigte PE-Header, AnyCPU, 32-Bit-Pflicht/-Präferenz, DLL-Flags, gemischter/nativer CLR-Code, Erhalt der Streamposition und begrenztes Lesen. Eine echte gebaute .NET-DLL wurde auf PE-/CLR-Header und Versionsinformationen geprüft. Metadatenfehler und Abbruch wurden im vollständigen Analyseablauf geprüft.

`python tools/check_structure.py` reproduziert die statischen Prüfungen, führt aber keine C#-Implementierung aus. Die C#-Prüfungen wurden über temporäre Harness-Projekte entsprechend `Verify.ps1` gebaut und gestartet. Die lokalen Builds verwendeten `-m:1 -p:UseSharedCompilation=false`, um die Build-Prozesse dieser Umgebung zu isolieren.

Die neuen ausführbaren Prüfungen umfassen Unterordner, Unicode- und versteckte Dateien, leere/fehlende Ordner, Abbruch und einen symbolischen Verzeichnislink zurück zum übergeordneten Ordner. CSV-Tests prüfen UTF-8-BOM, Kommas, Anführungszeichen, Zeilenumbrüche, unveränderte Hashwerte, leere Felder, ungültige Zeilenbreiten, vollständiges Ersetzen sowie Erhalt einer bestehenden Zieldatei bei Fehler und Abbruch. Alle 39 Spalten werden projiziert; Exportauswahl, Reihenfolge, Sprache, Hashformat, Architektur und ausgewählte Signatur werden geprüft. Auch leere Exportauswahl, JSON-Wiederherstellung und unbekannte/doppelte gespeicherte Feld-IDs sind abgedeckt.

Die zusätzlichen WPF-Prüfungen für Ribbon-Bindungen, Options-Häkchen, rekursives Hinzufügen mit Duplikaterkennung, Export trotz aktivem Filter, Schutz geladener Quelldateien, Dialogabbruch, Fehlerbehandlung und Cancel/Clear während Suche/Export/Hinzufügen wurden nur kompiliert.

## Noch nicht ausgeführt

Die Umgebung ist Linux. **Die PowerShell-/VS-Code-Tasks selbst, WPF-Laufzeittests, Ordner-/Speicherdialoge, die visuelle Kontrolle sowie die nativen Windows-Dateityp- und Signaturaufrufe wurden hier nicht ausgeführt.** Der frühere Publish wurde auf Windows nicht gestartet; für diese Erweiterung wurde kein neuer Publish erstellt. Ausgeliefert wird der Quellpatch; eine auf Windows getestete Binärausgabe liegt nicht bei.

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
