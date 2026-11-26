; LiveBackground installer (Inno Setup 6).
; Build via build.ps1 with -OutDir "%LOCALAPPDATA%\LiveBackgroundBuild".
; ISCC is normally called by build.ps1; SourceDir defaults to the local build folder.

#define MyAppName "LiveBackground"
#define MyAppVersion "1.9.0"
#define MyAppPublisher "LiveBackground"
#define MyAppExeName "LiveBackground.exe"
#define MyAppCliName "LiveBackgroundCli.exe"
#define MyAppSettingsExeName "LiveBackgroundSettings.exe"
#define MyAppId "{{8F4E2A1C-6D3B-4A7E-9C21-5B0F7D3E8A64}"

; ISCC is normally called by build.ps1; these are compile-time fallbacks only.
#ifndef SourceDir
  #if GetEnv('LOCALAPPDATA') != ''
    #define SourceDir GetEnv('LOCALAPPDATA') + '\LiveBackgroundBuild'
  #elif GetEnv('USERPROFILE') != ''
    #define SourceDir GetEnv('USERPROFILE') + '\AppData\Local\LiveBackgroundBuild'
  #else
    #define SourceDir 'LiveBackgroundBuild'
  #endif
#endif
#ifndef OutputDir
  #if GetEnv('LOCALAPPDATA') != ''
    #define OutputDir GetEnv('LOCALAPPDATA') + '\LiveBackgroundBuild\installer'
  #elif GetEnv('USERPROFILE') != ''
    #define OutputDir GetEnv('USERPROFILE') + '\AppData\Local\LiveBackgroundBuild\installer'
  #else
    #define OutputDir 'LiveBackgroundBuild\installer'
  #endif
#endif
#ifndef AppIcon
  #define AppIcon "assets\LiveBackground.ico"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
UninstallDisplayName={#MyAppName}
VersionInfoVersion=1.9.0.0
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} installer
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=LiveBackgroundSetup
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
; A flatter, Windows 11-leaning wizard presentation (Inno Setup 6.4+).
WizardStyle=modern dynamic
WizardSizePercent=120
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
; English only: the app and its installer ship in English by design, so there
; is no language to detect and no language dialog to show.
ShowLanguageDialog=no
; Ignore the task choices stored by a previous install. Without this, the
; "checkedonce" history (and, on an upgrade, the saved empty task set) can
; leave the startup and Start Menu checkboxes unchecked. With it, the [Tasks]
; defaults below are always the starting point: autostart and Start Menu on,
; desktop off.
UsePreviousTasks=no

[Languages]
; English only. The app is English-only, so the installer is too.
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start LiveBackground when I sign in"; GroupDescription: "Startup:"
Name: "startmenuicon"; Description: "Create a Start Menu shortcut"; GroupDescription: "{cm:AdditionalIcons}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\LiveBackground.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LiveBackgroundCli.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LiveBackgroundSettings.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LiveBackgroundTray.exe"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceDir}\LiveBackgroundBootstrap.exe"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceDir}\mpv\*"; DestDir: "{app}\mpv"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\tools\ffmpeg\*"; DestDir: "{app}\tools\ffmpeg"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\videos\*"; DestDir: "{app}\videos"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\images\*"; DestDir: "{app}\images"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\assets\licenses\*"; DestDir: "{app}\assets\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD-PARTY.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LiveBackground.cer"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[InstallDelete]
; Earlier installs on a Dutch Windows created the uninstall shortcut under its
; Dutch name ("Verwijder LiveBackground"). Remove it so nothing non-English is
; left behind; the English shortcut is created by [Icons] below.
Type: files; Name: "{group}\Verwijder LiveBackground.lnk"

[Icons]
Name: "{group}\LiveBackground settings"; Filename: "{app}\{#MyAppSettingsExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppSettingsExeName}"; Tasks: startmenuicon
Name: "{group}\LiveBackground"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{autodesktop}\LiveBackground"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; The installer already runs elevated, so clearing any logon/watchdog task left
; by an earlier run costs no extra prompt. install-task also runs elevated here
; (not runasoriginaluser) to avoid Inno Setup's CallSpawnServer failure mode;
; InstallTask resolves the signed-in desktop user so the task still belongs to
; the right account. If this delete is refused, install-task still falls back to
; its own elevation prompt when run outside the installer.
;
; Deliberately NOT gated on the autostart task: on an upgrade where the user
; unticked "Start when I sign in", the old tasks must still be removed or the
; app would keep starting at logon. Deleting a task that does not exist is
; already a no-op (StartupTask.Delete treats "not found" as success), so this is
; safe on a fresh install too.
Filename: "{app}\{#MyAppCliName}"; Parameters: "remove-task"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated; StatusMsg: "Preparing the startup task..."
Filename: "{cmd}"; Parameters: "/c ngen.exe install ""{app}\{#MyAppExeName}"" /nologo >nul 2>&1 & exit /b 0"; WorkingDir: "{win}\Microsoft.NET\Framework64\v4.0.30319"; Flags: runhidden waituntilterminated; StatusMsg: "Optimizing startup performance..."; Check: FileExists(ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\ngen.exe'))
Filename: "{cmd}"; Parameters: "/c ngen.exe install ""{app}\{#MyAppSettingsExeName}"" /nologo >nul 2>&1 & exit /b 0"; WorkingDir: "{win}\Microsoft.NET\Framework64\v4.0.30319"; Flags: runhidden waituntilterminated; StatusMsg: "Optimizing startup performance..."; Check: FileExists(ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\ngen.exe'))
Filename: "{app}\{#MyAppCliName}"; Parameters: "install-task"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated; StatusMsg: "Creating the logon and watchdog tasks..."; Tasks: autostart; Check: ShouldInstallAutostartTask
Filename: "{sys}\schtasks.exe"; Parameters: "/run /tn ""{#MyAppName} (early startup)"""; WorkingDir: "{app}"; Flags: runhidden nowait postinstall skipifsilent; Description: "Start LiveBackground now"; Tasks: autostart; Check: ShouldInstallAutostartTask
Filename: "{app}\README.txt"; Description: "View the README"; Flags: postinstall shellexec skipifsilent unchecked nowait
; Silent installs skip the finish page/postinstall checkbox, so start the
; app explicitly there too (unless startup was deselected).
Filename: "{sys}\schtasks.exe"; Parameters: "/run /tn ""{#MyAppName} (early startup)"""; WorkingDir: "{app}"; Flags: runhidden nowait skipifnotsilent; Tasks: autostart; Check: ShouldInstallAutostartTask

[UninstallRun]
; Remove the NGen native image while the exe is still present.
Filename: "{cmd}"; Parameters: "/c ngen.exe uninstall ""{app}\{#MyAppExeName}"" /nologo >nul 2>&1 & exit /b 0"; WorkingDir: "{win}\Microsoft.NET\Framework64\v4.0.30319"; Flags: runhidden waituntilterminated; RunOnceId: "NgenUninstallLiveBackground"; Check: FileExists(ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\ngen.exe'))
Filename: "{cmd}"; Parameters: "/c ngen.exe uninstall ""{app}\{#MyAppSettingsExeName}"" /nologo >nul 2>&1 & exit /b 0"; WorkingDir: "{win}\Microsoft.NET\Framework64\v4.0.30319"; Flags: runhidden waituntilterminated; RunOnceId: "NgenUninstallLiveBackgroundSettings"; Check: FileExists(ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\ngen.exe'))
; Runs the full uninstall command: stops the wallpaper, removes the logon
; and watchdog tasks, restores the previous desktop wallpaper and lock screen
; (as the README promises), and removes the app-managed cache and logs.
; --remove never touches the user's videos, pictures or settings, and the
; backup folder is kept automatically when a restore did not succeed.
; Runs elevated, which is fine for all these steps; the lock-screen WinRT
; call was verified to work elevated.
Filename: "{app}\{#MyAppCliName}"; Parameters: "uninstall --remove"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated; RunOnceId: "UninstallLiveBackground"

[Code]
var
  IsUpgradeInstall: Boolean;
  PreviousAutostartEnabled: Boolean;
  AutostartTaskAdjusted: Boolean;

function TaskExists(const TaskName: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\schtasks.exe'),
    '/query /tn "' + TaskName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function ShouldInstallAutostartTask: Boolean;
begin
  if not WizardIsTaskSelected('autostart') then
  begin
    Result := False;
    Exit;
  end;
  if IsUpgradeInstall and not PreviousAutostartEnabled then
  begin
    Result := False;
    Exit;
  end;
  Result := True;
end;

{ Detect the upgrade before the task page is shown so CurPageChanged can
  untick autostart when the previous install had no startup task. }
procedure DetectUpgradeInstall;
var
  Cli: String;
begin
  IsUpgradeInstall := False;
  PreviousAutostartEnabled := False;
  AutostartTaskAdjusted := False;
  { The app constant is not initialized yet; the destination edit already
    holds the default/previous path, which is where an old copy would be. }
  Cli := AddBackslash(WizardForm.DirEdit.Text) + '{#MyAppCliName}';
  if FileExists(Cli) then
  begin
    IsUpgradeInstall := True;
    PreviousAutostartEnabled := TaskExists('LiveBackground (early startup)');
  end;
end;

procedure InitializeWizard;
begin
  DetectUpgradeInstall;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectTasks) and IsUpgradeInstall and (not PreviousAutostartEnabled) and (not AutostartTaskAdjusted) then
  begin
    WizardSelectTasks('!autostart');
    AutostartTaskAdjusted := True;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Cli: String;
  ResultCode: Integer;
begin
  Result := '';
  Cli := ExpandConstant('{app}\{#MyAppCliName}');
  if FileExists(Cli) then
  begin
    { A /DIR override can point at a different folder than the wizard edit
      used for the early check. Re-detect here if that happened; the task-page
      decision has already been made, so do not clear it here. }
    if not IsUpgradeInstall then
    begin
      IsUpgradeInstall := True;
      PreviousAutostartEnabled := TaskExists('LiveBackground (early startup)');
    end;
    { Stop an older copy before its files are replaced. }
    Exec(Cli, 'stop', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

