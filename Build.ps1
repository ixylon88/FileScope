[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Run,
    [switch]$Publish,
    [switch]$SelfContained,
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 10 SDK, then open a new terminal.'
}
$appProject = Join-Path $PSScriptRoot 'src/FileScope.App/FileScope.App.csproj'
Push-Location $PSScriptRoot
try {
    & dotnet build (Join-Path $PSScriptRoot 'FileScope.sln') --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
    if ($Publish) {
        $outputPath = Join-Path $PSScriptRoot "artifacts/publish/$Runtime"
        if ($SelfContained) {
            $outputPath = $outputPath + "_self-contained"
            & dotnet publish $appProject --configuration $Configuration --runtime $Runtime --self-contained true --output $outputPath -p:PublishTrimmed=false
        }
        else {
            & dotnet publish $appProject --configuration $Configuration --runtime $Runtime --output $outputPath
        }
        if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit $LASTEXITCODE)." }
        Write-Host "Published to $outputPath"
    }
    if ($Run) {
        & dotnet run --project $appProject --configuration $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "App exited with code $LASTEXITCODE." }
    }
}
finally { Pop-Location }
