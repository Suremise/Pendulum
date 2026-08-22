# Builds the installable version of Pendulum: publishes a clean self-contained
# win-x64 build, then compiles it into a Setup.exe with Inno Setup.
#
# Usage:
#   .\build-installer.ps1                # uses the version from Pendulum.App.csproj
#   .\build-installer.ps1 -Version 1.1.0 # overrides it
#
# Output: installer\Output\PendulumSetup-<version>.exe

param(
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
$csproj = Join-Path $repoRoot "Pendulum.App\Pendulum.App.csproj"
$publishDir = Join-Path $repoRoot "installer\publish"
$innoScript = Join-Path $repoRoot "installer\Pendulum.iss"

if (-not $Version) {
    $match = Select-String -Path $csproj -Pattern '<Version>(.*)</Version>'
    if (-not $match) { throw "Couldn't find <Version> in $csproj - pass -Version explicitly." }
    $Version = $match.Matches[0].Groups[1].Value
}

Write-Host "Building Pendulum installer v$Version" -ForegroundColor Cyan

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:ProgramFiles\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "dotnet.exe not found. Install the .NET SDK first." }

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

& $dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe (Inno Setup compiler) not found. Install Inno Setup 6 first." }

& $iscc "/DMyAppVersion=$Version" "/DSourceDir=$publishDir" $innoScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed" }

Write-Host "Installer built: installer\Output\PendulumSetup-$Version.exe" -ForegroundColor Green
