#define DefaultApiPort "18275"
#define DefaultRdpPort "13389"

[Setup]
AppName=VmManager Agent
AppVersion=1.0.0
AppPublisher=VmManager
DefaultDirName={autopf}\VmManager Agent
DefaultGroupName=VmManager Agent
OutputDir=Output
OutputBaseFilename=VmManager-Setup-Agent
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\VmManager.Agent.exe

[Files]
Source: "..\..\publish-agent\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Tasks]
Name: "installservice"; Description: "Install as Windows service (starts automatically)"; GroupDescription: "Service:";
Name: "firewallapi"; Description: "Allow API + RDP port {#DefaultApiPort} (management and VM connections)"; GroupDescription: "Firewall rules:";
Name: "firewallrdp"; Description: "Allow standalone RDP proxy port {#DefaultRdpPort} (optional, set to 0 in appsettings.json to disable)"; GroupDescription: "Firewall rules:";

[Run]
Filename: "sc.exe"; Parameters: "create ""VmManager.Agent"" binPath= ""{app}\VmManager.Agent.exe"" start= auto"; Flags: runhidden; Tasks: installservice
Filename: "sc.exe"; Parameters: "description ""VmManager.Agent"" ""VmManager Remote Agent for Hyper-V VM management"""; Flags: runhidden; Tasks: installservice
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""VmManager Agent API"" dir=in action=allow protocol=tcp localport={#DefaultApiPort}"; Flags: runhidden; Tasks: firewallapi
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""VmManager Agent RDP"" dir=in action=allow protocol=tcp localport={#DefaultRdpPort}"; Flags: runhidden; Tasks: firewallrdp
Filename: "sc.exe"; Parameters: "start ""VmManager.Agent"""; Flags: runhidden; Tasks: installservice

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop ""VmManager.Agent"""; Flags: runhidden; RunOnceId: "StopService"
Filename: "sc.exe"; Parameters: "delete ""VmManager.Agent"""; Flags: runhidden; RunOnceId: "DeleteService"
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""VmManager Agent API"""; Flags: runhidden; RunOnceId: "DeleteFwApi"
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""VmManager Agent RDP"""; Flags: runhidden; RunOnceId: "DeleteFwRdp"
