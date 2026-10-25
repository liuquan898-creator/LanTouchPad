#define AppVersion "1.0.0"
[Setup]
AppId={{09E9BBBD-567E-4C31-9BE3-434511A6842A}
AppName=LanTouchPad
AppVersion={#AppVersion}
AppPublisher=YuXi
AppPublisherURL=https://github.com/liuquan898-creator/LanTouchPad
DefaultDirName={autopf}\LanTouchPad
DefaultGroupName=LanTouchPad
OutputDir=..\dist
OutputBaseFilename=LanTouchPad-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\lantouchpad.ico
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\LanTouchPad.exe
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start LanTouchPad when I sign in to Windows"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\dist\windows\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LanTouchPad"; Filename: "{app}\LanTouchPad.exe"
Name: "{autodesktop}\LanTouchPad"; Filename: "{app}\LanTouchPad.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LanTouchPad"; ValueData: """{app}\LanTouchPad.exe"" --background"; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "LanTouchPad"; Flags: deletevalue; Check: not WizardIsTaskSelected('autostart')

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LanTouchPad Control"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LanTouchPad Discovery"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""LanTouchPad Control"" dir=in action=allow program=""{app}\LanTouchPad.exe"" protocol=TCP localport=45832 remoteip=LocalSubnet profile=any"; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""LanTouchPad Discovery"" dir=in action=allow program=""{app}\LanTouchPad.exe"" protocol=UDP localport=45831 remoteip=LocalSubnet profile=any"; Flags: runhidden waituntilterminated
Filename: "{app}\LanTouchPad.exe"; Description: "Launch LanTouchPad"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LanTouchPad Control"""; Flags: runhidden; RunOnceId: "RemoveControlFirewall"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LanTouchPad Discovery"""; Flags: runhidden; RunOnceId: "RemoveDiscoveryFirewall"
