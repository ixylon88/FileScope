<#
.SYNOPSIS
Builds FileScope and executes its checks on the selected Windows architecture.
.DESCRIPTION
Without -Runtime, the test processes use the current SDK host architecture.
An explicit Windows RID requires a Windows machine that can execute that RID.
.EXAMPLE
.\Verify.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [string]$SignedFile,
    [string[]]$VersionFile = @(),
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
$coreProject = Join-Path $PSScriptRoot 'src/FileScope.Core/FileScope.Core.csproj'
$appProject = Join-Path $PSScriptRoot 'src/FileScope.App/FileScope.App.csproj'
$testDirectory = Join-Path $PSScriptRoot ('artifacts/verify/' + [Guid]::NewGuid().ToString('N'))
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

function Invoke-Checks {
    param([string]$Name, [string]$TargetFramework, [string]$Reference, [bool]$UseWpf, [string[]]$TestArguments)
    Write-BuildMessage $Name -Kind Phase
    $harnessDirectory = Join-Path $testDirectory $Name
    [void](New-Item -ItemType Directory -Path $harnessDirectory -Force)
    $project = Join-Path $harnessDirectory "$Name.csproj"
    $referenceXml = [System.Security.SecurityElement]::Escape($Reference)
    $sourceXml = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot "tests/$Name.cs"))
    $wpf = if ($UseWpf) { '<UseWPF>true</UseWPF><EnableWindowsTargeting>true</EnableWindowsTargeting>' } else { '' }
    $extraItems = ''
    if ($Name -eq 'PresentationChecks') {
        # Compile the UI-independent presentation code without a Windows runtime.
        $sources = @('Infrastructure/ObservableObject.cs', 'Infrastructure/Commands.cs',
            'Localization/LocalizationService.cs', 'Presentation/DigestFormatter.cs', 'Presentation/WindowSize.cs', 'Presentation/ResultExportProjection.cs',
            'Presentation/AnalysisRunSummary.cs', 'Presentation/AnalysisProgressBuffer.cs', 'Presentation/SignatureMoveSelection.cs', 'Presentation/FileMoveReport.cs', 'Presentation/AnalysisStageText.cs',
            'Services/PreferencesStore.cs', 'Services/IThemeService.cs', 'ViewModels/Options.cs', 'ViewModels/ColumnOption.cs',
            'ViewModels/SignatureOption.cs', 'ViewModels/OptionsViewModel.cs', 'ViewModels/FileRowViewModel.cs')
        foreach ($relative in $sources) {
            $item = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot "src/FileScope.App/$relative"))
            $extraItems += "<Compile Include=`"$item`" />"
        }
        $resources = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'src/FileScope.App/Resources/Strings*.resx'))
        $extraItems += "<EmbeddedResource Include=`"$resources`" Link=`"Resources/%(Filename)%(Extension)`" />"
    }
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>$TargetFramework</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <DebugType>portable</DebugType><DebugSymbols>true</DebugSymbols>
    <RootNamespace>FileScope.App</RootNamespace>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>$wpf
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="$referenceXml" /><Compile Include="$sourceXml" />$extraItems</ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project -Encoding UTF8
    $arguments = @('run', '--project', $project, '--configuration', $Configuration,
        '--self-contained', 'false', '--no-launch-profile', '--verbosity', $Verbosity, '--tl:off',
        "-p:Platform=$(Get-BuildPlatform $Runtime)")
    if ($Runtime) { $arguments += @('--runtime', $Runtime) }
    Invoke-DotNet -Name $Name -Arguments ($arguments + @('--') + $TestArguments)
}

# Resolve user-supplied paths relative to the caller, before entering the repository.
$coreArguments = @()
if ($SignedFile) { $coreArguments = @('--signed-file', (Resolve-Path -LiteralPath $SignedFile).Path) }
foreach ($file in $VersionFile) { $coreArguments += @('--version-file', (Resolve-Path -LiteralPath $file).Path) }
Push-Location $PSScriptRoot
try {
    $target = if ($Runtime) { $Runtime } else { 'current SDK host' }
    Write-BuildMessage "FILESCOPE VERIFY | $Configuration | $target" -Kind Header
    Assert-RuntimeExecution -Runtime $Runtime
    Initialize-BuildEnvironment -RepositoryRoot $PSScriptRoot
    Write-BuildMessage 'ScriptChecks' -Kind Phase
    & (Join-Path $PSScriptRoot 'tests/ScriptChecks.ps1')
    if (-not $SkipBuild) {
        Invoke-SolutionBuild -RepositoryRoot $PSScriptRoot -Configuration $Configuration -Runtime $Runtime -Verbosity $Verbosity
    }
    else { Write-BuildMessage 'Solution build skipped; test harnesses still build their references.' -Kind Warning }
    Invoke-Checks -Name 'CoreChecks' -TargetFramework 'net10.0' -Reference $coreProject -UseWpf $false -TestArguments $coreArguments
    Invoke-Checks -Name 'PresentationChecks' -TargetFramework 'net10.0' -Reference $coreProject -UseWpf $false -TestArguments @()
    if (-not $SkipUi -and $env:OS -eq 'Windows_NT') {
        Invoke-Checks -Name 'UiChecks' -TargetFramework 'net10.0-windows' -Reference $appProject -UseWpf $true -TestArguments @()
    }
    else { Write-BuildMessage 'Windows UI checks skipped.' -Kind Warning }
    Write-BuildMessage 'All requested checks passed.' -Kind Success
}
catch {
    Write-BuildMessage $_.Exception.Message -Kind Failure
    throw
}
finally {
    Pop-Location
    # Only this invocation's generated harness files are removed.
    Remove-Item -LiteralPath $testDirectory -Recurse -Force -ErrorAction SilentlyContinue
    $stopwatch.Stop()
    Write-BuildMessage "Elapsed: $($stopwatch.Elapsed)"
}
