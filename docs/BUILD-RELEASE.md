# Build, Verifikation und Release-Plan

## Gemeinsame Skripte für lokale Entwicklung und CI

`Build.ps1` und `Verify.ps1` sind die gemeinsamen Einstiegspunkte. Die CI
ruft diese Skripte mit denselben Parametern wie die lokale Entwicklung
auf. Es gibt keinen Schalter, der auf dem Build-Server andere App-Eigenschaften
aktiviert. `build/ScriptSupport.ps1` bündelt Phasenfarben, SDK-Ausgabe,
Exitcode-Prüfung und den Lösungsbuild. Die zwei Projekte werden beim
Lösungsbuild seriell gebaut (`-m:1`); zusätzliche MSBuild-Arbeitsprozesse werden
dafür weder lokal noch in CI benötigt. Die Darstellung orientiert sich an den
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

`-NoColor` bzw. die Umgebungsvariable `NO_COLOR` deaktiviert die Farben der
Skriptmeldungen. Externe Programme steuern ihre eigene Ausgabe. `-Verbosity`
regelt die .NET-Ausführlichkeit. Alle Meldungen behalten lesbare Phasennamen;
Fehler werden nicht in Erfolgsmeldungen umgewandelt. Die Skripte wechseln für
SDK-Auswahl und Build ins Repository und stellen das vorherige Arbeitsverzeichnis
auch nach einem Fehler wieder her. Externe Beispieldateien für Verify werden
vorher relativ zum aufrufenden Verzeichnis aufgelöst.

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
- Alle vier Publish-Varianten erhalten eigene Ausgabeordner. Vor einem Publish
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

Jeder Job installiert das SDK aus `global.json`, prüft Struktur und Ressourcen
und führt `Verify.ps1 -Runtime <Runtime> -NoColor` aus. Es wird kein `-SkipUi`
gesetzt: Core, Präsentation, Skripte und WPF laufen auf der jeweiligen nativen
Windows-Architektur. Ein fehlgeschlagener Job bricht den anderen nicht ab;
dadurch bleiben beide Ergebnisse verfügbar. Ein neuer Lauf desselben Branches
oder Pull Requests ersetzt dagegen einen noch laufenden älteren Lauf.

Erst nach erfolgreichem Verify veröffentlicht `Build.ps1` nacheinander
framework-dependent und self-contained. Die Upload-Schritte archivieren jeweils
den vollständigen Publish-Ordner einschließlich Lizenz und Sprachressourcen.
Die vier Artefakte heißen `FileScope-<Runtime>-framework-dependent` bzw.
`FileScope-<Runtime>-self-contained` und bleiben 14 Tage verfügbar. Ein fehlender
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
   Prüfung genau dieses Commits aus. Anschließend erzeugt `Build.ps1` vier ZIPs:
   x64/ARM64, jeweils framework-dependent und self-contained. SHA-256-Prüfsummen
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
