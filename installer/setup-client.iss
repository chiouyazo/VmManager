[Setup]
AppName=VM Manager Client
AppVersion={#AppVersion}
AppVerName=VM Manager Client {#AppVersion}
AppPublisher=VM Manager
AppPublisherURL=https://github.com/chiouyazo/VmManager
AppSupportURL=https://github.com/chiouyazo/VmManager/issues
AppUpdatesURL=https://github.com/chiouyazo/VmManager/releases
DefaultDirName={autopf}\VmManager Client
DefaultGroupName=VM Manager Client
OutputBaseFilename=VmManager-Setup-Client
OutputDir=Output
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\VmManager.exe
UninstallDisplayName=VM Manager Client

[Files]
Source: "..\publish-client\VmManager.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\VM Manager Client"; Filename: "{app}\VmManager.exe"
Name: "{commondesktop}\VM Manager Client"; Filename: "{app}\VmManager.exe"
Name: "{group}\Uninstall VM Manager Client"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\VmManager.exe"; Description: "Launch VM Manager Client"; Flags: nowait postinstall skipifsilent