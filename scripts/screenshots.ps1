<#
.SYNOPSIS
    Renders the README's screenshots into docs/screenshots.

.DESCRIPTION
    Builds tools/Pendulum.Screenshots and runs it. It draws the app's own views (the Reminders,
    Calendar and Settings tabs, Quick Reminder and an alert) in the dark theme, with invented
    reminders dated around today, and writes reminders.png, calendar.png, settings.png,
    quick-reminder.png and alert.png.

    Nothing is shown on screen: the views are laid out in memory and drawn with
    RenderTargetBitmap, so no window opens, nothing takes the focus and no keyboard or mouse input
    is sent. It never starts Pendulum itself, so a running copy is left alone, and it uses a temp
    folder of its own, never the app's Data\ or Sounds\.

.PARAMETER OutputDirectory
    Where the pictures go. Default: docs/screenshots.

.EXAMPLE
    pwsh scripts/screenshots.ps1
    pwsh scripts/screenshots.ps1 -OutputDirectory $env:TEMP\pendulum-shots
#>
[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'tools/Pendulum.Screenshots/Pendulum.Screenshots.csproj'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'docs/screenshots' }

# Built every time, as the solution builds it (x64), so the pictures always come from the code as
# it is now; then MSBuild says where the exe went (asking alone evaluates, it doesn't build).
dotnet build $project -c Release -p:Platform=x64 --disable-build-servers --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Building the screenshot tool failed.' }
$target = dotnet msbuild $project -p:Configuration=Release -p:Platform=x64 -nologo -nodeReuse:false -getProperty:TargetPath
if ($LASTEXITCODE -ne 0) { throw 'Finding the screenshot tool failed.' }
$exe = [IO.Path]::ChangeExtension(($target | Select-Object -Last 1).Trim(), '.exe')
if (-not (Test-Path $exe)) { throw "The screenshot tool isn't at $exe after building it." }

& $exe $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Rendering the screenshots failed.' }
