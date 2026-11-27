<#
.SYNOPSIS
Builds FileScope and runs its MSTest projects on the selected architecture.
.DESCRIPTION
Uses the same checked-in test projects as Visual Studio and dotnet test.
Without -Runtime, test processes use the current SDK host architecture.
An explicit Windows RID requires a Windows machine that can execute that RID.
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
    [switch]$NoColor
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
    param([string]$Group)

    $name = "FileScope.$Group.Tests"
    Write-BuildMessage $name -Kind Phase
    $project = Join-Path $PSScriptRoot "tests/$name/$name.csproj"
    $arguments = @('test', '--project', $project, '--configuration', $Configuration,
        '--verbosity', $Verbosity, "-p:Platform=$(Get-BuildPlatform $Runtime)",
        '--settings', $settingsPath, '--report-trx', '--report-trx-filename', "$name.trx",
        '--results-directory', $resultsDirectory)

    # A RID is valid for an individual project, never for the solution build.
    if ($Runtime) { $arguments += @('--runtime', $Runtime) }
    if ($NoColor -or $env:NO_COLOR) { $arguments += '--no-ansi' }
    Invoke-DotNet -Name $name -Arguments $arguments
}

Push-Location $PSScriptRoot
try {
    $target = if ($Runtime) { $Runtime } else { 'current SDK host' }
    Write-BuildMessage "FILESCOPE VERIFY | $Configuration | $target" -Kind Header
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
        Invoke-SolutionBuild -RepositoryRoot $PSScriptRoot -Configuration $Configuration -Runtime $Runtime -Verbosity $Verbosity
    }
    else { Write-BuildMessage 'Solution build skipped; dotnet test still builds its projects and references.' -Kind Warning }

    Invoke-Checks -Group 'Core'
    Invoke-Checks -Group 'Presentation'
    if (-not $SkipUi -and $env:OS -eq 'Windows_NT') {
        Invoke-Checks -Group 'Ui'
    }
    else { Write-BuildMessage 'Windows UI tests skipped.' -Kind Warning }

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
