; LiveBackground installer (Inno Setup 6).
; Build with build.ps1, or directly:
;   ISCC.exe /DSourceDir=dist /DOutputDir=dist\installer installer.iss

#define MyAppName "LiveBackground"
#define MyAppVersion "1.6.3"
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
VersionInfoVersion=1.6.3.0
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
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"

[Tasks]
Name: "autostart"; Description: "Start LiveBackground when I sign in"; GroupDescription: "Startup:"; Flags: checkedonce
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

[Icons]
Name: "{group}\LiveBackground settings"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--settings"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\LiveBackground"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\LiveBackground"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppCliName}"; Parameters: "install-task"; WorkingDir: "{app}"; Flags: runasoriginaluser runhidden waituntilterminated; StatusMsg: "Creating the logon and watchdog tasks..."; Tasks: autostart
Filename: "{app}\{#MyAppExeName}"; Parameters: "--startup"; WorkingDir: "{app}"; Flags: runasoriginaluser nowait postinstall skipifsilent; Description: "Start LiveBackground now"

[UninstallRun]
; Runs the full uninstall command: stops the wallpaper, removes the logon
; and watchdog tasks, and restores the previous desktop wallpaper and lock
; screen (as the README promises). Runs elevated, which is fine for all three
; steps; the lock-screen WinRT call was verified to work elevated.
Filename: "{app}\{#MyAppCliName}"; Parameters: "uninstall"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated; RunOnceId: "UninstallLiveBackground"

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
