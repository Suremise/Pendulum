# Builds the portable version of Pendulum: a clean, self-contained folder with
# no installer - copy it anywhere and run Pendulum.exe. This is a packaging
# convenience for cutting a release; it's NOT needed for day-to-day development -
# keep using `dotnet build` (or `dotnet run`) against Pendulum.App for that, exactly
# as before. This script just produces a tidy dist\ folder ready to zip and hand out.
#
# Usage:
#   .\build-portable.ps1                # uses the version from Pendulum.App.csproj
#   .\build-portable.ps1 -Version 1.1.0 # overrides it
#
# Output: dist\Pendulum-portable-win-x64\

param(
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
$csproj = Join-Path $repoRoot "Pendulum.App\Pendulum.App.csproj"
$outDir = Join-Path $repoRoot "dist\Pendulum-portable-win-x64"

if (-not $Version) {
    $match = Select-String -Path $csproj -Pattern '<Version>(.*)</Version>'
    if (-not $match) { throw "Couldn't find <Version> in $csproj - pass -Version explicitly." }
    $Version = $match.Matches[0].Groups[1].Value
}

Write-Host "Building Pendulum portable v$Version" -ForegroundColor Cyan

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:ProgramFiles\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "dotnet.exe not found. Install the .NET SDK first." }

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

& $dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:Version=$Version -o $outDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Debug symbols aren't useful in a distributed copy; Data\ is created on first run.
Get-ChildItem $outDir -Filter "*.pdb" -Recurse | Remove-Item -Force
$dataDir = Join-Path $outDir "Data"
if (Test-Path $dataDir) { Remove-Item $dataDir -Recurse -Force }

Write-Host "Portable build ready: $outDir" -ForegroundColor Green
Write-Host "Zip that folder (e.g. Pendulum-portable-win-x64.zip) to distribute it."
