# Builds what a Pendulum release ships: publishes a clean self-contained win-x64 build,
# compiles it into a Setup.exe with Inno Setup, zips the same build as a portable copy, and
# writes their SHA-256 checksums. The release workflow runs this same script, so a local build
# and a release can't drift apart.
#
# Usage:
#   .\build-installer.ps1                # uses the version from Pendulum.App.csproj
#   .\build-installer.ps1 -Version 1.1.0 # overrides it
#
# Output, in dist\:
#   PendulumSetup-<version>.exe          the installer (per-user, keeps Data\ on upgrade)
#   Pendulum-<version>-portable.zip      the same build to unzip and run anywhere
#   SHA256SUMS.txt                       their checksums

param(
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
$csproj = Join-Path $repoRoot "Pendulum.App\Pendulum.App.csproj"
$publishDir = Join-Path $repoRoot "installer\publish"
$innoScript = Join-Path $repoRoot "installer\Pendulum.iss"
$distDir = Join-Path $repoRoot "dist"

if (-not $Version) {
    $match = Select-String -Path $csproj -Pattern '<Version>(.*)</Version>'
    if (-not $match) { throw "Couldn't find <Version> in $csproj - pass -Version explicitly." }
    $Version = $match.Matches[0].Groups[1].Value
}

Write-Host "Building Pendulum $Version" -ForegroundColor Cyan

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:ProgramFiles\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "dotnet.exe not found. Install the .NET SDK first." }

foreach ($dir in $publishDir, $distDir) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}

# --disable-build-servers: no MSBuild nodes or compiler server left running afterwards.
& $dotnet publish $csproj -c Release -r win-x64 --self-contained true --disable-build-servers `
    -p:PublishSingleFile=false -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# A dev build's own Data\ (settings, reminders) must never ship.
if (Test-Path (Join-Path $publishDir "Data")) { throw "The publish folder has a Data\ folder: it must never ship." }

# whisper.cpp's libraries need Microsoft's Visual C++ runtime beside them: a release never ships
# without it (the build copies it from Visual Studio's redistributable folder).
$vcRuntime = "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "vcomp140.dll"
foreach ($dll in $vcRuntime) {
    if (-not (Test-Path (Join-Path $publishDir "runtimes\win-x64\$dll"))) {
        throw "runtimes\win-x64\$dll is missing from the build: install Visual Studio's C++ build tools (its redistributable files) and build again."
    }
}

# Signing comes later. When there's a certificate, sign Pendulum.exe here, before it's packaged,
# and the Setup.exe after Inno Setup builds it, for example:
#   signtool sign /fd SHA256 /tr http://timestamp.example /td SHA256 /f cert.pfx /p $env:SIGNING_PASSWORD `
#       (Join-Path $publishDir "Pendulum.exe")

$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe (Inno Setup compiler) not found. Install Inno Setup 6 first." }

& $iscc "/DMyAppVersion=$Version" "/DSourceDir=$publishDir" $innoScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed" }

New-Item -ItemType Directory $distDir | Out-Null
$setupName = "PendulumSetup-$Version.exe"
Copy-Item (Join-Path $repoRoot "installer\Output\$setupName") (Join-Path $distDir $setupName)

# The portable copy: the same files the installer installs (no debug symbols, no Data\), with the
# licence and notices beside the exe. It keeps its data in a Data\ folder beside Pendulum.exe.
$portableName = "Pendulum-$Version-portable.zip"
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("pendulum-portable-" + [guid]::NewGuid().ToString("N"))
try {
    $portableRoot = Join-Path $stage "Pendulum"
    Copy-Item $publishDir $portableRoot -Recurse
    Get-ChildItem $portableRoot -Recurse -Filter *.pdb | Remove-Item -Force
    Copy-Item (Join-Path $repoRoot "LICENSE") $portableRoot
    Copy-Item (Join-Path $repoRoot "THIRD-PARTY-NOTICES.md") $portableRoot
    Compress-Archive -Path $portableRoot -DestinationPath (Join-Path $distDir $portableName)
}
finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}

# The zip holds what it should, and nothing it shouldn't.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Join-Path $distDir $portableName))
try {
    $entries = $zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') }
    $required = @("Pendulum/Pendulum.exe", "Pendulum/LICENSE", "Pendulum/THIRD-PARTY-NOTICES.md", "Pendulum/Sounds/chime.wav") +
        ($vcRuntime | ForEach-Object { "Pendulum/runtimes/win-x64/$_" })
    foreach ($required in $required) {
        if ($entries -notcontains $required) { throw "The portable zip has no $required." }
    }
    $unwanted = $entries | Where-Object { $_ -like "*.pdb" -or $_ -like "Pendulum/Data/*" -or $_ -like "Pendulum/unins*" }
    if ($unwanted) { throw "The portable zip holds files it mustn't: $($unwanted -join ', ')" }
}
finally {
    $zip.Dispose()
}

# SHA256SUMS.txt, in the format sha256sum -c reads: "<hash>  <name>", LF line ends.
$sums = foreach ($name in $setupName, $portableName) {
    $hash = (Get-FileHash (Join-Path $distDir $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
[System.IO.File]::WriteAllText((Join-Path $distDir "SHA256SUMS.txt"), (($sums -join "`n") + "`n"))

Write-Host "Built in dist\: $setupName, $portableName, SHA256SUMS.txt" -ForegroundColor Green
