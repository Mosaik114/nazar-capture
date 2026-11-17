; Inno Setup script for Nazar Capture (Windows, per user, no admin rights).
;
; Built by build.ps1 -Installer, which passes:
;   /DAppVersion=<version from Cargo.toml>
;   /DSourceDir=<the build folder bin\ with both programs>
;   /DOutputDir=<where Nazar-Capture-Setup-<version>.exe goes>
;
; Installs to %LOCALAPPDATA%\Programs\NazarCapture, the same folder, Start-menu entry and autostart entry as
; NazarCapture.exe --install (the ZIP and the in-app update), so either one updates the other's installation.
; Settings (%LOCALAPPDATA%\NazarCapture: settings.ini, logs, update state) are never written here; the
; uninstaller offers to delete them but does not do so on its own. A silent uninstall asks nothing and deletes
; nothing. Screenshots and recordings are never touched.
;
; The in-app update (engine\Updates\Updates.cs) runs this setup with
;   /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTAPP=1
; /RESTARTAPP=1 starts the app again (in the tray) after the silent install, keeps the autostart entry as the
; app's own setting says, and starts the old copy again should the install fail after it was closed.
;
; Started with administrator rights, the setup warns (double-click) or stops (silent, unless /ALLOWADMIN=1 or
; /RESTARTAPP=1); see InitializeSetup.
;
; Test builds only (packaging\smoke_test.ps1 on a developer machine): /DAppIdGuid, /DAppDisplayName,
; /DSettingsDir and /DAppMutexes give the setup its own Windows "Apps" entry, shortcut and autostart names,
; settings folder and the mutexes of an isolated test instance (NAZAR_CAPTURE_TEST_INSTANCE), so it never
; meets a real installation. Release builds use the defaults below.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\bin"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

; Folders and the programs keep the technical name, as in src\install.rs.
#define AppName "NazarCapture"
; The name under which Nazar Capture is found: Start menu, desktop shortcut, Windows "Apps", the setup wizard
; and the autostart entry (VALUE_NAME in src\autostart.rs).
#ifndef AppDisplayName
  #define AppDisplayName "Nazar Capture"
#endif
#ifndef AppIdGuid
  #define AppIdGuid "6458E5CD-9A10-4E94-B0ED-57D2DC610D1D"
#endif
; Where the app keeps settings.ini (contract\contract.json: settings).
#ifndef SettingsDir
  #define SettingsDir "{localappdata}\NazarCapture"
#endif
#define AppExe "NazarCapture.exe"
#define EngineExe "NazarCapture.Engine.exe"
#define AppUrl "https://github.com/Mosaik114/nazar-capture"
; The single-instance mutexes of host and engine (contract\contract.json): setup and uninstall wait for them.
#ifndef AppMutexes
  #define AppMutexes "Local\NazarCapture.Host,Local\NazarCapture.Engine"
#endif

[Setup]
AppId={{{#AppIdGuid}}
AppName={#AppDisplayName}
AppVersion={#AppVersion}
AppVerName={#AppDisplayName} {#AppVersion}
AppPublisher={#AppDisplayName}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
; Per-user install: {autopf} is %LOCALAPPDATA%\Programs without admin rights.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
; Always the default folder: the in-app update installs there (/DIR= still works for tests).
DisableDirPage=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=Nazar-Capture-Setup-{#AppVersion}
SetupIconFile=..\assets\capture.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppDisplayName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
#ifdef RequiredInnoVersion
  ; CI (build.ps1 -InnoVersion): only the pinned compiler builds a release.
  #if DecodeVer(VER) != RequiredInnoVersion
    #pragma message "Inno Setup " + DecodeVer(VER) + " compiles this, but build.ps1 -InnoVersion asks for " + RequiredInnoVersion
    #error Wrong Inno Setup version: this compiler is not the one build.ps1 -InnoVersion names (the pinned one)
  #endif
#endif
#if VER >= EncodeVer(6,7,0)
; Inno Setup 6.7+ turns on Windows' RedirectionGuard for the setup process, and the app the setup starts (the
; [Run] entries below, DeinitializeSetup) inherits it: that copy of Nazar Capture then cannot follow any junction or
; symbolic link a user made without admin rights (error 448, "untrusted mount point"), e.g. ffmpeg from winget or
; Scoop, or a Pictures folder moved with a link. Inno Setup 6.7.4/7.1.1 stopped the inheritance only for setups that
; ask for admin rights, not for PrivilegesRequired=lowest. This setup is meant to run without admin rights and writes
; only below the user's own %LOCALAPPDATA%, so the guard protects no privilege boundary here; it is switched off.
; Started with admin rights anyway, the setup warns first, or stops when silent (InitializeSetup below).
; packaging\smoke_test.ps1 checks that the app started by the setup runs without it.
; Remove this once Inno Setup stops handing the guard on for PrivilegesRequired=lowest too (check its whatsnew:
; "RedirectionGuard" ... "child processes"); then the guard can stay on for the setup itself.
RedirectionGuard=no
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
english.AutostartTask=Start Nazar Capture when I sign in to Windows
german.AutostartTask=Nazar Capture bei der Windows-Anmeldung starten
english.ExtraOptions=Options:
german.ExtraOptions=Optionen:
english.AppBusy=A recording or area selection is running in Nazar Capture. Please finish it first, then try again.
german.AppBusy=In Nazar Capture läuft gerade eine Aufnahme oder Bereichsauswahl. Bitte sie zuerst beenden und es dann erneut versuchen.
english.AppStillRunning=Nazar Capture did not close in time. Please quit it from its tray icon and try again.
german.AppStillRunning=Nazar Capture hat sich nicht rechtzeitig beendet. Bitte über das Symbol im Infobereich beenden und es erneut versuchen.
english.ElevatedWarning=This setup is running with administrator rights. Nazar Capture does not need them: it installs for you alone.%n%nSafest: choose No and start the setup again with a normal double-click (not "Run as administrator").%n%nChoose Yes to install anyway.
german.ElevatedWarning=Dieses Setup läuft mit Administratorrechten. Nazar Capture braucht sie nicht: Es wird nur für dich installiert.%n%nAm sichersten: Nein wählen und das Setup noch einmal mit einem normalen Doppelklick starten (nicht „Als Administrator ausführen“).%n%nMit Ja wird trotzdem installiert.
english.RemoveUserData=Also delete your Nazar Capture settings and logs?%n%nThey are in the NazarCapture folder under AppData\Local. Screenshots and recordings stay either way. Choose No to keep the settings for a later reinstall.
german.RemoveUserData=Auch die Einstellungen und Logs von Nazar Capture löschen?%n%nSie liegen im Ordner NazarCapture unter AppData\Local. Screenshots und Aufnahmen bleiben in jedem Fall erhalten. Mit Nein bleiben die Einstellungen für eine spätere Neuinstallation erhalten.

[Tasks]
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:ExtraOptions}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:ExtraOptions}"; Flags: unchecked

[InstallDelete]
; Debug files a copy installed from a build folder may have left; the release has only the two programs.
Type: files; Name: "{app}\NazarCapture.pdb"
Type: files; Name: "{app}\NazarCapture.Engine.pdb"
Type: files; Name: "{app}\NazarCapture.Engine.xml"

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\{#EngineExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Same file name as the shortcut NazarCapture.exe --install writes, so there is only ever one.
Name: "{autoprograms}\{#AppDisplayName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppDisplayName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The same entry the app writes (src\autostart.rs), so there is only ever one.
; --background: at sign-in Nazar Capture starts silently in the tray.
; The in-app update follows the app's own setting instead (CurStepChanged).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppDisplayName}"; ValueData: "{code:AutostartCommand}"; Tasks: autostart; Check: not RestartRequested

[Run]
; Started silently in the tray, as at sign-in; the tray icon opens the window.
Filename: "{app}\{#AppExe}"; Parameters: "--background"; Description: "{cm:LaunchProgram,{#AppDisplayName}}"; Flags: nowait postinstall skipifsilent
; After the in-app update (/RESTARTAPP=1) the new version starts in the tray, as the old one ran.
Filename: "{app}\{#AppExe}"; Parameters: "--background"; Flags: nowait; Check: RestartRequested

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  { NazarCapture.exe --quit: 11 = refused, a recording or area selection is running (Exit in src\main.rs). }
  QuitRefused = 11;
  QuitTimeoutMs = 15000;

var
  TasksInitialized: Boolean;
  { The copy in the install folder was closed for this setup (and did not run again yet). }
  AppClosed: Boolean;
  { The files are installed. }
  Installed: Boolean;

{ The settings file of the app (contract\contract.json: settings). }
function SettingsFile(): String;
begin
  Result := ExpandConstant('{#SettingsDir}\settings.ini');
end;

{ /RESTARTAPP=1: the in-app update runs this setup silently; the app starts again afterwards. }
function RestartRequested(): Boolean;
begin
  Result := ExpandConstant('{param:RESTARTAPP|0}') = '1';
end;

{ Started with administrator rights ("Run as administrator", an elevated prompt, UAC switched off). Nazar Capture
  installs per user and never needs them, and RedirectionGuard is off for this setup (see [Setup]): an elevated
  process writing into a user's folders without the guard is exactly what the guard is for. So:
  - double-clicked (a person is there): a plain warning; Yes installs anyway, so nobody is locked out;
  - silent: stops (exit code 1, the reason in the log), unless /ALLOWADMIN=1 says it is meant (CI runners are
    elevated) or it is the app's own update (/RESTARTAPP=1): a copy that runs elevated already starts it that way,
    and refusing would only leave that user without updates. }
function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsAdmin() then
    Exit;
  if ExpandConstant('{param:ALLOWADMIN|0}') = '1' then
  begin
    Log('Running with administrator rights: allowed by /ALLOWADMIN=1.');
    Exit;
  end;
  if WizardSilent() then
  begin
    if RestartRequested() then
    begin
      Log('Running with administrator rights: the app''s own update (/RESTARTAPP=1), installing.');
      Exit;
    end;
    Log('Running with administrator rights and silently: stopped. Start it without them, or pass /ALLOWADMIN=1.');
    Result := False;
    Exit;
  end;
  Result := MsgBox(CustomMessage('ElevatedWarning'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  if not Result then
    Log('Running with administrator rights: the user chose not to install.');
end;

{ The autostart command, as the app writes it (src\autostart.rs). }
function AutostartCommand(Param: String): String;
begin
  Result := '"' + ExpandConstant('{app}\{#AppExe}') + '" --background';
end;

{ The app's own autostart setting: on unless the file says 0 (missing file or value: the default, on). }
function StoredAutostart(): Boolean;
begin
  Result := StrToIntDef(Trim(GetIniString('capture', 'autostart', '1', SettingsFile())), 1) <> 0;
end;

{ The app writes its autostart entry from settings.ini at every start, so the choice made here is stored
  there too; otherwise the next start would undo it. Written only when it differs. }
procedure StoreAutostart(Enabled: Boolean);
var
  Value: String;
begin
  if StoredAutostart() = Enabled then
    Exit;
  if Enabled then
    Value := '1'
  else
    Value := '0';
  ForceDirectories(ExtractFileDir(SettingsFile()));
  SetIniString('capture', 'autostart', Value, SettingsFile());
end;

{ Only an entry that starts this installation is ours; another copy's entry stays. }
function AutostartIsOurs(): Boolean;
var
  Value: String;
begin
  Result := RegQueryStringValue(HKCU, RunKey, '{#AppDisplayName}', Value) and
    (Pos(Lowercase(ExpandConstant('{app}\{#AppExe}')), Lowercase(Value)) > 0);
end;

procedure RemoveOurAutostart();
begin
  if AutostartIsOurs() then
    RegDeleteValue(HKCU, RunKey, '{#AppDisplayName}');
end;

procedure WriteOurAutostart();
begin
  RegWriteStringValue(HKCU, RunKey, '{#AppDisplayName}', AutostartCommand(''));
end;

// Asks the copy running from the install folder to quit through the app's own quit protocol (host and
// engine) and waits until both have exited. A copy started from another folder is not addressed (its --quit
// is not trusted there) and keeps running. Returns an error text, or '' when nothing of ours runs any more.
function QuitRunningApp(): String;
var
  Exe: String;
  ResultCode, Waited: Integer;
begin
  Result := '';
  Exe := ExpandConstant('{app}\{#AppExe}');
  if not FileExists(Exe) then
    Exit;
  if not Exec(Exe, '--quit', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Exit;
  if ResultCode = QuitRefused then
  begin
    Result := CustomMessage('AppBusy');
    Exit;
  end;
  { 0: our copy quit, or nothing ran. Anything else: a copy from another folder runs; leave it alone. }
  if ResultCode <> 0 then
    Exit;
  AppClosed := True;
  Waited := 0;
  while CheckForMutexes('{#AppMutexes}') do
  begin
    if Waited >= QuitTimeoutMs then
    begin
      Result := CustomMessage('AppStillRunning');
      Exit;
    end;
    Sleep(100);
    Waited := Waited + 100;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := QuitRunningApp();
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  { The autostart box starts as the app's own setting, so an update keeps what was chosen in the app. }
  if (CurPageID = wpSelectTasks) and not TasksInitialized then
  begin
    TasksInitialized := True;
    if not StoredAutostart() then
      WizardSelectTasks('!autostart');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    Installed := True;
    if RestartRequested() then
    begin
      { The in-app update: the app's own setting decides, and it stays as it is. }
      if StoredAutostart() then
        WriteOurAutostart()
      else
        RemoveOurAutostart();
      Exit;
    end;
    { Reinstalled without the task: switch autostart off again. }
    if not WizardIsTaskSelected('autostart') then
      RemoveOurAutostart();
    StoreAutostart(WizardIsTaskSelected('autostart'));
  end;
end;

procedure DeinitializeSetup();
var
  Exe: String;
  ResultCode: Integer;
begin
  { The in-app update closed the app but could not install: start the old copy again, so it is not just gone. }
  if RestartRequested() and AppClosed and not Installed then
  begin
    Exe := ExpandConstant('{app}\{#AppExe}');
    if FileExists(Exe) then
      Exec(Exe, '--background', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
  end;
end;

function InitializeUninstall(): Boolean;
var
  Error: String;
begin
  Error := QuitRunningApp();
  Result := Error = '';
  if not Result and not UninstallSilent() then
    MsgBox(Error, mbError, MB_OK);
end;

procedure RemoveUserData();
begin
  DelTree(ExpandConstant('{localappdata}\{#AppName}'), True, True, True);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveOurAutostart();
  { Offered, never forced: a silent uninstall keeps the user's settings. }
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() then
  begin
    if MsgBox(CustomMessage('RemoveUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      RemoveUserData();
  end;
end;
