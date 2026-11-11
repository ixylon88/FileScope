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
        if ($name -eq 'run') {
            $project = $arguments[[Array]::IndexOf($arguments, '--project') + 1]
            $name = [System.IO.Path]::GetFileNameWithoutExtension($project)
            $xml = [xml](Get-Content -LiteralPath $project -Raw)
        }
        $scenarioState.Calls.Add([pscustomobject]@{ Name = $name; Arguments = $arguments; Project = $xml })
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
    [void](New-Item -ItemType Directory -Path (Join-Path $fixture 'build') -Force)
    [void](New-Item -ItemType Directory -Path (Join-Path $fixture 'tests') -Force)
    [void](New-Item -ItemType Directory -Path (Join-Path $fixture 'src/FileScope.App') -Force)
    foreach ($path in @('Build.ps1', 'Verify.ps1', 'global.json', 'build/ScriptSupport.ps1',
        'build/colored_console.ps1', 'src/FileScope.App/FileScope.App.csproj')) {
        Copy-Item -LiteralPath (Join-Path $repository $path) -Destination (Join-Path $fixture $path)
    }
    # Only the disposable copy bypasses platform execution; every dotnet call is fake.
    Add-Content -LiteralPath (Join-Path $fixture 'build/ScriptSupport.ps1') -Value "`nfunction Assert-RuntimeExecution { param([string]`$Runtime) }"
    Set-Content -LiteralPath (Join-Path $fixture 'tests/ScriptChecks.ps1') -Value '# Avoid recursive orchestration tests in this fixture.'
    $env:OS = 'Windows_NT'
    $preserved = Join-Path $fixture 'artifacts/verify/keep.txt'
    [void](New-Item -ItemType Directory -Path (Split-Path $preserved -Parent) -Force)
    Set-Content -LiteralPath $preserved -Value 'Preserve unrelated artifacts.'

    foreach ($runtime in @('win-x64', 'win-arm64')) {
        foreach ($selfContained in @($false, $true)) {
            $name = if ($selfContained) { "${runtime}_self-contained" } else { $runtime }
            $stale = Join-Path $fixture "artifacts/publish/$name/stale.txt"
            [void](New-Item -ItemType Directory -Path (Split-Path $stale -Parent) -Force)
            Set-Content -LiteralPath $stale -Value 'Old publish content.'
            $result = Invoke-Scenario 'Build.ps1' @{ Publish = $true; Runtime = $runtime; SelfContained = $selfContained }
            $publish = @($result.Calls | Where-Object Name -eq 'publish')
            Check ($result.Error -eq '' -and $publish.Count -eq 1 -and -not ($result.Calls | Where-Object Name -eq 'build')) "publish performs one matching restore/build: $name ($($result.Error))"
            $call = $publish[0]
            Check ((Has-Option $call '--runtime' $runtime) -and (Has-Option $call '--self-contained' $selfContained.ToString().ToLowerInvariant()) -and
                ($call.Arguments -contains '-p:PublishTrimmed=false') -and
                (($call.Arguments -contains '-p:TargetLatestRuntimePatch=true') -eq $selfContained)) "publish uses explicit deployment settings: $name"
            Check (-not (Test-Path -LiteralPath $stale) -and (Test-Path -LiteralPath $preserved) -and $result.LocationRestored) "publish removes stale output and preserves other files/location: $name"
        }
    }

    foreach ($runtime in @('win-x64', 'win-arm64')) {
        $result = Invoke-Scenario 'Build.ps1' @{ Runtime = $runtime; Configuration = 'Debug'; Run = $true }
        $build = @($result.Calls | Where-Object Name -eq 'build')
        $run = @($result.Calls | Where-Object Name -eq 'FileScope.App')
        $platform = if ($runtime -eq 'win-arm64') { 'Arm64' } else { 'x64' }
        Check ($result.Error -eq '' -and $build.Count -eq 1 -and $run.Count -eq 1 -and
            (Has-Option $build[0] '--configuration' 'Debug') -and (Has-Option $run[0] '--configuration' 'Debug') -and
            $build[0].Arguments -contains "-p:Platform=$platform" -and $run[0].Arguments -contains "-p:Platform=$platform") "normal build and run select the same platform/configuration: $runtime"
        Check ($build[0].Arguments -notcontains '--runtime' -and $run[0].Arguments -notcontains '--runtime' -and
            $run[0].Arguments -contains '--no-build') "run reuses the solution output without a different RID directory: $runtime"
    }

    foreach ($runtime in @('win-x64', 'win-arm64')) {
        $result = Invoke-Scenario 'Verify.ps1' @{ Runtime = $runtime; Configuration = 'Debug' }
        $work = @($result.Calls | Where-Object { $_.Name -in @('build', 'CoreChecks', 'PresentationChecks', 'UiChecks') })
        $platform = if ($runtime -eq 'win-arm64') { 'Arm64' } else { 'x64' }
        Check ($result.Error -eq '' -and $work.Count -eq 4 -and
            @($work | Where-Object { -not (Has-Option $_ '--configuration' 'Debug') -or
                $_.Arguments -notcontains "-p:Platform=$platform" }).Count -eq 0) "build and all three test processes use the same architecture/configuration: $runtime"
        $build = $work | Where-Object Name -eq 'build'
        $harnesses = @($work | Where-Object Name -ne 'build')
        Check ($build.Arguments -notcontains '--runtime' -and
            @($harnesses | Where-Object { -not (Has-Option $_ '--runtime' $runtime) }).Count -eq 0) "verify uses runtime identifiers only for individual test projects: $runtime"
        $ui = $work | Where-Object Name -eq 'UiChecks'
        Check ($ui.Project.Project.PropertyGroup.UseWPF -eq 'true') "UI harness still uses actual WPF: $runtime"
        Check (@(Get-ChildItem -LiteralPath (Split-Path $preserved -Parent)).Count -eq 1 -and $result.LocationRestored) "verify cleans only its temporary harnesses: $runtime"
    }

    $result = Invoke-Scenario 'Verify.ps1' @{ SkipBuild = $true; SkipUi = $true }
    Check ($result.Error -eq '' -and @($result.Calls | Where-Object { $_.Name -in @('CoreChecks', 'PresentationChecks') }).Count -eq 2 -and
        @($result.Calls | Where-Object { $_.Name -in @('build', 'UiChecks') }).Count -eq 0) 'skip switches keep the requested non-UI tests'
    Check (-not $result.Log.Contains([string][char]27) -and $result.Log.Contains('[SUCCESS]')) 'plain console output retains readable phase/result labels'

    $caller = Join-Path $fixture 'external caller'
    [void](New-Item -ItemType Directory -Path $caller -Force)
    Push-Location $caller
    try {
        Set-Content -LiteralPath 'sample & version.exe' -Value 'test input'
        $result = Invoke-Scenario 'Verify.ps1' @{ SignedFile = './sample & version.exe'; VersionFile = @('./sample & version.exe'); SkipBuild = $true }
        $core = $result.Calls | Where-Object Name -eq 'CoreChecks'
        $expected = Join-Path $caller 'sample & version.exe'
        Check ($result.Error -eq '' -and (Has-Option $core '--signed-file' $expected) -and (Has-Option $core '--version-file' $expected)) 'sample paths with spaces and XML-sensitive characters survive argument forwarding'
    }
    finally { Pop-Location }

    foreach ($failure in @('--version', 'build', 'CoreChecks', 'PresentationChecks', 'UiChecks')) {
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
