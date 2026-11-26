# Exercise orchestration with an in-process dotnet stand-in and disposable files.
# No SDK, network access, application launch or test-framework package is needed.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path ([System.IO.Path]::GetTempPath()) ('FileScope & script checks ' + [Guid]::NewGuid().ToString('N'))
$script:CheckCount = 0

function Check {
    param([bool]$Condition, [string]$Label)
    if (-not $Condition) { throw "FAIL: $Label" }
    $script:CheckCount++
    Write-Host "PASS: $Label"
}

function Invoke-Scenario {
    param([string]$Script, [hashtable]$Parameters, [string]$FailAt = '')
    $scenarioState = [pscustomobject]@{
        Calls = [System.Collections.Generic.List[object]]::new()
        FailurePoint = $FailAt
    }
    function dotnet {
        $arguments = [string[]]$args
        $name = $arguments[0]
        $xml = $null
        if ($name -in @('run', 'test')) {
            $project = $arguments[[Array]::IndexOf($arguments, '--project') + 1]
            $name = [System.IO.Path]::GetFileNameWithoutExtension($project)
            $xml = [xml](Get-Content -LiteralPath $project -Raw)
        }
        $settingsXml = $null
        $settingsIndex = [Array]::IndexOf($arguments, '--settings')
        if ($settingsIndex -ge 0) { $settingsXml = [xml](Get-Content -LiteralPath $arguments[$settingsIndex + 1] -Raw) }

        # Simulate a report even for failed tests, so cleanup is checked on disk.
        $reportPath = ''
        $resultsIndex = [Array]::IndexOf($arguments, '--results-directory')
        $reportIndex = [Array]::IndexOf($arguments, '--report-trx-filename')
        if ($resultsIndex -ge 0 -and $reportIndex -ge 0) {
            $reportPath = Join-Path $arguments[$resultsIndex + 1] $arguments[$reportIndex + 1]
            [void](New-Item -ItemType Directory -Path (Split-Path $reportPath -Parent) -Force)
            Set-Content -LiteralPath $reportPath -Value '<TestRun />'
        }
        $scenarioState.Calls.Add([pscustomobject]@{ Name = $name; Arguments = $arguments; Project = $xml; Settings = $settingsXml; ReportPath = $reportPath })
        # Match the SDK restriction instead of accepting invalid solution calls.
        if ($name -eq 'build' -and $arguments[1] -like '*.sln' -and $arguments -contains '--runtime') {
            throw 'NETSDK1134: A solution build cannot specify a runtime identifier.'
        }
        $global:LASTEXITCODE = if ($name -eq $scenarioState.FailurePoint) { 23 } else { 0 }
        if ($arguments[0] -eq '--version') { 'test SDK' }
    }
    $before = (Get-Location).Path
    $errorMessage = ''
    $log = Join-Path $fixture 'scenario.log'
    try { & (Join-Path $fixture $Script) @Parameters -NoColor *> $log }
    catch { $errorMessage = $_.Exception.Message }
    [pscustomobject]@{
        Calls = $scenarioState.Calls.ToArray()
        Error = $errorMessage
        LocationRestored = (Get-Location).Path -eq $before
        Log = Get-Content -LiteralPath $log -Raw
    }
}

function Has-Option {
    param([object]$Call, [string]$Name, [string]$Value)
    $index = [Array]::IndexOf($Call.Arguments, $Name)
    return $index -ge 0 -and $index + 1 -lt $Call.Arguments.Count -and $Call.Arguments[$index + 1] -eq $Value
}

function Get-TaskRuntime {
    param([object]$Task)

    $index = [Array]::IndexOf([object[]]$Task.args, '-Runtime')
    if ($index -lt 0) { return 'host' }
    return $Task.args[$index + 1]
}

function Get-MessageLines {
    param([string]$Message, [string]$Kind = 'Info', [switch]$NoColor)
    $lines = [System.Collections.Generic.List[string]]::new()
    # Capture each host write before the host appends its newline.
    function Write-Host { param([string]$Object) $lines.Add($Object) }
    . (Join-Path $repository 'build/ScriptSupport.ps1') -NoColor:$NoColor
    Write-BuildMessage -Message $Message -Kind $Kind
    $lines.ToArray()
}

$oldOs = $env:OS
$oldNoColor = $env:NO_COLOR
$oldExitCode = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
$oldExitCodeValue = if ($null -ne $oldExitCode) { $oldExitCode.Value } else { 0 }
try {
    # Validate the actual editor entry points before testing disposable script copies.
    $workspace = Get-Content -LiteralPath (Join-Path $repository 'FileScope.code-workspace') -Raw | ConvertFrom-Json
    $tasks = @($workspace.tasks.tasks)
    $taskPrefix = '${workspaceFolder:FileScope}/'
    Check (@($workspace.folders | Where-Object { $_.name -eq 'FileScope' -and $_.path -eq '.' }).Count -eq 1) 'workspace tasks resolve the named repository folder'

    $invalidTasks = @($tasks | Where-Object {
        $fileIndex = [Array]::IndexOf([object[]]$_.args, '-File')
        if ($fileIndex -lt 0 -or $fileIndex + 1 -ge $_.args.Count) { return $true }

        $scriptPath = [string]$_.args[$fileIndex + 1]
        if (-not $scriptPath.StartsWith($taskPrefix, [StringComparison]::Ordinal)) { return $true }

        $scriptName = $scriptPath.Substring($taskPrefix.Length)
        $_.type -ne 'process' -or $_.command -ne 'powershell.exe' -or
            $_.options.cwd -ne '${workspaceFolder:FileScope}' -or
            $scriptName -notin @('Build.ps1', 'Verify.ps1') -or
            -not (Test-Path -LiteralPath (Join-Path $repository $scriptName) -PathType Leaf)
    })
    Check ($tasks.Count -gt 0 -and @($tasks.label | Select-Object -Unique).Count -eq $tasks.Count -and
        $invalidTasks.Count -eq 0) 'workspace tasks have unique labels and invoke existing scripts with the repository working directory'

    $expectedPublish = @('win-x64|False', 'win-x64|True', 'win-arm64|False', 'win-arm64|True')
    foreach ($product in @('FileScope', 'DuplicateFinder')) {
        $productTasks = @($tasks | Where-Object { $_.label -like "$($product): *" })
        $wrongSelector = @($productTasks | Where-Object {
            $index = [Array]::IndexOf([object[]]$_.args, '-Application')
            if ($product -eq 'FileScope') { return $index -ge 0 }
            return $index -lt 0 -or $_.args[$index + 1] -ne $product
        })
        Check ($productTasks.Count -gt 0 -and $wrongSelector.Count -eq 0) "$product tasks select the intended application scope"

        $publishTasks = @($productTasks | Where-Object { $_.args -contains ($taskPrefix + 'Build.ps1') -and $_.args -contains '-Publish' })
        $publishTargets = @($publishTasks | ForEach-Object {
            '{0}|{1}' -f (Get-TaskRuntime $_), ($_.args -contains '-SelfContained')
        })
        Check ($publishTargets.Count -eq 4 -and @(Compare-Object $expectedPublish $publishTargets).Count -eq 0) "$product publish tasks cover both architectures and deployment variants"

        $verifyTasks = @($productTasks | Where-Object { $_.args -contains ($taskPrefix + 'Verify.ps1') })
        $verifyTargets = @($verifyTasks | ForEach-Object { Get-TaskRuntime $_ })
        Check ($verifyTargets.Count -eq 3 -and @(Compare-Object @('host', 'win-x64', 'win-arm64') $verifyTargets).Count -eq 0) "$product verify tasks cover the host, x64 and ARM64"

        $buildTasks = @($productTasks | Where-Object { $_.args -contains ($taskPrefix + 'Build.ps1') -and
            $_.args -notcontains '-Publish' -and $_.args -notcontains '-Run' })
        Check ($buildTasks.Count -eq 1 -and $buildTasks[0].args -notcontains '-Runtime') "$product has a regular build task"
    }

    $runTasks = @($tasks | Where-Object { $_.args -contains '-Run' })
    $runTargets = @($runTasks | ForEach-Object { Get-TaskRuntime $_ })
    Check ($runTasks.Count -eq 2 -and @(Compare-Object @('win-x64', 'win-arm64') $runTargets).Count -eq 0 -and
        @($runTasks | Where-Object { $_.args -notcontains 'DuplicateFinder' -or $_.args -notcontains ($taskPrefix + 'Build.ps1') }).Count -eq 0) 'DuplicateFinder run tasks start the selected architecture'

    $defaults = @($tasks | Where-Object { $_.group -isnot [string] -and $_.group.isDefault })
    Check ($defaults.Count -eq 2 -and ($defaults | Where-Object { $_.group.kind -eq 'build' }).label -eq 'FileScope: Build (Release)' -and
        ($defaults | Where-Object { $_.group.kind -eq 'test' }).label -eq 'FileScope: Verify') 'default build and test tasks still cover the whole solution'

    [void](New-Item -ItemType Directory -Path (Join-Path $fixture 'build') -Force)
    [void](New-Item -ItemType Directory -Path (Join-Path $fixture 'tests') -Force)
    [void](New-Item -ItemType Directory -Path (Join-Path $fixture 'src/FileScope.App') -Force)
    foreach ($path in @('Build.ps1', 'Verify.ps1', 'global.json', 'build/ScriptSupport.ps1',
        'build/colored_console.ps1', 'src/FileScope.App/FileScope.App.csproj', 'tests/FileScope.runsettings',
        'tests/FileScope.Core.Tests/FileScope.Core.Tests.csproj',
        'tests/FileScope.Presentation.Tests/FileScope.Presentation.Tests.csproj',
        'tests/FileScope.Ui.Tests/FileScope.Ui.Tests.csproj',
        'src/DuplicateFinder.App/DuplicateFinder.App.csproj',
        'tests/DuplicateFinder.Tests/DuplicateFinder.Tests.csproj',
        'tests/DuplicateFinder.Ui.Tests/DuplicateFinder.Ui.Tests.csproj')) {
        [void](New-Item -ItemType Directory -Path (Split-Path (Join-Path $fixture $path) -Parent) -Force)
        Copy-Item -LiteralPath (Join-Path $repository $path) -Destination (Join-Path $fixture $path)
    }
    # Only the disposable copy bypasses platform execution; every dotnet call is fake.
    Add-Content -LiteralPath (Join-Path $fixture 'build/ScriptSupport.ps1') -Value "`nfunction Assert-RuntimeExecution { param([string]`$Runtime) }"
    Set-Content -LiteralPath (Join-Path $fixture 'tests/ScriptChecks.ps1') -Value '# Avoid recursive orchestration tests in this fixture.'
    $env:OS = 'Windows_NT'
    $preserved = Join-Path $fixture 'artifacts/verify/keep.txt'
    [void](New-Item -ItemType Directory -Path (Split-Path $preserved -Parent) -Force)
    Set-Content -LiteralPath $preserved -Value 'Preserve unrelated artifacts.'

    foreach ($product in @('FileScope', 'DuplicateFinder')) {
        $project = Join-Path $fixture "src/$product.App/$product.App.csproj"
        $publishRoot = Join-Path $fixture 'artifacts/publish'
        $otherRoot = Join-Path $publishRoot 'DuplicateFinder'
        if ($product -eq 'DuplicateFinder') {
            $otherRoot = $publishRoot
            $publishRoot = Join-Path $publishRoot 'DuplicateFinder'
        }

        foreach ($runtime in @('win-x64', 'win-arm64')) {
            foreach ($selfContained in @($false, $true)) {
                $name = if ($selfContained) { "${runtime}_self-contained" } else { $runtime }
                $output = Join-Path $publishRoot $name
                $stale = Join-Path $output 'stale.txt'
                $other = Join-Path (Join-Path $otherRoot $name) 'keep.txt'
                foreach ($path in @($stale, $other)) {
                    [void](New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force)
                    Set-Content -LiteralPath $path -Value 'Existing output.'
                }

                $parameters = @{ Publish = $true; Runtime = $runtime; SelfContained = $selfContained }
                if ($product -eq 'DuplicateFinder') { $parameters.Application = $product }
                $result = Invoke-Scenario 'Build.ps1' $parameters
                $publish = @($result.Calls | Where-Object Name -eq 'publish')
                Check ($result.Error -eq '' -and $publish.Count -eq 1 -and $publish[0].Arguments[1] -eq $project -and
                    (Has-Option $publish[0] '--output' $output) -and -not ($result.Calls | Where-Object Name -eq 'build')) "$product publishes its project to the selected directory: $name"

                $call = $publish[0]
                Check ((Has-Option $call '--runtime' $runtime) -and (Has-Option $call '--self-contained' $selfContained.ToString().ToLowerInvariant()) -and
                    $call.Arguments -contains '-p:PublishTrimmed=false' -and
                    (($call.Arguments -contains '-p:TargetLatestRuntimePatch=true') -eq $selfContained)) "$product publishes the requested deployment: $name"
                Check (-not (Test-Path -LiteralPath $stale) -and (Test-Path -LiteralPath $other) -and
                    (Test-Path -LiteralPath $preserved) -and $result.LocationRestored) "$product cleans only its selected publish directory: $name"
            }
        }
    }

    foreach ($runtime in @('win-x64', 'win-arm64')) {
        $result = Invoke-Scenario 'Build.ps1' @{ Runtime = $runtime; Configuration = 'Debug'; Run = $true }
        $build = @($result.Calls | Where-Object Name -eq 'build')
        $run = @($result.Calls | Where-Object Name -eq 'FileScope.App')
        $platform = if ($runtime -eq 'win-arm64') { 'Arm64' } else { 'x64' }
        Check ($result.Error -eq '' -and $build.Count -eq 1 -and $run.Count -eq 1 -and
            $build[0].Arguments[1] -eq (Join-Path $fixture 'FileScope.sln') -and
            (Has-Option $build[0] '--configuration' 'Debug') -and (Has-Option $run[0] '--configuration' 'Debug') -and
            $build[0].Arguments -contains "-p:Platform=$platform" -and $run[0].Arguments -contains "-p:Platform=$platform") "normal build and run select the same platform/configuration: $runtime"
        Check ($build[0].Arguments -notcontains '--runtime' -and $run[0].Arguments -notcontains '--runtime' -and
            $run[0].Arguments -contains '--no-build') "run reuses the solution output without a different RID directory: $runtime"
    }

    foreach ($product in @('FileScope', 'DuplicateFinder')) {
        $project = Join-Path $fixture "src/$product.App/$product.App.csproj"
        $result = Invoke-Scenario 'Build.ps1' @{ Application = $product.ToLowerInvariant(); Run = $true; Runtime = 'win-arm64' }
        $commands = @($result.Calls | Where-Object { $_.Arguments[0] -in @('build', 'run') })
        Check ($result.Error -eq '' -and $commands.Count -eq 2 -and $commands[0].Arguments[1] -ceq $project -and
            (Has-Option $commands[1] '--project' $project) -and $commands[1].Arguments -contains '--no-build' -and
            @($commands | Where-Object { $_.Arguments -contains '--runtime' -or $_.Arguments -notcontains '-p:Platform=Arm64' }).Count -eq 0) "$product builds and runs the same project and platform"

        $expectedTests = if ($product -eq 'FileScope') { @('FileScope.Core.Tests', 'FileScope.Presentation.Tests', 'FileScope.Ui.Tests') }
            else { @('DuplicateFinder.Tests', 'DuplicateFinder.Ui.Tests') }
        $result = Invoke-Scenario 'Verify.ps1' @{ Application = $product.ToLowerInvariant() }
        $build = @($result.Calls | Where-Object Name -eq 'build')
        $suites = @($result.Calls | Where-Object { $_.Arguments[0] -eq 'test' })
        Check ($result.Error -eq '' -and $build.Count -eq 1 -and $build[0].Arguments[1] -ceq $project -and
            $build[0].Arguments -contains '-p:Platform=AnyCPU' -and ($suites.Name -join ',') -eq ($expectedTests -join ',')) "$product verification builds its app and runs its own suites"

        $result = Invoke-Scenario 'Verify.ps1' @{ Application = $product; Runtime = 'win-arm64'; SkipUi = $true; SkipBuild = $true }
        $portable = @($expectedTests | Where-Object { $_ -notlike '*.Ui.Tests' })
        $suites = @($result.Calls | Where-Object { $_.Arguments[0] -eq 'test' })
        Check ($result.Error -eq '' -and -not ($result.Calls | Where-Object Name -eq 'build') -and
            ($suites.Name -join ',') -eq ($portable -join ',') -and
            @($suites | Where-Object { -not (Has-Option $_ '--runtime' 'win-arm64') }).Count -eq 0) "$product verification respects the requested runtime and skip switches"
    }

    foreach ($runtime in @('win-x64', 'win-arm64')) {
        $result = Invoke-Scenario 'Verify.ps1' @{ Runtime = $runtime; Configuration = 'Debug' }
        $work = @($result.Calls | Where-Object { $_.Name -in @('build', 'FileScope.Core.Tests', 'FileScope.Presentation.Tests', 'FileScope.Ui.Tests', 'DuplicateFinder.Tests', 'DuplicateFinder.Ui.Tests') })
        $platform = if ($runtime -eq 'win-arm64') { 'Arm64' } else { 'x64' }
        Check ($result.Error -eq '' -and $work.Count -eq 6 -and
            @($work | Where-Object { -not (Has-Option $_ '--configuration' 'Debug') -or
                $_.Arguments -notcontains "-p:Platform=$platform" }).Count -eq 0) "build and all five test processes use the same architecture/configuration: $runtime"
        $build = $work | Where-Object Name -eq 'build'
        $testProjects = @($work | Where-Object Name -ne 'build')
        Check ($build.Arguments[1] -eq (Join-Path $fixture 'FileScope.sln') -and $build.Arguments -notcontains '--runtime' -and
            @($testProjects | Where-Object { -not (Has-Option $_ '--runtime' $runtime) }).Count -eq 0) "verify uses runtime identifiers only for individual test projects: $runtime"
        $ui = $work | Where-Object Name -eq 'FileScope.Ui.Tests'
        Check ($ui.Project.Project.PropertyGroup.UseWPF -eq 'true') "UI test project still uses actual WPF: $runtime"
        Check (@($testProjects | Where-Object { $_.Arguments[0] -ne 'test' -or
            $_.Arguments -notcontains '--report-trx' -or $_.Arguments -notcontains '--no-ansi' -or
            -not (Has-Option $_ '--settings' (Join-Path $fixture 'tests/FileScope.runsettings')) }).Count -eq 0) "all suites use MSTest, shared settings and persistent reports: $runtime"
        Check (@(Get-ChildItem -LiteralPath (Split-Path $preserved -Parent)).Count -eq 1 -and $result.LocationRestored -and
            @($testProjects | Where-Object { -not (Test-Path -LiteralPath $_.ReportPath) }).Count -eq 0) "verify preserves unrelated artifacts and completed test reports: $runtime"
    }

    $result = Invoke-Scenario 'Verify.ps1' @{ Application = 'All' }
    $build = @($result.Calls | Where-Object Name -eq 'build')
    $suites = @($result.Calls | Where-Object { $_.Arguments[0] -eq 'test' })
    Check ($result.Error -eq '' -and $build.Count -eq 1 -and $build[0].Arguments[1] -eq (Join-Path $fixture 'FileScope.sln') -and
        $build[0].Arguments -contains '-p:Platform=Any CPU' -and $suites.Count -eq 5 -and
        @($suites | Where-Object { $_.Arguments -contains '--runtime' -or $_.Arguments -notcontains '-p:Platform=AnyCPU' }).Count -eq 0) 'host verification builds the Any CPU solution and runs all five projects without a RID'

    $result = Invoke-Scenario 'Verify.ps1' @{ SkipBuild = $true; SkipUi = $true }
    Check ($result.Error -eq '' -and @($result.Calls | Where-Object { $_.Name -in @('FileScope.Core.Tests', 'FileScope.Presentation.Tests', 'DuplicateFinder.Tests') }).Count -eq 3 -and
        @($result.Calls | Where-Object { $_.Name -in @('build', 'FileScope.Ui.Tests', 'DuplicateFinder.Ui.Tests') }).Count -eq 0) 'skip switches keep the requested non-UI tests'
    Check (-not $result.Log.Contains([string][char]27) -and $result.Log.Contains('[SUCCESS]')) 'plain console output retains readable phase/result labels'

    $caller = Join-Path $fixture 'external caller'
    [void](New-Item -ItemType Directory -Path $caller -Force)
    Push-Location $caller
    try {
        Set-Content -LiteralPath 'sample & version.exe' -Value 'test input'
        $result = Invoke-Scenario 'Verify.ps1' @{ SignedFile = './sample & version.exe'; VersionFile = @('./sample & version.exe'); SkipBuild = $true }
        $core = $result.Calls | Where-Object Name -eq 'FileScope.Core.Tests'
        $expected = Join-Path $caller 'sample & version.exe'
        $parameters = $core.Settings.RunSettings.TestRunParameters.Parameter
        $signedValue = ($parameters | Where-Object name -eq 'SignedFile').value
        $versionValues = @(($parameters | Where-Object name -eq 'VersionFiles').value | ConvertFrom-Json)
        Check ($result.Error -eq '' -and $signedValue -eq $expected -and $versionValues.Count -eq 1 -and $versionValues[0] -eq $expected -and
            ($parameters | Where-Object name -eq 'VersionFiles').value.StartsWith('[')) 'sample paths with spaces and XML-sensitive characters survive runsettings forwarding'
        Check ((Get-Content -LiteralPath (Join-Path $fixture 'tests/FileScope.runsettings') -Raw) -eq
            (Get-Content -LiteralPath (Join-Path $repository 'tests/FileScope.runsettings') -Raw)) 'fixture overrides never modify the shared settings file'

        # A custom configuration is the same input in Visual Studio and Verify.
        $customSettings = '<RunSettings><TestRunParameters><Parameter name="SignedFile" value="configured.exe" /><Parameter name="VersionFiles" value="[]" /></TestRunParameters></RunSettings>'
        Set-Content -LiteralPath 'custom.runsettings' -Value $customSettings
        $result = Invoke-Scenario 'Verify.ps1' @{ Settings = './custom.runsettings'; SkipBuild = $true }
        $core = $result.Calls | Where-Object Name -eq 'FileScope.Core.Tests'
        Check ($result.Error -eq '' -and (Has-Option $core '--settings' (Join-Path $caller 'custom.runsettings')) -and
            ($core.Settings.RunSettings.TestRunParameters.Parameter | Where-Object name -eq 'SignedFile').value -eq 'configured.exe') 'custom runsettings are forwarded unchanged from the caller directory'

        $result = Invoke-Scenario 'Verify.ps1' @{ Settings = './custom.runsettings'; SignedFile = './sample & version.exe'; SkipBuild = $true } 'FileScope.Core.Tests'
        $core = $result.Calls | Where-Object Name -eq 'FileScope.Core.Tests'
        Check ($result.Error -like '*exit 23*' -and $result.LocationRestored -and
            ($core.Settings.RunSettings.TestRunParameters.Parameter | Where-Object name -eq 'SignedFile').value -eq $expected -and
            (Get-Content -LiteralPath 'custom.runsettings' -Raw).Trim() -eq $customSettings -and
            (Test-Path -LiteralPath $core.ReportPath) -and
            @(Get-ChildItem -LiteralPath (Split-Path $preserved -Parent)).Count -eq 1) 'failed test runs remove temporary overrides and preserve the custom configuration'
    }
    finally { Pop-Location }

    foreach ($failure in @('--version', 'build', 'FileScope.Core.Tests', 'FileScope.Presentation.Tests', 'FileScope.Ui.Tests', 'DuplicateFinder.Tests', 'DuplicateFinder.Ui.Tests')) {
        $result = Invoke-Scenario 'Verify.ps1' @{} $failure
        Check ($result.Error -like '*exit 23*' -and $result.Calls[-1].Name -eq $failure -and -not $result.Log.Contains('[SUCCESS]')) "failure stops verification immediately: $failure"
        Check ($result.LocationRestored -and @(Get-ChildItem -LiteralPath (Split-Path $preserved -Parent)).Count -eq 1) "failure restores location and cleans temporary files: $failure"
    }
    $result = Invoke-Scenario 'Build.ps1' @{ Publish = $true; Runtime = 'win-arm64' } 'publish'
    Check ($result.Error -like '*exit 23*' -and -not $result.Log.Contains('[SUCCESS]') -and $result.LocationRestored) 'publish failure propagates without a success message'
    $result = Invoke-Scenario 'Build.ps1' @{ SelfContained = $true }
    Check ($result.Error -like '*requires -Publish*' -and $result.Calls.Count -eq 0) 'self-contained without publish fails before any build'

    $env:NO_COLOR = $null
    $escape = [string][char]27
    $colors = @{ Header = '45;97'; Phase = '44;97'; Info = '37'; Success = '42;97'; Warning = '93'; Failure = '41;97' }
    foreach ($kind in @('Header', 'Phase', 'Info', 'Success', 'Warning', 'Failure')) {
        $lines = @(Get-MessageLines 'Test message' $kind)
        Check ($lines.Count -eq 1 -and $lines[0] -ceq "${escape}[$($colors[$kind])m[$($kind.ToUpperInvariant())] Test message${escape}[0m") "ANSI color resets before the host newline: $kind"
    }
    foreach ($separator in @("`r`n", "`n", "`r")) {
        $lines = @(Get-MessageLines "first${separator}${separator}last" 'Phase')
        Check ($lines.Count -eq 3 -and $lines[0] -ceq "${escape}[44;97m[PHASE] first${escape}[0m" -and
            $lines[1] -ceq "${escape}[44;97m${escape}[0m" -and $lines[2] -ceq "${escape}[44;97mlast${escape}[0m") "multiline output resets every line, including empty lines (separator bytes: $([System.Text.Encoding]::ASCII.GetBytes($separator) -join ', '))"
    }
    $lines = @(Get-MessageLines 'Plain message' 'Success' -NoColor)
    Check ($lines.Count -eq 1 -and $lines[0] -ceq '[SUCCESS] Plain message') 'NoColor suppresses ANSI sequences'
    $env:NO_COLOR = '1'
    $lines = @(Get-MessageLines 'Plain message' 'Failure')
    Check ($lines.Count -eq 1 -and $lines[0] -ceq '[FAILURE] Plain message') 'NO_COLOR environment variable suppresses ANSI sequences'

    # Test the real platform guard independently of the fake Windows scenarios.
    . (Join-Path $repository 'build/ScriptSupport.ps1') -NoColor
    $isWindowsHost = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
    $hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    $rejected = $false
    try { Assert-RuntimeExecution 'win-arm64' }
    catch { $rejected = $true }
    Check ($rejected -eq (-not $isWindowsHost -or $hostArchitecture -ne 'Arm64')) 'real host guard allows ARM64 execution only on Windows ARM64'
    Write-Host "Script checks: $script:CheckCount passed."
}
finally {
    $env:OS = $oldOs
    $env:NO_COLOR = $oldNoColor
    if ($null -ne $oldExitCode) { $global:LASTEXITCODE = $oldExitCodeValue }
    else { Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
}
