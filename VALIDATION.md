# Prüfstand der ausgelieferten Quellen

Stand: 4. November 2026.

## Hier tatsächlich ausgeführt

| Prüfung | Ergebnis |
| --- | --- |
| XML-Parser für XAML, RESX, Projektdateien und Manifest | Bestanden |
| Zwei Projekte in der Projektmappe, existierende Referenzpfade | Bestanden |
| Keine WPF-/App-Abhängigkeit der Kernbibliothek | Bestanden |
| Drei vollständige Ressourcensätze mit jeweils 118 Schlüsseln | Bestanden |
| Identische Formatplatzhalter in allen Sprachen | Bestanden |
| Verwendete XAML-Ressourcenschlüssel und dynamische Statusschlüssel | Bestanden |
| Einfache Binding-Wurzeln und ausgeglichene C#-Klammern | Bestanden; dies ist kein C#-Compiler |
| Fünf `abc`-Prüfsummen-Testkonstanten, unabhängig mit Python hashlib verglichen | Bestanden |
| Feldreihenfolge und Strukturgrößen von WINTRUST_DATA, WINTRUST_FILE_INFO und WINTRUST_SIGNATURE_SETTINGS für 32/64 Bit | Bestanden, mit unabhängig nachgebildeten ctypes-Layouts |
| Übereinstimmung aller 24 Spalten-IDs zwischen Tabelle und Optionskatalog; begrenzte automatische Breiten | Bestanden |
| Theme-Umschalter, eigene ControlTemplates und Zeilenwechsel deaktiviert/entfernt | Statische Prüfung bestanden |
| Release-Build beider Projekte mit .NET SDK 10.0.100 unter Linux, inklusive WPF-XAML-Kompilierung | Bestanden, 0 Warnungen / 0 Fehler |
| `CoreChecks.cs` ausgeführt mit .NET 10 unter Linux | 55 Prüfungen bestanden; Windows-spezifische Prüfungen übersprungen |
| `PresentationChecks.cs` ausgeführt mit .NET 10 unter Linux | 83 Prüfungen bestanden |
| Neue native Strukturen CRYPT_PROVIDER_SGNR, CMSG_SIGNER_INFO und CRYPT_ATTRIBUTE: Größe und kritische Offsets | x64 zur Laufzeit geprüft; x86/ARM64 hier nicht ausgeführt |
| WPF-Testprogramm mit `UiChecks.cs` kompiliert | Bestanden, 0 Warnungen / 0 Fehler; nicht ausgeführt |

Für die Speicherung der Prüf-Häkchen wurden der Release-Build einschließlich WPF-Testprogramm, die Präsentations-/Einstellungsprüfungen und die statischen Strukturprüfungen erneut ausgeführt. Die unveränderten Core-Prüfungen wurden dabei nicht erneut gestartet; deren 55 Ergebnisse stammen aus dem vorherigen Signatur-/Fenster-Update.

`python tools/check_structure.py` reproduziert die statischen Strukturprüfungen. Diese untersuchen die Quelldateien; sie führen die C#-Implementierung nicht aus.

## Noch nicht ausgeführt

Die Umgebung ist Linux. **WPF-Laufzeittests, echte native Windows-Signaturaufrufe, visuelle Kontrolle und das PowerShell-Skript `Verify.ps1` wurden hier nicht ausgeführt.** Die C#-Tests wurden über entsprechende temporäre Harness-Projekte gebaut; die Core- sowie Formatierungs-/Einstellungsprüfungen wurden ausgeführt. Es liegt keine getestete Windows-Binärausgabe bei.

Der erfolgreiche Build ersetzt keine Abnahme der Darstellung und Bedienung unter Windows.

## Mitgelieferte ausführbare Prüfungen

Nach Installation des .NET 10 SDK auf Windows:

```powershell
.\Verify.ps1
```

Das Skript baut zuerst beide Produktionsprojekte. Anschließend kompiliert und startet es temporäre Testprogramme mit den Dateien aus `tests`:

| Testdatei | Abgedeckte Fälle |
| --- | --- |
| `CoreChecks.cs` | Fünf bekannte Hashwerte, leere Datei, ein gemeinsamer Lesedurchlauf, nicht-seekbarer Stream, mehrere Puffer, Abbruch vor/während des Lesens, Ressourcenfreigabe bei Fehlern, unveränderliche Optionen, Unicode-Pfade, Dateigröße/-datum, Metadatenmodus, fehlende Datei, Ordner, Dateisperre unter Windows, native ABI-Größen und unsignierte PE-Datei. ASN.1-Metadaten für klassische und RFC-3161-Zeitstempel; beschädigte, leere und mehrdeutige Daten; Trennung von ausgelesenem Datum und bestätigtem Zeitstempel; niemals aktuelle Prüfzeit als Ersatz. |
| `PresentationChecks.cs` | Bekannte Hex-/Base64-/Base64url-Vektoren inklusive führender Nullbytes und Padding; alle Digest-Längen; unveränderte Analysewerte und nicht-Hash-Felder; Details und Suche; Formatwechsel während/nach einer Analyse; Spaltenpermutationen und Grenzen; drei Sprachen; Speichern/Laden; fehlende, doppelte, unbekannte oder ungültige Einstellungen; beschädigte JSON-Datei und Schreibfehler. Mehrere Signaturen mit eigenen Zertifikaten/Status/Zeitstempeln; dasselbe Zertifikat in mehreren Signaturen; fehlende Metadaten ohne Übernahme aus einer anderen Signatur; Fenstermaße, Monitorgrenzen und Erhalt aller übrigen Einstellungen beim Speichern. Alle sieben Prüf-Häkchen über JSON speichern/laden; alle ausgeschaltet; fehlende einzelne Felder und ältere Einstellungen ohne Prüfauswahl. |
| `UiChecks.cs` | Reales XAML und Optionsfenster initialisieren, Sprachbindungen, Dateiaufnahme und Deduplizierung, Filter, Clear/Cancel und verspätete Fortschrittsmeldungen. Gespeicherte Prüf-Häkchen im Haupt-ViewModel wiederherstellen, keine Schreibvorgänge beim Laden, direktes Speichern bei Änderung, Spaltensichtbarkeit und Verwendung der Auswahl in der Analyse; Ausgrauen der Online-Sperrprüfung sowie Leeren erhalten die Auswahl. Spalten-Häkchen, vollständige Reihenfolge einschließlich ausgeblendeter Spalten über die Fixierungsgrenze, Formatwechsel über die ComboBox ohne Analyseaufruf, Filteraktualisierung, unveränderte Resultate sowie automatische Breiten innerhalb der Grenzen. Echte Signatur-ComboBox und bedingte Spaltensichtbarkeit; Suche/Statusfilter nach Auswahlwechsel; Reset und Leeren; Fenstermaße und Zustand über Schließen/Neustart einschließlich minimiertem Schließen. Scrollkorrektur am tatsächlichen Fluent-Knopf; gemeldete negative Bruchwerte und Sonderwerte durch eine echte Width-Bindung; fraktionale horizontale Scrollpositionen; aktive Binding-Fehlerprotokollierung sowie Erhalt von Vorlage, Virtualisierung, fixierten Spalten und automatischen Breiten. |

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

17. Eine gemischte Prüfauswahl setzen (einschließlich ausgeschaltetem SHA-256), Signaturprüfung und Online-Sperrprüfung ändern und die App neu starten. Alle sieben Häkchen und die zugehörige Spaltensichtbarkeit müssen wiederhergestellt werden. Dasselbe mit allen Häkchen aus sowie mit gewählter, aber ausgegrauter Online-Sperrprüfung testen. Anschließend Sprache, Hashformat, Spaltenreihenfolge und Fenstergröße ändern und erneut starten: Alle Bereiche müssen ihre Einstellungen behalten. „Leeren“ darf die Prüfauswahl nicht zurücksetzen.

Für unternehmenseigene Vertrauensrichtlinien, besondere Signaturformate und sehr große Dateimengen sind zusätzliche reale Testdateien sinnvoll.
