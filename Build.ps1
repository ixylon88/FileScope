<#
.SYNOPSIS
Builds the solution, or builds, runs and publishes an individual application.
.DESCRIPTION
Without -Application, build the whole solution. Run and publish default to FileScope.
Use -Application DuplicateFinder to select its project, executable and publish directory.
.EXAMPLE
.\Build.ps1 -Application DuplicateFinder -Run
.EXAMPLE
.\Build.ps1 -Application DuplicateFinder -Publish -Runtime win-arm64 -SelfContained
.EXAMPLE
.\Build.ps1 -Publish -Runtime win-arm64 -SelfContained
.EXAMPLE
.\Build.ps1 -Publish -Runtime win-x64 -Run -NoColor
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Run,
    [switch]$Publish,
    [switch]$SelfContained,
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')][string]$Runtime = 'win-x64',
    [ValidateSet('quiet', 'minimal', 'normal', 'detailed', 'diagnostic')][string]$Verbosity = 'minimal',
    [switch]$NoColor,
    [ValidateSet('FileScope', 'DuplicateFinder')][string]$Application = 'FileScope'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'build/ScriptSupport.ps1') -NoColor:$NoColor

$Runtime = $Runtime.ToLowerInvariant()
$product = if ($Application -eq 'DuplicateFinder') { 'DuplicateFinder' } else { 'FileScope' }
$appProject = Join-Path $PSScriptRoot "src/$product.App/$product.App.csproj"
$buildTarget = if ($PSBoundParameters.ContainsKey('Application')) { $appProject }
    else { Join-Path $PSScriptRoot 'FileScope.sln' }

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
Push-Location $PSScriptRoot
try {
    Write-BuildMessage "$product BUILD | $Configuration | $Runtime" -Kind Header
    if ($SelfContained -and -not $Publish) { throw '-SelfContained requires -Publish.' }
    if ($Run) { Assert-RuntimeExecution -Runtime $Runtime }
    Initialize-BuildEnvironment -RepositoryRoot $PSScriptRoot

    if ($Publish) {
        $mode = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
        $outputName = if ($SelfContained) { "${Runtime}_self-contained" } else { $Runtime }
        $outputName = "$product/$outputName"
        $outputPath = Join-Path $PSScriptRoot "artifacts/publish/$outputName"
        Write-BuildMessage "PUBLISH | $mode" -Kind Phase
        # Clean only this generated distribution, preserving the other variants.
        if (Test-Path -LiteralPath $outputPath) {
            Remove-Item -LiteralPath $outputPath -Recurse -Force
        }
        $arguments = @('publish', $appProject, '--configuration', $Configuration,
            '--runtime', $Runtime, '--self-contained', $SelfContained.IsPresent.ToString().ToLowerInvariant(),
            '--output', $outputPath, '--verbosity', $Verbosity, '--tl:off', '-p:PublishTrimmed=false',
            "-p:Platform=$(Get-BuildPlatform $Runtime)")
        # Use the patch known to the pinned SDK; this is not a floating NuGet version.
        if ($SelfContained) { $arguments += '-p:TargetLatestRuntimePatch=true' }
        # Publish performs its own matching restore/build for this RID and mode.
        Invoke-DotNet -Name 'Publish' -Arguments $arguments
        Write-BuildMessage "Published to $outputPath" -Kind Success
    }
    else {
        Invoke-BuildTarget -Path $buildTarget -Configuration $Configuration -Runtime $Runtime -Verbosity $Verbosity
    }

    if ($Run) {
        Write-BuildMessage 'RUN APPLICATION' -Kind Phase
        if ($Publish) {
            # Exercise the distributed apphost and resources, including icon/splash.
            $executable = Join-Path $outputPath "$product.App.exe"
            & $executable
            if ($LASTEXITCODE -ne 0) { throw "Published app exited with code $LASTEXITCODE." }
        }
        else {
            # Match the build's platform-only output path; --runtime would
            # make --no-build look for a different, RID-specific directory.
            Invoke-DotNet -Name 'Application' -Arguments @('run', '--project', $appProject,
                '--configuration', $Configuration, '--self-contained', 'false',
                '--no-build', '--no-launch-profile', '--verbosity', $Verbosity, '--tl:off',
                "-p:Platform=$(Get-BuildPlatform $Runtime)")
        }
    }
    Write-BuildMessage 'BUILD SUCCEEDED' -Kind Success
}
catch {
    Write-BuildMessage $_.Exception.Message -Kind Failure
    throw
}
finally {
    Pop-Location
    $stopwatch.Stop()
    Write-BuildMessage "Elapsed: $($stopwatch.Elapsed)"
}
