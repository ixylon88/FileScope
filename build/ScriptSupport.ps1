# Shared console output and native-command handling for local and CI builds.
# Keep this compatible with Windows PowerShell 5.1 and PowerShell 7.
[CmdletBinding()]
param([switch]$NoColor)

$script:FileScopeUseColor = -not ($NoColor -or $env:NO_COLOR)

function Write-BuildMessage {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [ValidateSet('Header', 'Phase', 'Info', 'Success', 'Warning', 'Failure')]
        [string]$Kind = 'Info'
    )

    $text = "[$($Kind.ToUpperInvariant())] $Message"
    if (-not $script:FileScopeUseColor) { Write-Host $text; return }
    switch ($Kind) {
        'Header'  { Write-Host $text -ForegroundColor White -BackgroundColor DarkMagenta }
        'Phase'   { Write-Host $text -ForegroundColor White -BackgroundColor DarkBlue }
        'Success' { Write-Host $text -ForegroundColor White -BackgroundColor DarkGreen }
        'Warning' { Write-Host $text -ForegroundColor Yellow }
        'Failure' { Write-Host $text -ForegroundColor White -BackgroundColor DarkRed }
        default   { Write-Host $text -ForegroundColor Gray }
    }
}

function Invoke-DotNet {
    param([string]$Name, [string[]]$Arguments)

    # Arguments stay separate; the displayed command is never evaluated as code.
    Write-BuildMessage ("dotnet " + ($Arguments -join ' '))
    # Handle exit codes identically in PowerShell 5.1 and 7, including CI profiles.
    $PSNativeCommandUseErrorActionPreference = $false
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit $LASTEXITCODE)." }
}

function Initialize-BuildEnvironment {
    param([string]$RepositoryRoot)

    $sdk = (Get-Content -LiteralPath (Join-Path $RepositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "Install .NET SDK $sdk from global.json, then open a new terminal."
    }
    # Unlike --info, --version fails if the SDK selected by global.json is absent.
    $version = Invoke-DotNet -Name "SDK selection (required: $sdk)" -Arguments @('--version')
    Write-BuildMessage "Selected .NET SDK: $version"
    Invoke-DotNet -Name 'SDK information' -Arguments @('--info')
}

function Assert-RuntimeExecution {
    param([string]$Runtime)

    if (-not $Runtime) { return }
    if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)) {
        throw "Executing $Runtime requires Windows. Cross-publishing is available with Build.ps1 -Publish -Runtime $Runtime."
    }
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    if (($Runtime -eq 'win-arm64' -and $architecture -ne 'Arm64') -or
        ($Runtime -eq 'win-x64' -and $architecture -notin @('X64', 'Arm64'))) {
        throw "Cannot execute $Runtime on Windows $architecture. Use a matching Windows machine or CI runner; cross-publishing does not execute tests."
    }
}

function Get-BuildPlatform {
    param([string]$Runtime)

    switch ($Runtime) {
        'win-arm64' { return 'Arm64' }
        'win-x64' { return 'x64' }
        'win-x86' { return 'x86' }
        default { return 'AnyCPU' }
    }
}

function Invoke-SolutionBuild {
    param([string]$RepositoryRoot, [string]$Configuration, [string]$Runtime, [string]$Verbosity)

    Write-BuildMessage 'BUILD SOLUTION' -Kind Phase
    # Solution platform names use "Any CPU"; SDK project properties use "AnyCPU".
    $platform = if ($Runtime) { Get-BuildPlatform $Runtime } else { 'Any CPU' }
    $arguments = @('build', (Join-Path $RepositoryRoot 'FileScope.sln'),
        '--configuration', $Configuration, '--verbosity', $Verbosity, '--tl:off',
        # A serial solution build keeps the two-project log ordered and avoids
        # additional MSBuild worker processes on constrained build hosts.
        '-m:1', '--self-contained', 'false', "-p:Platform=$platform")
    if ($Runtime) { $arguments += @('--runtime', $Runtime) }
    Invoke-DotNet -Name 'Solution build' -Arguments $arguments
}
