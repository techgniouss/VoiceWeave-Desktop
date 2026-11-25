; Inno Setup script for VoiceWeave. Built in CI: iscc /DAppVersion=1.2.3 desktop\installer\VoiceWeave.iss
; Expects the self-contained publish output in desktop\publish.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{B7E3C1D4-5A2F-4E8B-9C61-3D7F0A4E9B52}
AppName=VoiceWeave
AppVersion={#AppVersion}
AppPublisher=techgniouss
AppPublisherURL=https://github.com/techgniouss/VoiceWeave-Desktop
AppSupportURL=https://github.com/techgniouss/VoiceWeave-Desktop/issues
; Per-user install: no administrator prompt, and the app runs as the normal user so it can type into normal apps.
PrivilegesRequired=lowest
DefaultDirName={autopf}\VoiceWeave
DefaultGroupName=VoiceWeave
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=VoiceWeaveSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\VoiceWeave.App\Assets\app.ico
LicenseFile=..\..\LICENSE
UninstallDisplayName=VoiceWeave
UninstallDisplayIcon={app}\VoiceWeave.exe
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"
Name: "startup"; Description: "Start VoiceWeave when I sign in to Windows (you can turn this off later in Windows Settings, Apps, Startup)"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "..\licenses\THIRD-PARTY.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\VoiceWeave"; Filename: "{app}\VoiceWeave.exe"
Name: "{autodesktop}\VoiceWeave"; Filename: "{app}\VoiceWeave.exe"; Tasks: desktopicon

; A fresh install adds VoiceWeave to the per-user startup list (the same entry the in-app switch uses), so it shows under
; Windows Settings > Apps > Startup and Task Manager > Startup apps, where it can be turned off. An update never touches
; it, so a user who turned it off stays off. The entry is removed on uninstall either way.
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VoiceWeave"; ValueData: """{app}\VoiceWeave.exe"" --background"; Flags: uninsdeletevalue; Tasks: startup; Check: IsFreshInstall
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "VoiceWeave"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\VoiceWeave.exe"; Description: "Start VoiceWeave now"; Flags: nowait postinstall skipifsilent
; An update started from inside the app runs silently; bring the new version back up afterwards.
Filename: "{app}\VoiceWeave.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent

[Code]
var
  AlreadyInstalled: Boolean;

function InitializeSetup(): Boolean;
begin
  AlreadyInstalled := RegKeyExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B7E3C1D4-5A2F-4E8B-9C61-3D7F0A4E9B52}_is1');
  Result := True;
end;

function IsFreshInstall: Boolean;
begin
  Result := not AlreadyInstalled;
end;

// Downloaded models are large, so ask before deleting them (a silent uninstall keeps them).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if MsgBox('Also delete the downloaded speech models, your dictionary and settings?' + #13#10 +
              '(They are in ' + ExpandConstant('{localappdata}\VoiceWeave') + ')',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{localappdata}\VoiceWeave'), True, True, True);
end;
