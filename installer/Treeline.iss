; Treeline installer script (Inno Setup 6)
; Per-user install: no admin/UAC required, installs to %LocalAppData%\Programs\Treeline,
; adds Start-menu + Startup shortcuts (the tray app auto-starts on login), and registers an
; uninstaller in "Installed apps". CloseApplications=yes lets the in-app updater replace a
; running copy (the updater launches this with /SILENT /SUPPRESSMSGBOXES /NORESTART).

#define MyAppName "Treeline"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "lukr-99"
#define MyAppExeName "Treeline.exe"
#define MyAppURL "https://github.com/lukr-99/Treeline"

; Path to the self-contained publish output (passed in via ISCC /D, with a fallback).
#ifndef PublishDir
  #define PublishDir "publish"
#endif

[Setup]
AppId={{75BAEB0A-02AA-4778-92E9-C3B45A7CA49C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\Treeline
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
CloseApplications=yes
RestartApplications=no
OutputDir=.
OutputBaseFilename=Treeline-Setup-{#MyAppVersion}
SetupIconFile=..\src\Treeline.App\wwwroot\assets\treeline.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
LicenseFile=..\LICENSE.md

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start Treeline automatically when I sign in"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: startup
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
