; Inno Setup script for Pendulum.
; Built via build-installer.ps1, which passes /DMyAppVersion and /DSourceDir.
; Can also be compiled directly (e.g. from the Inno Setup IDE) using the
; fallback defines below, as long as a Release build already exists.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\Pendulum.App\bin\Release\net8.0-windows\win-x64"
#endif

[Setup]
AppId={{287D517B-D89D-47D7-9BBC-BEA11618BDB3}
AppName=Pendulum
AppVersion={#MyAppVersion}
AppPublisher=Surmise Software
AppPublisherURL=https://surmise.it
AppSupportURL=https://surmise.it
AppUpdatesURL=https://surmise.it
DefaultDirName={localappdata}\Programs\Pendulum
DefaultGroupName=Pendulum
DisableProgramGroupPage=yes
; Per-user install under LocalAppData - no admin/UAC prompt needed, and it keeps
; the app's own Data\ and Sounds\ folders (created next to the exe) writable
; without elevation, exactly like the portable build.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=PendulumSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Pendulum.exe
SetupIconFile=..\Pendulum.App\Resources\pendulum.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Excludes debug symbols and any Data\ folder that might exist in a dev build output -
; Data\ and Sounds\ (beyond the shipped defaults) are created fresh on first run.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,Data\*"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\Pendulum"; Filename: "{app}\Pendulum.exe"
Name: "{group}\Uninstall Pendulum"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Pendulum"; Filename: "{app}\Pendulum.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Pendulum.exe"; Description: "Launch Pendulum"; Flags: nowait postinstall skipifsilent

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nSuremised it right. - surmise.it%n%nIt is recommended that you close all other applications before continuing.
