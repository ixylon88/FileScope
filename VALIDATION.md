# Prüfstand der ausgelieferten Quellen

Stand: 4. November 2026.

## Hier tatsächlich ausgeführt

| Prüfung | Ergebnis |
| --- | --- |
| XML-Parser für XAML, RESX, Projektdateien und Manifest | Bestanden |
| Zwei Projekte in der Projektmappe, existierende Referenzpfade | Bestanden |
| Keine WPF-/App-Abhängigkeit der Kernbibliothek | Bestanden |
| Drei vollständige Ressourcensätze mit jeweils 93 Schlüsseln | Bestanden |
| Identische Formatplatzhalter in allen Sprachen | Bestanden |
| Verwendete XAML-Ressourcenschlüssel und dynamische Statusschlüssel | Bestanden |
| Einfache Binding-Wurzeln und ausgeglichene C#-Klammern | Bestanden; dies ist kein C#-Compiler |
| Fünf `abc`-Prüfsummen-Testkonstanten, unabhängig mit Python hashlib verglichen | Bestanden |
| Feldreihenfolge und Strukturgrößen von WINTRUST_DATA, WINTRUST_FILE_INFO und WINTRUST_SIGNATURE_SETTINGS für 32/64 Bit | Bestanden, mit unabhängig nachgebildeten ctypes-Layouts |
| Identische Schlüssel in beiden Theme-Paletten und vollständige dynamische Farbverweise | Bestanden |
| Textkontrast der definierten Palettenpaare (Normaltext, Sekundärtext, Auswahl, Überschriften und Status) | Mindestens 4,5:1 in beiden Themes; keine vollständige Barrierefreiheitsprüfung |
| Release-Build beider Projekte mit .NET SDK 10.0.100 unter Linux, inklusive WPF-XAML-Kompilierung | Bestanden, 0 Warnungen / 0 Fehler |
| `CoreChecks.cs` ausgeführt mit .NET 10 unter Linux | 35 Prüfungen bestanden; Windows-spezifische Prüfungen übersprungen |
| WPF-Testprogramm mit `UiChecks.cs` kompiliert | Bestanden, 0 Warnungen / 0 Fehler; nicht ausgeführt |

`python tools/check_structure.py` reproduziert die statischen Strukturprüfungen. Diese untersuchen die Quelldateien; sie führen die C#-Implementierung nicht aus.

## Noch nicht ausgeführt

Die Umgebung ist Linux. **WPF-Laufzeittests, echte native Windows-Signaturaufrufe, visuelle Kontrolle und das PowerShell-Skript `Verify.ps1` wurden hier nicht ausgeführt.** Die C#-Tests wurden über entsprechende temporäre Harness-Projekte gebaut; die Core-Prüfungen wurden ausgeführt. Es liegt keine getestete Windows-Binärausgabe bei.

Der erfolgreiche Build ersetzt keine Abnahme der Darstellung und Bedienung unter Windows.

## Mitgelieferte ausführbare Prüfungen

Nach Installation des .NET 10 SDK auf Windows:

```powershell
.\Verify.ps1
```

Das Skript baut zuerst beide Produktionsprojekte. Anschließend kompiliert und startet es temporäre Testprogramme mit den Dateien aus `tests`:

| Testdatei | Abgedeckte Fälle |
| --- | --- |
| `CoreChecks.cs` | Fünf bekannte Hashwerte, leere Datei, ein gemeinsamer Lesedurchlauf, nicht-seekbarer Stream, mehrere Puffer, Abbruch vor/während des Lesens, Ressourcenfreigabe bei Fehlern, unveränderliche Optionen, Unicode-Pfade, Dateigröße/-datum, Metadatenmodus, fehlende Datei, Ordner, Dateisperre unter Windows, native ABI-Größen und unsignierte PE-Datei. |
| `UiChecks.cs` | Reales XAML initialisieren, Sprachressourcen binden, Dateiliste deduplizieren, Analyseergebnisse übertragen, Text-/Status-/Signaturfilter, vorhandene Zeilen übersetzen, mit neuen Optionen erneut analysieren, Clear/Cancel während blockierender Verarbeitung, verspätete Fortschrittsmeldungen verwerfen. Die Ansicht muss während Änderungsereignissen lesbar bleiben; Einzel-/Mehrfach-Drop, Dateiauswahl bei aktiver Sortierung/Filterung, ausgefilterte neue Dateien und erneutes Ablegen nach Clear. Zusätzlich: tatsächliche DataGrid-Spaltensichtbarkeit für jede Hashoption und alle Zertifikatsfelder, lokalisierte Theme-Namen, Umschalten über die ComboBox, Fluent-Modus und dynamische Farben, Zeilenhintergründe/Auswahl nach Theme-Wechsel, Sortieren und Filtern; bestehende Ergebnisse bleiben erhalten. |

Die automatisierten UI-Tests laufen auf einem STA-Thread mit WPF-Dispatcher und echten ViewModels. Sie initialisieren die View ohne ein sichtbares Fenster. Sie ersetzen weder einen physischen Drag-and-drop-Test aus Explorer noch eine visuelle Prüfung.

Mit `-SignedFile 'C:\Pfad\zur\signierten-datei.exe'` kann zusätzlich eine bekannte eingebettet signierte Datei geprüft werden. Dieser Test verwendet standardmäßig den lokalen Cache und setzt keine pauschale Vertrauenswürdigkeit voraus.

## Sinnvolle manuelle Abnahme unter Windows

1. App starten und Dateien aus Explorer auf freien Fensterbereich, Suchfeld und DataGrid ziehen. Neue Zeilen müssen jeweils erscheinen; während eines laufenden Durchlaufs wird eine weitere Ablage abgelehnt.
2. Alle Hashoptionen auswählen und eine Datei mit exakt den drei ASCII-Bytes `abc` prüfen. Erwartetes SHA-256: `BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD`.
3. Eine große Datei bearbeiten, abbrechen und erneut analysieren. Dann während eines Durchlaufs „Leeren“ drücken: nach Abschluss des Abbruchs müssen alle Zeilen verschwunden bleiben.
4. Mehrere Zeilen laden, nach Name/Größe/Datum sortieren und Filter kombinieren. Die zweite, vierte usw. sichtbare Zeile muss grau erscheinen.
5. Englisch, Deutsch und 简体中文 durchschalten, auch mit vorhandenen Ergebnissen. Überschriften, Filter und Status sollen sich ändern; Hashwerte und originale Zertifikatsangaben bleiben gleich.
6. Eine bekannte eingebettet signierte Datei prüfen und Unterzeichner/Fingerabdruck mit den Windows-Dateieigenschaften vergleichen. Offline kann „Sperrstatus unbekannt“ korrekt sein. Mit Online-Sperrprüfung erneut prüfen.
7. Eine Kopie einer bekannten signierten Datei in einem für die Signatur relevanten Datenbereich verändern und prüfen, dass kein gültiges Ergebnis erscheint. Ein gültiger Authenticode-Zeitstempel darf dagegen ein inzwischen abgelaufenes Unterzeichnerzertifikat weiterhin legitimieren.
8. Darstellung bei 100 %, 150 % und 200 % Skalierung sowie allen drei Sprachen kontrollieren; lange Hashwerte und Pfade über horizontales Scrollen, Tooltip oder Detailfeld lesen/kopieren.
9. Ohne Dateien sowie mit vorhandenen Ergebnissen jede Hashoption und die Signaturprüfung ab-/anwählen. Die zugehörigen Spalten müssen sofort verschwinden/erscheinen. Name, Analysestatus, Größe, Datum, Pfad und Diagnose bleiben sichtbar. Bereits ermittelte Werte bleiben beim Ausblenden erhalten; neue Berechnungen über „Alle neu prüfen“ starten.
10. Direkt unter der Sprache mehrfach Hell/Dunkel wechseln, auch während einer Analyse und mit aktiven Filtern. Fenster, Menüband, Eingabefelder, Tabelle, Status und Detailfeld müssen lesbar bleiben; Ergebnisse und Optionen bleiben erhalten. Zeilen per Maus und Tastatur auswählen, Spalten sortieren/verschieben/vergrößern und horizontal scrollen; die ersten zwei Spalten bleiben fixiert.

Für unternehmenseigene Vertrauensrichtlinien, besondere Signaturformate und sehr große Dateimengen sind zusätzliche reale Testdateien sinnvoll.
