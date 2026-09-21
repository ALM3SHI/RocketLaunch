; RocketLaunch Inno Setup Script
; Build locally: iscc installer\RocketLaunch.iss
; Build in CI:   iscc /DPublishDir="C:\abs\path\publish" /DOutputDir="C:\abs\path\Output" installer\RocketLaunch.iss

#define AppName    "RocketLaunch"
#define AppVersion "1.0.0"
#define AppPublisher "RocketLaunch"
#define AppURL     "https://github.com/ALM3SHI/RocketLaunch"
#define AppExeName "LauncherApp.exe"

; PublishDir and OutputDir can be overridden via /D on the command line (used by CI).
; Defaults to relative paths for local builds.
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "Output"
#endif

[Setup]
AppId={{A7F3C241-8B5D-4E2A-9C1F-3D6E7B8A0F12}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir={#OutputDir}
OutputBaseFilename=RocketLaunch-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline

; Minimum Windows version: Windows 10
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Start RocketLaunch with Windows"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; PublishDir is set via /D in CI, or defaults to ..\publish for local builds
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
; Set the server URL as a user-level environment variable so the launcher
; finds it without needing a system restart.
Root: HKCU; Subkey: "Environment"; ValueType: string; ValueName: "ROCKETLAUNCH_SERVER"; \
  ValueData: "{code:GetServerUrl}"; Flags: uninsdeletevalue

[Code]
var
  ServerUrlPage: TInputQueryWizardPage;
  ServerUrlValue: String;

// ── .NET 8 check ──────────────────────────────────────────────
function IsDotNet8Installed(): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(
    HKLM,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost',
    'Version',
    Version);
  if Result then
    Result := CompareStr(Version, '8.0.0') >= 0;
end;

function InitializeSetup(): Boolean;
var
  ErrCode: Integer;
begin
  if not IsDotNet8Installed() then
  begin
    if MsgBox(
      '.NET 8 Runtime is required but not found.' + #13#10 +
      'Download it now from Microsoft?',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open',
        'https://dotnet.microsoft.com/download/dotnet/8.0',
        '', '', SW_SHOWNORMAL, ewNoWait, ErrCode);
    end;
    Result := False;
  end else
    Result := True;
end;

// ── Server URL wizard page ────────────────────────────────────
procedure InitializeWizard();
var
  Existing: String;
begin
  ServerUrlPage := CreateInputQueryPage(
    wpSelectDir,
    'Presence Server URL',
    'Connect RocketLaunch to your server',
    'Paste the URL of your deployed PresenceServer below.' + #13#10 +
    'If you are running the server locally, leave the default.' + #13#10 +
    '(You can change this later via the ROCKETLAUNCH_SERVER environment variable.)');

  ServerUrlPage.Add('Server URL:', False);

  Existing := '';
  RegQueryStringValue(HKCU, 'Environment', 'ROCKETLAUNCH_SERVER', Existing);
  if Existing = '' then Existing := 'http://localhost:5000';
  ServerUrlPage.Values[0] := Existing;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = ServerUrlPage.ID then
  begin
    ServerUrlValue := Trim(ServerUrlPage.Values[0]);
    if ServerUrlValue = '' then
    begin
      MsgBox('Please enter a server URL.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function GetServerUrl(Param: String): String;
begin
  Result := ServerUrlValue;
end;

