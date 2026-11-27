<#
.SYNOPSIS
Builds and verifies both applications or the one selected with -Application.
.DESCRIPTION
Uses the same checked-in test projects as Visual Studio and dotnet test.
Without -Runtime, test processes use the current SDK host architecture.
An explicit Windows RID requires a Windows machine that can execute that RID.
-Application All (default) runs the full suite. ScriptChecks run for every selection.
.EXAMPLE
.\Verify.ps1 -Application DuplicateFinder -Runtime win-x64
.EXAMPLE
.\Verify.ps1 -Runtime win-arm64
.EXAMPLE
.\Verify.ps1 -Settings .\tests\FileScope.local.runsettings
#>
[CmdletBinding()]
param(
    [string]$SignedFile,
    [string[]]$VersionFile = @(),
    [string]$Settings,
    [switch]$SkipBuild,
    [switch]$SkipUi,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')][string]$Runtime,
    [ValidateSet('quiet', 'minimal', 'normal', 'detailed', 'diagnostic')][string]$Verbosity = 'minimal',
    [switch]$NoColor,
    [ValidateSet('All', 'FileScope', 'DuplicateFinder')][string]$Application = 'All'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'build/ScriptSupport.ps1') -NoColor:$NoColor
if ($Runtime) { $Runtime = $Runtime.ToLowerInvariant() }

# Resolve caller-relative paths before entering the repository. XML serialization
# preserves quotes, ampersands and Unicode in optional fixture paths.
$settingsPath = if ($Settings) { (Resolve-Path -LiteralPath $Settings).Path }
    else { Join-Path $PSScriptRoot 'tests/FileScope.runsettings' }
$resolvedSignedFile = if ($SignedFile) { (Resolve-Path -LiteralPath $SignedFile).Path } else { '' }
$resolvedVersionFiles = @($VersionFile | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })

# Select canonical paths even when the caller uses lowercase (ValidateSet ignores case).
$buildTarget = switch ($Application) {
    'FileScope'       { Join-Path $PSScriptRoot 'src/FileScope.App/FileScope.App.csproj' }
    'DuplicateFinder' { Join-Path $PSScriptRoot 'src/DuplicateFinder.App/DuplicateFinder.App.csproj' }
    'All'             { Join-Path $PSScriptRoot 'FileScope.sln' }
}

$runId = [Guid]::NewGuid().ToString('N')
$temporaryDirectory = Join-Path $PSScriptRoot "artifacts/verify/$runId"
$resultsDirectory = Join-Path $PSScriptRoot "artifacts/test-results/$runId"
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

function Set-TestParameter {
    param([xml]$Document, [string]$Name, [string]$Value)

    $parameters = $Document.SelectSingleNode('/RunSettings/TestRunParameters')
    if ($null -eq $parameters) {
        $parameters = $Document.CreateElement('TestRunParameters')
        [void]$Document.DocumentElement.AppendChild($parameters)
    }
    $parameter = $parameters.SelectSingleNode("Parameter[@name='$Name']")
    if ($null -eq $parameter) {
        $parameter = $Document.CreateElement('Parameter')
        $parameter.SetAttribute('name', $Name)
        [void]$parameters.AppendChild($parameter)
    }
    $parameter.SetAttribute('value', $Value)
}

function Invoke-Checks {
    param([string]$Name)
    Write-BuildMessage $Name -Kind Phase
    $project = Join-Path $PSScriptRoot "tests/$Name/$Name.csproj"
    $arguments = @('test', '--project', $project, '--configuration', $Configuration,
        '--verbosity', $Verbosity, "-p:Platform=$(Get-BuildPlatform $Runtime)",
        '--settings', $settingsPath, '--report-trx', '--report-trx-filename', "$Name.trx",
        '--results-directory', $resultsDirectory)

    # This portable assembly contains shared presentation tests and resources for both apps.
    if ($Name -eq 'FileScope.Presentation.Tests') {
        switch ($Application) {
            'FileScope'       { $arguments += @('--filter', 'TestCategory!=DuplicateFinder') }
            'DuplicateFinder' { $arguments += @('--filter', 'TestCategory=Shared|TestCategory=DuplicateFinder') }
        }
    }

    # A RID is valid for an individual project, never for the solution build.
    if ($Runtime) { $arguments += @('--runtime', $Runtime) }
    if ($NoColor -or $env:NO_COLOR) { $arguments += '--no-ansi' }
    Invoke-DotNet -Name $Name -Arguments $arguments
}

Push-Location $PSScriptRoot
try {
    $target = if ($Runtime) { $Runtime } else { 'current SDK host' }
    Write-BuildMessage "VERIFY | $Application | $Configuration | $target" -Kind Header
    Assert-RuntimeExecution -Runtime $Runtime
    Initialize-BuildEnvironment -RepositoryRoot $PSScriptRoot

    if ($SignedFile -or $resolvedVersionFiles.Count -gt 0) {
        [xml]$document = Get-Content -LiteralPath $settingsPath -Raw
        if ($SignedFile) { Set-TestParameter $document 'SignedFile' $resolvedSignedFile }
        if ($resolvedVersionFiles.Count -gt 0) {
            $json = ConvertTo-Json -InputObject $resolvedVersionFiles -Compress
            Set-TestParameter $document 'VersionFiles' $json
        }
        [void](New-Item -ItemType Directory -Path $temporaryDirectory -Force)
        $settingsPath = Join-Path $temporaryDirectory 'FileScope.runsettings'
        $document.Save($settingsPath)
    }

    Write-BuildMessage 'ScriptChecks' -Kind Phase
    & (Join-Path $PSScriptRoot 'tests/ScriptChecks.ps1')

    if (-not $SkipBuild) {
        Invoke-BuildTarget -Path $buildTarget -Configuration $Configuration -Runtime $Runtime -Verbosity $Verbosity
    }
    else { Write-BuildMessage 'Initial build skipped; dotnet test still builds its projects and references.' -Kind Warning }

    # Both apps depend on FileScope.Core and FileScope.Shared. Each owns its WPF suite.
    $testProjects = @('FileScope.Core.Tests')
    if ($Application -ne 'FileScope') { $testProjects += 'DuplicateFinder.Tests' }
    $testProjects += 'FileScope.Presentation.Tests'
    if ($Application -ne 'DuplicateFinder') { $testProjects += 'FileScope.Ui.Tests' }
    if ($Application -ne 'FileScope') { $testProjects += 'DuplicateFinder.Ui.Tests' }

    if ($SkipUi -or $env:OS -ne 'Windows_NT') {
        Write-BuildMessage 'Windows UI tests skipped.' -Kind Warning
        $testProjects = $testProjects -notlike '*.Ui.Tests'
    }

    foreach ($project in $testProjects) {
        Invoke-Checks -Name $project
    }

    Write-BuildMessage 'All requested checks passed.' -Kind Success
}
catch {
    Write-BuildMessage $_.Exception.Message -Kind Failure
    throw
}
finally {
    Pop-Location
    # Preserve TRX reports, including failures; remove only this run's settings copy.
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
    $stopwatch.Stop()
    Write-BuildMessage "Test reports: $resultsDirectory"
    Write-BuildMessage "Elapsed: $($stopwatch.Elapsed)"
}
