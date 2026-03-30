#ifndef ReleaseDir
  #error "ReleaseDir preprocessor define is required. Example: /DReleaseDir=C:\path\to\publish"
#endif

#ifndef OutputDir
  #define OutputDir AddBackslash(SourcePath) + "..\dist\release\installer"
#endif

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#define MyAppId "{{A0AFD169-7CE2-4D59-9A17-30C4EABF2A91}"
#define MyAppName "Petrichor"
#define MyAppPublisher "Petrichor"
#define MyAppExeName "Petrichor.App.exe"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Petrichor
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
OutputDir={#OutputDir}
OutputBaseFilename=Petrichor-Setup-{#AppVersion}
DisableProgramGroupPage=yes
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#ReleaseDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Petrichor"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Petrichor"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,Petrichor}"; Flags: nowait postinstall skipifsilent
