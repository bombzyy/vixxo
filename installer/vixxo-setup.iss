; Vixxo Windows Setup Script (Inno Setup 6)
#define MyAppName "Vixxo"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Vixxo Project"
#define MyAppURL "https://github.com/vixxo/vixxo"
#define MyAppExeName "Vixxo.exe"

[Setup]
AppId={{8B19E524-7F89-49BE-9E46-13CEBF89163D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=Vixxo-Setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Assets\app.ico
SetupIconFile=..\windows\Vixxo.App\Assets\app.ico
CloseApplications=yes
RestartApplications=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsVbCableInstalled(): Boolean;
var
  DeviceFound: Boolean;
begin
  DeviceFound := RegKeyExists(HKEY_LOCAL_MACHINE, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Audio Cable') or
                 RegKeyExists(HKEY_LOCAL_MACHINE, 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\VB:Audio Cable') or
                 RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Enum\ROOT\MEDIA\0000');
  Result := DeviceFound;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not IsVbCableInstalled() then
    begin
      MsgBox('Vixxo requires the VB-CABLE Virtual Audio Device to route your phone microphone to Discord, Zoom, Teams, and OBS.' + #13#10 + #13#10 +
             'VB-CABLE was not detected. Please install it from https://vb-audio.com/Cable/ to enable virtual microphone input in Windows applications.', mbInformation, MB_OK);
    end;
  end;
end;
