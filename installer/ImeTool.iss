#ifndef MyAppVersion
  #define MyAppVersion "1.0.7"
#endif
#ifndef PublishDir
  #define PublishDir "..\src\ImeTool\bin\Release\net9.0-windows10.0.17763.0\win-x64\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define MyAppName "ImeTool"
#define MyAppExeName "ImeTool.exe"
#define MyAppPublisher "yixing233"
#define MyAppUrl "https://github.com/yixing233/ImeTool"
#define RuntimeUrl "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/9.0.17/windowsdesktop-runtime-9.0.17-win-x64.exe"
#define RuntimeFileName "windowsdesktop-runtime-9.0.17-win-x64.exe"
#define RuntimeSha256 "8b7a862a148855c0e870a7efabb608682ff378290fa984eb7830a62d5e7a4e57"

[Setup]
AppId={{33EC67D8-9ECF-46E5-BB5C-5C746E4DCA21}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases/latest
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=ImeTool_Windows_x64
SetupIconFile=..\src\ImeTool\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic
CloseApplications=force
RestartApplications=no
UsePreviousAppDir=yes
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=ImeTool Windows x64 Installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
SetupLogging=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "ImeTool"; Flags: uninsdeletevalue

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\ImeTool\Updates"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent; Check: not IsUpdateInstall
Filename: "{app}\{#MyAppExeName}"; Parameters: "--silent"; Flags: nowait; Check: IsUpdateInstall

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function IsUpdateInstall: Boolean;
begin
  Result := CompareText(ExpandConstant('{param:UPDATE|0}'), '1') = 0;
end;

function IsDotNet9DesktopRuntimeInstalled: Boolean;
var
  RuntimeDirectory: String;
  FindRec: TFindRec;
begin
  Result := False;
  RuntimeDirectory := ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if FindFirst(RuntimeDirectory + '\9.*', FindRec) then
  begin
    try
      repeat
        if FileExists(
          RuntimeDirectory + '\' + FindRec.Name +
          '\Microsoft.WindowsDesktop.App.deps.json') then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function CloseRunningImeTool: Boolean;
var
  ResultCode: Integer;
begin
  { A portable copy can run outside the install directory, so Restart Manager cannot find it
    by target-file usage. Close all same-user ImeTool instances explicitly. }
  Result := Exec(
    ExpandConstant('{sys}\taskkill.exe'),
    '/F /T /IM ImeTool.exe',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
  if Result then
    Result := (ResultCode = 0) or (ResultCode = 128);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RuntimePath: String;
  ResultCode: Integer;
begin
  Result := '';
  if not CloseRunningImeTool then
  begin
    Result := '无法关闭正在运行的 ImeTool。请退出旧版本后重试。';
    Exit;
  end;

  if IsDotNet9DesktopRuntimeInstalled then
    Exit;

  try
    DownloadTemporaryFile(
      '{#RuntimeUrl}',
      '{#RuntimeFileName}',
      '{#RuntimeSha256}',
      nil);
  except
    Result := '无法下载 .NET 9 Desktop Runtime：' + GetExceptionMessage;
    Exit;
  end;

  RuntimePath := ExpandConstant('{tmp}\{#RuntimeFileName}');
  if not ShellExec('', RuntimePath, '/install /quiet /norestart', '', SW_SHOW,
    ewWaitUntilTerminated, ResultCode) then
  begin
    Result := '无法启动 .NET 9 Desktop Runtime 安装程序。';
    Exit;
  end;

  if (ResultCode = 1641) or (ResultCode = 3010) then
    NeedsRestart := True
  else if ResultCode <> 0 then
  begin
    Result := Format('.NET 9 Desktop Runtime 安装失败，错误代码：%d。', [ResultCode]);
    Exit;
  end;

  if not IsDotNet9DesktopRuntimeInstalled then
    Result := '.NET 9 Desktop Runtime 安装完成，但系统尚未检测到运行库。';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExistingRunValue: String;
begin
  if CurStep = ssPostInstall then
  begin
    if RegQueryStringValue(HKCU, RunKey, 'ImeTool', ExistingRunValue) then
      RegWriteStringValue(
        HKCU,
        RunKey,
        'ImeTool',
        '"' + ExpandConstant('{app}\{#MyAppExeName}') + '"');
  end;
end;
