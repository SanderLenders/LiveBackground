; LiveBackground installer (Inno Setup 6).
; Build with build.ps1, or directly:
;   ISCC.exe /DSourceDir=dist /DOutputDir=dist\installer installer.iss

#define MyAppName "LiveBackground"
#define MyAppVersion "1.8.6"
#define MyAppPublisher "LiveBackground"
#define MyAppExeName "LiveBackground.exe"
#define MyAppCliName "LiveBackgroundCli.exe"
#define MyAppId "{{8F4E2A1C-6D3B-4A7E-9C21-5B0F7D3E8A64}"

#ifndef SourceDir
  #define SourceDir "dist"
#endif
#ifndef OutputDir
  #define OutputDir "dist\installer"
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
VersionInfoVersion=1.8.6.0
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
PrivilegesRequiredOverridesAllowed=commandline dialog
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
Source: "{#SourceDir}\mpv\*"; DestDir: "{app}\mpv"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\tools\ffmpeg\*"; DestDir: "{app}\tools\ffmpeg"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\videos\*"; DestDir: "{app}\videos"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\images\*"; DestDir: "{app}\images"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\assets\licenses\*"; DestDir: "{app}\assets\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "{#SourceDir}\THIRD-PARTY.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LiveBackground.cer"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[InstallDelete]
; Earlier installs on a Dutch Windows created the uninstall shortcut under its
; Dutch name ("Verwijder LiveBackground"). Remove it so nothing non-English is
; left behind; the English shortcut is created by [Icons] below.
Type: files; Name: "{group}\Verwijder LiveBackground.lnk"

[Icons]
Name: "{group}\LiveBackground settings"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--settings"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{group}\LiveBackground"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{autodesktop}\LiveBackground"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; The installer already runs elevated, so clearing any logon/watchdog task left
; by an earlier run costs no extra prompt. The unelevated install-task below can
; then create the tasks fresh - a normal user may create a new task but not
; overwrite one an elevated run created - so an update no longer asks for
; permission a second time. If this delete is refused, install-task still falls
; back to its own elevation prompt.
;
; Deliberately NOT gated on the autostart task: on an upgrade where the user
; unticked "Start when I sign in", the old tasks must still be removed or the
; app would keep starting at logon. Deleting a task that does not exist is
; already a no-op (StartupTask.Delete treats "not found" as success), so this is
; safe on a fresh install too.
Filename: "{app}\{#MyAppCliName}"; Parameters: "remove-task"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated; StatusMsg: "Preparing the startup task..."
Filename: "{app}\{#MyAppCliName}"; Parameters: "install-task"; WorkingDir: "{app}"; Flags: runasoriginaluser runhidden waituntilterminated; StatusMsg: "Creating the logon and watchdog tasks..."; Tasks: autostart
Filename: "{app}\{#MyAppExeName}"; Parameters: "--startup"; WorkingDir: "{app}"; Flags: runasoriginaluser nowait postinstall skipifsilent; Description: "Start LiveBackground now"

[UninstallRun]
; Runs the full uninstall command: stops the wallpaper, removes the logon
; and watchdog tasks, restores the previous desktop wallpaper and lock screen
; (as the README promises), and removes the app-managed cache and logs.
; --remove never touches the user's videos, pictures or settings, and the
; backup folder is kept automatically when a restore did not succeed.
; Runs elevated, which is fine for all these steps; the lock-screen WinRT
; call was verified to work elevated.
Filename: "{app}\{#MyAppCliName}"; Parameters: "uninstall --remove"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated; RunOnceId: "UninstallLiveBackground"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Cli: String;
  ResultCode: Integer;
begin
  Result := '';
  Cli := ExpandConstant('{app}\{#MyAppCliName}');
  if FileExists(Cli) then
  begin
    { Stop an older copy before its files are replaced. }
    Exec(Cli, 'stop', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

