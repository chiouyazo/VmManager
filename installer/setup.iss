[Setup]
AppName=VM Manager
AppVersion={#AppVersion}
AppVerName=VM Manager {#AppVersion}
AppPublisher=VM Manager
AppPublisherURL=https://github.com/chiouyazo/VmManager
AppSupportURL=https://github.com/chiouyazo/VmManager/issues
AppUpdatesURL=https://github.com/chiouyazo/VmManager/releases
DefaultDirName={autopf}\VmManager
DefaultGroupName=VM Manager
OutputBaseFilename=VmManagerSetup
OutputDir=Output
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\VmManager.exe
UninstallDisplayName=VM Manager

[Files]
Source: "..\publish\VmManager.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\VM Manager"; Filename: "{app}\VmManager.exe"
Name: "{commondesktop}\VM Manager"; Filename: "{app}\VmManager.exe"
Name: "{group}\Uninstall VM Manager"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\VmManager.exe"; Description: "Launch VM Manager"; Flags: nowait postinstall skipifsilent
