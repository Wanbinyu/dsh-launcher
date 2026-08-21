#define MyAppVersion "0.3.5"

[Setup]
AppId={{B42DAA6B-4D4A-4F8E-AE8E-7E2C8C6C8D11}
AppName=dsh-launcher
AppVersion={#MyAppVersion}
AppPublisher=Wanbinyu
AppPublisherURL=https://github.com/Wanbinyu/dsh-launcher
AppSupportURL=https://github.com/Wanbinyu/dsh-launcher/issues
AppUpdatesURL=https://github.com/Wanbinyu/dsh-launcher/releases
DefaultDirName={localappdata}\Programs\dsh-launcher
DefaultGroupName=dsh-launcher
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=dsh-launcher-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\dsh-launcher.ico
ChangesEnvironment=yes
UninstallDisplayIcon={app}\dsh-launcher.exe

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\win-x64-installer\dsh-launcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dsh.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\deepseek.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dsh-launcher.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\install.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\install.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.en.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{app}\dsh.exe"
Type: files; Name: "{app}\deepseek.exe"

[Icons]
Name: "{autoprograms}\dsh-launcher"; Filename: "{app}\dsh-launcher.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\dsh-launcher"; Filename: "{app}\dsh-launcher.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[UninstallRun]
Filename: "{app}\dsh-launcher.exe"; Parameters: "exit"; Flags: runhidden waituntilterminated; RunOnceId: "StopDshLauncher"

[Code]
function NormalizePathEntry(const Value: string): string;
begin
  Result := Trim(Value);
  while (Length(Result) > 3) and (Result[Length(Result)] = '\\') do
    Delete(Result, Length(Result), 1);
end;

function PathContainsEntry(const PathValue, Entry: string): Boolean;
var
  Remaining, Current: string;
  Separator: Integer;
begin
  Result := False;
  Remaining := PathValue;
  while Remaining <> '' do
  begin
    Separator := Pos(';', Remaining);
    if Separator = 0 then
    begin
      Current := Remaining;
      Remaining := '';
    end
    else
    begin
      Current := Copy(Remaining, 1, Separator - 1);
      Delete(Remaining, 1, Separator);
    end;

    if CompareText(NormalizePathEntry(Current), NormalizePathEntry(Entry)) = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure AddToUserPath(const Entry: string);
var
  PathValue: string;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', PathValue) then
    PathValue := '';
  if not PathContainsEntry(PathValue, Entry) then
  begin
    if PathValue = '' then
      PathValue := Entry
    else
      PathValue := Entry + ';' + PathValue;
    RegWriteExpandStringValue(HKCU, 'Environment', 'Path', PathValue);
  end;
end;

procedure RemoveFromUserPath(const Entry: string);
var
  PathValue, Remaining, Current, NewValue: string;
  Separator: Integer;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', PathValue) then
    Exit;

  Remaining := PathValue;
  NewValue := '';
  while Remaining <> '' do
  begin
    Separator := Pos(';', Remaining);
    if Separator = 0 then
    begin
      Current := Remaining;
      Remaining := '';
    end
    else
    begin
      Current := Copy(Remaining, 1, Separator - 1);
      Delete(Remaining, 1, Separator);
    end;

    if CompareText(NormalizePathEntry(Current), NormalizePathEntry(Entry)) <> 0 then
    begin
      if NewValue = '' then
        NewValue := Current
      else
        NewValue := NewValue + ';' + Current;
    end;
  end;

  RegWriteExpandStringValue(HKCU, 'Environment', 'Path', NewValue);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    AddToUserPath(ExpandConstant('{app}'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  LauncherPath: string;
  VersionMS, VersionLS: Cardinal;
begin
  Result := '';
  LauncherPath := ExpandConstant('{app}\dsh-launcher.exe');
  if FileExists(LauncherPath) then
  begin
    if GetVersionNumbers(LauncherPath, VersionMS, VersionLS) and
       ((VersionMS > $00000003) or
        ((VersionMS = $00000003) and (VersionLS >= $00030000))) then
      Exec(LauncherPath, 'exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    else
      Exec(LauncherPath, 'stop', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveFromUserPath(ExpandConstant('{app}'));
end;
