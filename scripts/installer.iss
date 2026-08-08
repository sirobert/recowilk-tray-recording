; Inno Setup 6 — instalator per-user (bez uprawnień administratora)
; Nie usuwa nagrań użytkownika przy deinstalacji.

#define MyAppName "Meeting Audio Recorder"
#define MyAppVersion "1.1.1"
#define MyAppPublisher "MeetingAudioRecorder"
#define MyAppExeName "MeetingAudioRecorder.exe"

[Setup]
AppId={{A7B3C2D1-4E5F-6789-ABCD-EF0123456789}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\MeetingAudioRecorder
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\publish\installer
OutputBaseFilename=MeetingAudioRecorder-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Utwórz ikonę na pulpicie"; GroupDescription: "Dodatkowe skróty:"; Flags: unchecked
Name: "autostart"; Description: "Uruchamiaj po zalogowaniu do Windows"; GroupDescription: "Autostart:"; Flags: checkedonce

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Odinstaluj {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MeetingAudioRecorder"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Uruchom {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Usuń tylko pliki programu — NIE usuwaj %LOCALAPPDATA%\MeetingAudioRecorder (ustawienia, logi, temp)
; NIE usuwaj folderu nagrań użytkownika (Dokumenty\Nagrania spotkań)

[Code]
function InitializeUninstall(): Boolean;
begin
  Result := True;
  MsgBox('Nagrania użytkownika oraz folder %LOCALAPPDATA%\MeetingAudioRecorder nie zostaną usunięte.', mbInformation, MB_OK);
end;
