#ifndef AppVersion
#define AppVersion "0.2.2"
#endif

#define AppName "Keywise"
#define AppPublisher "Epiano7"
#define AppExeName "Keywise.exe"
#define SourceDir AddBackslash(SourcePath) + "..\artifacts\Keywise-win-x64"
#define InstallerOutputDir AddBackslash(SourcePath) + "..\artifacts\installer"

[Setup]
AppId={{B4A7AE18-7F92-4DDF-8C5E-9AF650B0E181}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/Epiano7/Keywise
AppSupportURL=https://github.com/Epiano7/Keywise/issues
AppUpdatesURL=https://github.com/Epiano7/Keywise/releases
DefaultDirName={localappdata}\Programs\Keywise
DefaultGroupName=Keywise
DisableProgramGroupPage=yes
OutputDir={#InstallerOutputDir}
OutputBaseFilename=Keywise-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\Assets\keywise.ico
AppMutex=KeywiseAppMutex
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Keywise"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall Keywise"; Filename: "{uninstallexe}"
Name: "{commondesktop}\Keywise"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Keywise"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
