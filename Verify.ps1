[CmdletBinding()]
param(
    [string]$SignedFile,
    [switch]$SkipBuild,
    [switch]$SkipUi
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 10 SDK first.' }
$coreProject = Join-Path $PSScriptRoot 'src/FileScope.Core/FileScope.Core.csproj'
$appProject = Join-Path $PSScriptRoot 'src/FileScope.App/FileScope.App.csproj'
$testDirectory = Join-Path $PSScriptRoot ('artifacts/verify/' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $testDirectory -Force)

function Invoke-Checks {
    param([string]$Name, [string]$TargetFramework, [string]$Reference, [bool]$UseWpf, [string[]]$TestArguments)
    $harnessDirectory = Join-Path $testDirectory $Name
    [void](New-Item -ItemType Directory -Path $harnessDirectory -Force)
    $project = Join-Path $harnessDirectory "$Name.csproj"
    $referenceXml = [System.Security.SecurityElement]::Escape($Reference)
    $sourceXml = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot "tests/$Name.cs"))
    $wpf = if ($UseWpf) { '<UseWPF>true</UseWPF><EnableWindowsTargeting>true</EnableWindowsTargeting>' } else { '' }
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>$TargetFramework</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>$wpf
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="$referenceXml" /><Compile Include="$sourceXml" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project -Encoding UTF8
    & dotnet run --project $project --configuration Release -- @TestArguments
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit $LASTEXITCODE)." }
}

Push-Location $PSScriptRoot
try {
    if (-not $SkipBuild) {
        & dotnet build (Join-Path $PSScriptRoot 'FileScope.sln') --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "Solution build failed (exit $LASTEXITCODE)." }
    }
    $coreArguments = @()
    if ($SignedFile) { $coreArguments = @('--signed-file', (Resolve-Path -LiteralPath $SignedFile).Path) }
    Invoke-Checks -Name 'CoreChecks' -TargetFramework 'net10.0' -Reference $coreProject -UseWpf $false -TestArguments $coreArguments
    if (-not $SkipUi -and $env:OS -eq 'Windows_NT') {
        Invoke-Checks -Name 'UiChecks' -TargetFramework 'net10.0-windows' -Reference $appProject -UseWpf $true -TestArguments @()
    }
    else { Write-Host 'Windows UI checks skipped.' }
    Write-Host 'All requested checks passed.'
}
finally {
    Pop-Location
    # Only this invocation's generated harness files are removed.
    Remove-Item -LiteralPath $testDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
