# Build, Verifikation und Release-Plan

## Gemeinsame Skripte für lokale Entwicklung und CI

`Build.ps1` und `Verify.ps1` sind die gemeinsamen Einstiegspunkte. Die CI
ruft diese Skripte mit denselben Parametern wie die lokale Entwicklung
auf. Es gibt keinen Schalter, der auf dem Build-Server andere App-Eigenschaften
aktiviert. `build/ScriptSupport.ps1` bündelt Phasenfarben, SDK-Ausgabe,
Exitcode-Prüfung und den Build. Ein normaler Aufruf von `Build.ps1` baut die ganze Solution seriell (`-m:1`). Mit `-Application FileScope` oder `-Application DuplicateFinder` baut das Skript das gewählte WPF-Projekt samt Referenzen. Die Darstellung orientiert sich an den
TinyCamServer-Skripten; Visual-Studio-Pfade, COM-Registrierung und deren
projektspezifische Schritte werden nicht übernommen.

Die ANSI-Sequenzen stehen in `build/colored_console.ps1`. Jede farbige Zeile
endet mit einem Reset (`ESC[0m`), bevor `Write-Host` den Zeilenumbruch ausgibt.
Auch mehrzeilige Meldungen werden zeilenweise eingefärbt und zurückgesetzt,
damit Hintergrundfarben nicht in die nächste Zeile weiterlaufen.

```powershell
# Auf Windows x64
.\Verify.ps1 -Runtime win-x64
.\Build.ps1 -Publish -Runtime win-x64
.\Build.ps1 -Publish -Runtime win-x64 -SelfContained

# Publish auch auf Windows x64 möglich; Verify benötigt Windows ARM64.
.\Verify.ps1 -Runtime win-arm64
.\Build.ps1 -Publish -Runtime win-arm64
.\Build.ps1 -Publish -Runtime win-arm64 -SelfContained
```

Für DuplicateFinder erhalten Build, Start, Verify und Publish denselben Schalter. Die Pakete liegen je nach gewählter App unter `artifacts/publish/FileScope/` oder `artifacts/publish/DuplicateFinder/`. Darunter erhält jede Kombination aus Architektur und Bereitstellungsart ihren eigenen Ordner.

```powershell
.\Build.ps1 -Application DuplicateFinder -Run -Runtime win-x64
.\Verify.ps1 -Application DuplicateFinder -Runtime win-x64
.\Build.ps1 -Application DuplicateFinder -Publish -Runtime win-arm64 -SelfContained
```

`-NoColor` bzw. die Umgebungsvariable `NO_COLOR` deaktiviert die Farben der
Skriptmeldungen. Externe Programme steuern ihre eigene Ausgabe. `-Verbosity`
regelt die .NET-Ausführlichkeit. Alle Meldungen behalten lesbare Phasennamen;
Fehler werden nicht in Erfolgsmeldungen umgewandelt. Die Skripte wechseln für
SDK-Auswahl und Build ins Repository und stellen das vorherige Arbeitsverzeichnis
auch nach einem Fehler wieder her. Externe Beispieldateien für Verify werden
vorher relativ zum aufrufenden Verzeichnis aufgelöst.

## MSTest in Visual Studio und Konsole

Die Solution enthält `FileScope.Core.Tests`, `FileScope.Presentation.Tests`,
`FileScope.Ui.Tests`, `DuplicateFinder.Tests` und `DuplicateFinder.Ui.Tests`. Alle verwenden `MSTest.Sdk`; dessen Version und der
Test-Runner `Microsoft.Testing.Platform` sind in `global.json` festgelegt.
Das SDK liefert Framework, Adapter und Runner. Das Erweiterungsprofil bleibt
bei `None`; nur die Ausgabe von TRX-Berichten wird zusätzlich aktiviert.
Eine separate Paketversionsliste für die einzelnen MSTest-Bausteine entfällt.

**Warum MSTest?** Die Integration in Visual Studio, `dotnet test` und CI sowie
die gemeinsame SDK-Konfiguration halten die Testinfrastruktur überschaubar.
Unter Windows bietet das Framework außerdem eingebaute STA-Unterstützung über
`STATestMethod` und `STATestClass`, ohne zusätzliches STA-Testpaket
([Microsoft-Dokumentation](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-writing-tests-controlling-execution#threading-attributes)).
Die vorhandenen UI-Tests verwenden diese Attribute allerdings nicht:
`WpfTestHost` stellt den STA-Thread selbst bereit, um eine gemeinsame
WPF-`Application` samt Dispatcher, Ressourcen und Aufräumen zu verwalten.

Die Projekte erscheinen nach Restore/Build im **Test-Explorer** von Visual
Studio mit .NET-10-Unterstützung. Dort lassen sich Szenarien einzeln ausführen
und debuggen. Release-Testprojekte behalten dafür portable Debugsymbole.
Dieselben Testmethoden laufen auf Windows auch direkt in der Konsole:

```powershell
# Alle C#-Tests, ohne RID an der Solution.
dotnet test --solution FileScope.sln -c Release

# Ein Szenario, beispielsweise bei der Fehlersuche.
dotnet test --project tests/FileScope.Ui.Tests/FileScope.Ui.Tests.csproj --filter "FullyQualifiedName~HorizontalScrolling"

# Ein Testprojekt mit festgelegter Architektur, auf einem passenden Windows-Host.
dotnet test --project tests/FileScope.Core.Tests/FileScope.Core.Tests.csproj -c Release --runtime win-arm64 -p:Platform=Arm64
```

`Verify.ps1` führt zuerst die gemeinsamen Skriptprüfungen aus. Standardmäßig baut es die Solution und startet die fünf Testprojekte mit `dotnet test`. Bei Auswahl einer App baut es deren Projekt samt Referenzen und startet die drei FileScope- oder die zwei DuplicateFinder-Testprojekte. `-SkipBuild` überspringt den vorangestellten Build. Die Testaufrufe bauen weiterhin ihre Projekte und Referenzen. `-SkipUi` überspringt die jeweils ausgewählten WPF-Tests. Ohne expliziten
Windows-RID laufen unter Linux die Core- und Präsentationstests; ein vollständiger
Solution-Testlauf einschließlich WPF benötigt Windows.

Innerhalb eines Testprojekts laufen die Szenarien seriell. Die WPF-Gruppe nutzt
eine gemeinsame `Application` mit einem eigenen STA-Thread und Dispatcher.
Vor jedem Szenario werden Ressourcen und Sprache initialisiert; danach werden
auch bei Fehlern die verbliebenen Fenster geschlossen und der Ausgangszustand
wiederhergestellt. Kein Szenario setzt voraus, dass ein anderes zuerst lief.
Der Host startet seine Nachrichtenschleife mit `Application.Run()`, damit
`Application.Shutdown()` sie beim Assembly-Cleanup auch beendet. Das Warten
auf den Shutdown-Aufruf und anschließend auf das Threadende ist jeweils auf
zehn Sekunden begrenzt; ein Fehler hält dadurch nicht den letzten Test endlos offen.
Die Präsentationstests binden weiterhin nur die UI-unabhängigen Quelldateien
ein; dafür wird kein WPF-Fenster benötigt.

### Build, Konsistenz und Verhalten

Die Prüfungen ergänzen sich und laufen lokal sowie in der CI über `Verify.ps1`:

| Prüfung | Aufgabe |
| --- | --- |
| .NET-Build | C# und XAML kompilieren, Projektverweise und Ressourcen verarbeiten. |
| Statische MSTest-Prüfungen | Übersetzungsschlüssel und Formatplatzhalter, XAML-Ressourcenverweise, Theme-Paletten, Spaltenkatalog, Darstellungsgrundregeln und das eingebettete Startbild auf Konsistenz prüfen. |
| MSTest-Verhaltenstests | Den C#-Code ausführen und Ergebnisse prüfen, einschließlich echter WPF-Fenster, Bindungen, Benutzeraktionen und nativer Windows-Aufrufe. |
| PowerShell-Skripttests | VS-Code-Aufgaben und die Build-/Test-/Publish-Aufrufe einschließlich Fehlerbehandlung prüfen. |

Die statischen Prüfungen liegen als `ResourceConsistencyTests` im bestehenden
Präsentations-Testprojekt und erscheinen damit auch im Visual-Studio-Test-Explorer
und in den TRX-Berichten. Sie prüfen die kompilierten Sprachressourcen ohne
Rückgriff auf die Ausgangssprache. XAML, App-Projektdatei und Startbild werden
als Prüfmaterial in die Testassembly eingebettet; ein bestimmtes Arbeitsverzeichnis
oder ein danebenliegender Checkout ist zur Testausführung nicht erforderlich.
Die App erhält dadurch keine zusätzlichen Ressourcen oder Abhängigkeiten.

Das bisherige `tools/check_structure.py` und der separate Python-Schritt in der
Pipeline entfallen. C#-Klammerprüfungen und einfache Suchen nach Bindungsnamen
werden durch Compiler und WPF-Verhaltenstests abgedeckt; die bekannten Hashwerte
prüfen bereits die Core-Tests. Starre Vorgaben zur Projektanzahl und eine
pauschale Textsuche nach `TODO` werden nicht übernommen. Python wird weder für
die App noch für Build und Tests benötigt. Die früheren Zahlen im Prüfverlauf
beziehen sich weiterhin auf den damaligen Python-Check.

### Optionale Beispieldateien und gemeinsame Einstellungen

Die zusätzlichen Dateien sind dieselben wie bei den optionalen
`Verify.ps1`-Aufrufen in der README. Die Beschreibung der jeweiligen Prüfung
bleibt dort; die Übergabe an Visual Studio und `dotnet test` erfolgt über
`TestRunParameters` in einer `.runsettings`-Datei:

| Prüfung und Beispiel in der README | Parameter von `Verify.ps1` | Name unter `TestRunParameters` |
| --- | --- | --- |
| [Signatur und Unterzeichnerdaten](../README.md#signatur) | `-SignedFile` | `SignedFile`: ein Dateipfad |
| [Versionsinformationen und Dateipositionen](../README.md#version) | `-VersionFile` | `VersionFiles`: ein JSON-Array von Dateipfaden |

`tests/FileScope.runsettings` enthält leere Vorgaben für zusätzliche
Signatur- und Versionsdateien. Ohne Beispieldatei meldet MSTest den betreffenden
optionalen Test als übersprungen. Betriebssystemspezifische Tests werden auf
anderen Betriebssystemen ebenfalls ausdrücklich übersprungen.
Ohne ausgewählte `.runsettings`-Datei fehlen diese Parameter im `TestContext`;
das wird genauso wie eine leere Vorgabe behandelt. Zum normalen Ausführen im
Test-Explorer muss daher keine Einstellungsdatei ausgewählt werden.

Für eigene Dateien eine lokale Kopie `tests/FileScope.local.runsettings`
anlegen; `*.local.runsettings` ist von Git ausgeschlossen. Die Parameter
verwenden **absolute Pfade**, damit Arbeitsverzeichnisse von Visual Studio,
Konsole und CI keinen Unterschied machen. `VersionFiles` enthält ein JSON-Array;
im XML werden dessen Anführungszeichen maskiert:

```xml
<RunSettings>
  <TestRunParameters>
    <Parameter name="SignedFile" value="D:/Fixtures/signed.exe" />
    <Parameter name="VersionFiles" value="[&quot;D:/Fixtures/VBRUN300.DLL&quot;,&quot;D:/Fixtures/DMSGEW.EXE&quot;]" />
  </TestRunParameters>
</RunSettings>
```

In Visual Studio diese Datei unter **Test → Configure Run Settings → Select
Solution Wide runsettings File** auswählen. Es gibt bewusst keine feste
`RunSettingsFilePath`-Projektvorgabe, die diese Auswahl überschreiben würde.
In der Konsole dieselbe Datei angeben:

```powershell
.\Verify.ps1 -Settings .\tests\FileScope.local.runsettings
dotnet test --solution FileScope.sln --settings tests/FileScope.local.runsettings

# Die bisherigen Kurzformen bleiben erhalten; sie dürfen auch relative Pfade verwenden.
.\Verify.ps1 -SignedFile 'D:\Fixtures\signed.exe'
.\Verify.ps1 -VersionFile 'D:\Fixtures\VBRUN300.DLL', 'D:\Fixtures\DMSGEW.EXE'
```

`-SignedFile` und `-VersionFile` überschreiben nur den jeweiligen Parameter in
einer temporären Kopie der gewählten Einstellungen. Die Quelldatei bleibt
unverändert; die Kopie wird auch nach Fehlern entfernt. Ohne `-Settings` verwendet
Verify die gemeinsame Datei im Repository.

### Testergebnisse

Verify schreibt je Testprojekt einen TRX-Bericht nach
`artifacts/test-results/<Lauf-ID>/`. Diese Berichte bleiben auch nach einem
Fehlschlag erhalten. Die CI lädt vorhandene Berichte für jede Architektur als
eigenes Artefakt hoch. Scheitert bereits Restore oder Build, kann für das
betreffende Projekt noch kein Testbericht vorliegen.

MSTest zählt **Testfälle**, nicht einzelne Assertions innerhalb eines Szenarios.
Datenzeilen zählen als eigene Fälle. Die früheren Assertion-Zahlen bleiben im
Prüfverlauf in `VALIDATION.md` erhalten und sind nicht direkt vergleichbar.

### Übersprungene Tests und Visual-Studio-Meldungen

Im Windows-Lauf ohne zusätzliche Beispieldateien haben die drei übersprungenen
Tests folgende Bedeutung:

| Test | Grund für `skipped` / `Assert.Inconclusive` |
| --- | --- |
| `OptionalSignedFileExposesSignerMetadata` | `SignedFile` fehlt oder ist leer; keine eigene signierte Beispieldatei angegeben. |
| `OptionalVersionFilesPreservePositions` | `VersionFiles` fehlt oder enthält keine Dateipfade; keine eigenen Versionsdateien angegeben. |
| `NonWindowsSignatureInspectionIsExplicit` | Prüft das Verhalten außerhalb von Windows und wird deshalb auf Windows übersprungen. |

Für die ersten beiden Tests lassen sich die Dateien wie unter
[Optionale Beispieldateien und gemeinsame Einstellungen](#optionale-beispieldateien-und-gemeinsame-einstellungen)
angeben. Der dritte Skip ist auf Windows weiterhin beabsichtigt. Auf anderen
Betriebssystemen werden stattdessen Tests übersprungen, die native Windows-APIs
benötigen. Übersprungen bedeutet, dass ein Test nicht ausgeführt wurde; es ist
weder ein bestandener noch ein fehlgeschlagener Test.

Visual Studio kann außerdem Warnungen zu nicht unterstützten Abschnitten in
den Runsettings wie `BoostTestInternalSettings`, `Python` und
`GoogleTestAdapterSettings` ausgeben. Die mitgelieferte
[`FileScope.runsettings`](../tests/FileScope.runsettings) enthält diese
Abschnitte nicht. Sie betreffen andere Testadapter; MSTest auf
Microsoft.Testing.Platform ignoriert sie ausdrücklich. Tauchen sie trotz
unveränderter Projektdateien auf, liegt die Herkunft vermutlich in der lokalen
Visual-Studio-Testumgebung oder einer zusätzlich ausgewählten Runsettings-Datei.
Aus der Warnung allein lässt sich die verursachende Erweiterung nicht bestimmen.
Für die FileScope-Tests ist wegen dieser Meldungen keine Codeänderung nötig.
Ein pauschales Unterdrücken aller Warnungen würde auch relevante Hinweise
verbergen.

Das Visual-Studio-Ausgabefenster sammelt mehrere Läufe. Für einen vollständigen
neuen Lauf ist dessen abschließendes `Test run finished` mit der erwarteten
Testanzahl maßgeblich; ältere Fehler und Abbrüche können darüber stehen bleiben.
`Aborting test discovery because a test run was requested` bedeutet, dass die
Testsuche zugunsten eines angeforderten Testlaufs beendet wurde. Ein
`Test run aborted` bezeichnet dagegen einen unvollständigen Testlauf und ist
kein Erfolgsnachweis. Die aktuelle Anzahl und die bestätigten Ergebnisse stehen
in [README → Überprüfung](../README.md#überprüfung) und
[`VALIDATION.md`](../VALIDATION.md).

Microsoft-Referenzen:
[MSTest-SDK](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-sdk),
[MTP ausführen und debuggen](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-run-and-debug),
[Runsettings in Visual Studio](https://learn.microsoft.com/en-us/visualstudio/test/configure-unit-tests-by-using-a-dot-runsettings-file),
[MSTest: Warnungen zu nicht unterstützten Runsettings-Abschnitten](https://github.com/microsoft/testfx/blob/v4.5.3/src/Adapter/MSTest.TestAdapter/TestingPlatformAdapter/MSTestRunSettings.cs).

## SDK, Runtime und Reproduzierbarkeit

- `global.json` legt die vollständige **SDK-Version** mit `rollForward: disable`
  fest. Maßgeblich ist die dort eingetragene Version; ein fehlendes SDK wird
  gemeldet und nicht automatisch durch eine andere Version ersetzt. Weitere SDKs
  können parallel installiert sein. Eine Pipeline muss diese Datei auch bei der
  SDK-Installation auswerten.
- Für self-contained wird `TargetLatestRuntimePatch=true` verwendet. Das SDK
  bestimmt den ihm bekannten Runtime-Patch; es wird kein beliebiger neuer Patch
  zur Laufzeit des Skripts gesucht. Die Runtime wird mit der App ausgeliefert.
  Ein anderer installierter Runtime-Patch auf dem Zielrechner ersetzt sie nicht.
- Framework-dependent verlangt eine passende installierte .NET 10 Desktop
  Runtime derselben Architektur. Die Runtime-Auswahl beim Start kann auf einen
  neueren installierten Patch weiterrollen. Ein identisches App-Paket garantiert
  daher nicht auf allen Rechnern einen identischen WPF-Runtime-Stand.
- Jede Anwendung erhält eigene Ausgabeordner für ihre vier Publish-Varianten. Vor einem Publish
  wird nur dessen eigener Ordner geleert; übrige Varianten bleiben erhalten.
- Beim Lösungsbuild wählt der Skriptparameter `-Runtime` ausschließlich die
  passende `Platform` (x64, Arm64 oder x86). Ein `--runtime` am Lösungsbuild
  ist nicht zulässig (NETSDK1134). Normale Build-Ausgaben sind bereits durch die
  Plattform getrennt, etwa `bin/Arm64/Release/net10.0-windows`. `Build.ps1 -Run`
  verwendet dieselbe Plattform ohne RID und startet diesen vorhandenen Build.
  Publish und die einzelnen Verify-Testprojekte erhalten dagegen einen RID;
  ihre Build-Ausgaben enthalten zusätzlich das RID-Unterverzeichnis. Ohne
  `-Runtime` baut Verify die Solution mit `Any CPU`. Gleichzeitige Builds im
  selben Checkout werden nicht unterstützt: NuGet-Zwischendateien in `obj`
  werden weiterhin gemeinsam verwendet. CI-Matrixjobs benötigen eigene Checkouts.

Gleiche Skripte und ein festgelegtes SDK machen Builds nachvollziehbar, sind aber
noch keine Zusage byteidentischer ZIPs. Dafür müssten zusätzlich Commit/Tag,
Paketauflösung und Paketquellen, Build-Eigenschaften, Werkzeug-/Runner-Versionen,
Quellpfade und Archivmetadaten kontrolliert werden. NuGet-Lockdateien und
`--locked-mode` sind bei Einführung der Release-Pipeline zu prüfen. Derzeit ist
kein Build-Zeitstempel als Assembly-Attribut eingebunden; die Copyright-Jahreszahl
in `Directory.Build.props` hängt allerdings vom Buildjahr ab. Signierung und
ZIP-Zeitstempel können spätere Paketvergleiche ebenfalls beeinflussen.

SDK-Patches werden bewusst aktualisiert und verifiziert. Eine feste Version
bedeutet keine dauerhafte Sperre gegen Updates. `latestPatch` wäre eine mögliche
Lockerung für lokale Entwicklung, würde aber wieder unterschiedliche installierte
SDK-Patchstände zulassen.

Microsoft-Referenzen:
[global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json),
[Runtime-Patchauswahl](https://learn.microsoft.com/en-us/dotnet/core/deploying/runtime-patch-selection),
[Bereitstellungsvarianten](https://learn.microsoft.com/en-us/dotnet/core/deploying/),
[SDK-Fehler einschließlich NETSDK1134](https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/).

## GitHub-CI

`.github/workflows/ci.yml` läuft bei Pushes und Pull Requests für `dev`, `main`
und `master` sowie über `workflow_dispatch`. Zwei voneinander getrennte Jobs
prüfen die unterstützten Architekturen:

| Runtime | GitHub-Runner | SDK-Architektur |
| --- | --- | --- |
| `win-x64` | `windows-2025` | `x64` |
| `win-arm64` | `windows-11-arm` | `arm64` |

Jeder Job installiert das SDK aus `global.json` und führt
`Verify.ps1 -Runtime <Runtime> -NoColor` einschließlich der Ressourcenprüfungen
aus. Es wird kein `-SkipUi`
gesetzt: Core, Präsentation, Skripte und WPF laufen auf der jeweiligen nativen
Windows-Architektur. Ein fehlgeschlagener Job bricht den anderen nicht ab;
dadurch bleiben beide Ergebnisse verfügbar. Ein neuer Lauf desselben Branches
oder Pull Requests ersetzt dagegen einen noch laufenden älteren Lauf.

Erst nach erfolgreichem Verify veröffentlicht `Build.ps1` nacheinander
framework-dependent und self-contained. Die Upload-Schritte archivieren jeweils
den vollständigen Publish-Ordner einschließlich Lizenz und Sprachressourcen.
Pro App entstehen vier Artefakte. Ihre Namen beginnen mit `FileScope-` oder `DuplicateFinder-`, gefolgt von Runtime und Bereitstellungsart. Sie bleiben 14 Tage verfügbar. Ein fehlender
Publish-Ordner ist ein Fehler. Die Pipeline erzeugt noch keinen GitHub-Release
und prüft das sichtbare Erscheinungsbild der veröffentlichten EXEs nicht.

Der Workflow benötigt nur `contents: read`, keine zusätzlichen Secrets und
keine Schreibrechte auf Branches oder Releases. Die verwendeten GitHub-Actions
sind auf vollständige Commit-SHAs festgelegt; der jeweilige Versionsname steht
daneben im Kommentar. Workflow-Anpassungen ändern keine lokalen Buildparameter.

Der erste Lauf startet nach dem Push der Workflowdatei auf einen der genannten
Branches. Sobald sie auf dem Standardbranch liegt, steht auch der manuelle
Start unter **Actions → CI → Run workflow** zur Verfügung. Bei Fehlern zeigt
der Schritt **Build and verify, including WPF** die genaue fehlgeschlagene
Prüfbedingung. Die Scroll-Prüfung meldet Vorlage, fixierte Spalten, Virtualisierung
und Breiten getrennt; abweichende Breiten enthalten Spalten-ID und Breitenmodus.

GitHub-Referenzen:
[Runner-Auswahl](https://docs.github.com/en/actions/how-tos/write-workflows/choose-where-workflows-run/choose-the-runner-for-a-job),
[SDK aus global.json](https://github.com/actions/setup-dotnet#using-the-global-json-file-input).

## Geplant: Tagbasierte Releases

Dieser Abschnitt hält den vereinbarten Vorschlag für die separate
Release-Pipeline fest. Sie ist noch nicht implementiert.

1. `release.yml`: Ein Versions-Tag wie `v1.0.0` oder `v1.1.0-rc.1` löst die
   Prüfung genau dieses Commits aus. Anschließend erzeugt `Build.ps1` vier ZIPs je Anwendung für
   x64 und ARM64, jeweils framework-dependent und self-contained. SHA-256-Prüfsummen
   gehören zu den Release-Dateien.
2. Der Vorschlag zur Versionsableitung bleibt **MinVer als reine
   Build-Abhängigkeit** mit `PrivateAssets=all` und Tagpräfix `v`. MinVer ist für
   Git-Tag-Trigger nicht erforderlich, kann aber Assembly-/Produktversion und
   Paketnamen aus demselben Tag ableiten. Dafür sind vollständige Git-Historie
   und Tags nötig; manuelle konkurrierende Versionsangaben wären zu entfernen.
3. Die veröffentlichten EXEs auf der passenden Windows-Architektur starten.
   `Verify.ps1` allein startet nicht die fertigen Publish-Pakete. Symbol, Splash
   sowie helles/dunkles Design sind zusätzlich sichtbar zu prüfen. Der erste
   Release kann dazu zunächst als Entwurf erstellt werden. Native ARM64-Tests
   laufen auf Windows ARM64, nicht auf einem x64-Runner.

Der gemeldete Theme-Unterschied zwischen self-contained und framework-dependent
ist noch nicht als Runtime-Fehler nachgewiesen. Die tatsächlich gebündelte bzw.
geladene WPF-Runtime ist bei einem Vergleich mitzuerfassen. Die geänderte
Patchauswahl allein gilt nicht als Beleg für eine Behebung.

## Später: WiX 7 mit Burn-Setup

Als weiterer Auslieferungsweg ist ein **Burn-Bundle** vorgesehen. Das Setup prüft
die passende **.NET 10 Desktop Runtime** für x64 bzw. ARM64 und installiert sie
bei Bedarf vor dem FileScope-MSI. Der SDK-Installer ist dafür nicht erforderlich.
Die Versionsprüfung muss zur benötigten Runtime und deren Roll-forward-Regeln
passen; ein bloßer Test auf irgendeine .NET-Installation genügt nicht.

Zwei Bundle-Varianten sind möglich:

| Variante | Runtime-Bereitstellung |
| --- | --- |
| Web-Setup | Bei fehlender passender Runtime den festgelegten Microsoft-Installer herunterladen und ausführen. |
| Offline-Setup | Denselben Runtime-Installer als Payload mitliefern; kein Download für diese Voraussetzung erforderlich. |

Bereits geeignete Runtime-Installationen werden verwendet. Beide Varianten sollen
dieselbe geprüfte Runtime-Version, Architektur und Integritätsprüfung verwenden.
Die vier ZIP-Varianten bleiben unabhängig davon verfügbar. Das vorhandene
TinyCamServer-Setup kann später als Ausgangspunkt dienen; FileScope benötigt keine
COM-Registrierung. Umsetzung und Bundle-Konfiguration folgen separat.

WiX-Referenzen:
[Burn-Bundles](https://docs.firegiant.com/wix/tools/burn/),
[.NET erkennen und installieren](https://docs.firegiant.com/wix/tools/wixext/dotnet/).
