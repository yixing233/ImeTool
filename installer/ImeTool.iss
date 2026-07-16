#ifndef MyAppVersion
  #define MyAppVersion "1.0.24"
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
#define RuntimeFallbackUrl "https://dotnetcli.azureedge.net/dotnet/WindowsDesktop/9.0.17/windowsdesktop-runtime-9.0.17-win-x64.exe"
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
LicenseFile=..\LICENSE
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
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "ImeTool"; Flags: uninsdeletevalue

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\ImeTool\Updates"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent; Check: ShouldLaunchManualInstall
Filename: "{app}\{#MyAppExeName}"; Parameters: "--silent"; Flags: nowait; Check: ShouldLaunchUpdateInstall

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

var
  RuntimeRestartRequired: Boolean;

function IsUpdateInstall: Boolean;
begin
  Result := CompareText(ExpandConstant('{param:UPDATE|0}'), '1') = 0;
end;

function ShouldLaunchManualInstall: Boolean;
begin
  Result := (not IsUpdateInstall) and (not RuntimeRestartRequired);
end;

function ShouldLaunchUpdateInstall: Boolean;
begin
  Result := IsUpdateInstall and (not RuntimeRestartRequired);
end;

function IsDotNet9DesktopRuntimeInstalled: Boolean;
var
  DotNetRoot: String;
  RuntimeDirectory: String;
  CoreRuntimeDirectory: String;
  HostFxrDirectory: String;
  CandidateDirectory: String;
  CandidateVersion: String;
  FindRec: TFindRec;
  HostFxrFound: Boolean;
  CoreRuntimeFound: Boolean;
begin
  Result := False;
  DotNetRoot := ExpandConstant('{pf64}\dotnet');
  if not FileExists(DotNetRoot + '\dotnet.exe') then
  begin
    Log('.NET prerequisite check: x64 dotnet host was not found.');
    Exit;
  end;

  { A stale shared-framework folder is not enough to launch a framework-dependent app.
    Verify the x64 hostfxr and both the Desktop and Core framework payloads. }
  HostFxrFound := False;
  HostFxrDirectory := DotNetRoot + '\host\fxr';
  if FindFirst(HostFxrDirectory + '\9.*', FindRec) then
  begin
    try
      repeat
        if FileExists(HostFxrDirectory + '\' + FindRec.Name + '\hostfxr.dll') then
        begin
          HostFxrFound := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;

  if not HostFxrFound then
  begin
    Log('.NET prerequisite check: a usable x64 .NET 9 hostfxr was not found.');
    Exit;
  end;

  RuntimeDirectory := DotNetRoot + '\shared\Microsoft.WindowsDesktop.App';
  CoreRuntimeDirectory := DotNetRoot + '\shared\Microsoft.NETCore.App';
  CoreRuntimeFound := False;
  if FindFirst(CoreRuntimeDirectory + '\9.*', FindRec) then
  begin
    try
      repeat
        if FileExists(CoreRuntimeDirectory + '\' + FindRec.Name + '\System.Private.CoreLib.dll') then
        begin
          CoreRuntimeFound := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;

  if not CoreRuntimeFound then
  begin
    Log('.NET prerequisite check: a usable x64 Microsoft.NETCore.App 9.x was not found.');
    Exit;
  end;

  if FindFirst(RuntimeDirectory + '\9.*', FindRec) then
  begin
    try
      repeat
        CandidateVersion := FindRec.Name;
        CandidateDirectory := RuntimeDirectory + '\' + CandidateVersion;
        if FileExists(CandidateDirectory + '\Microsoft.WindowsDesktop.App.deps.json') and
           FileExists(CandidateDirectory + '\PresentationCore.dll') and
           FileExists(CandidateDirectory + '\PresentationFramework.dll') and
           FileExists(CandidateDirectory + '\WindowsBase.dll') then
        begin
          Log('.NET prerequisite check: found Microsoft.WindowsDesktop.App ' + CandidateVersion + ' x64.');
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;

  Log('.NET prerequisite check: Microsoft.WindowsDesktop.App 9.x x64 is missing or incomplete.');
end;

function DownloadDotNet9DesktopRuntime: String;
var
  PrimaryError: String;
begin
  Result := '';
  try
    Log('Downloading .NET 9 Desktop Runtime x64 from the primary source.');
    DownloadTemporaryFile(
      '{#RuntimeUrl}',
      '{#RuntimeFileName}',
      '{#RuntimeSha256}',
      nil);
    Exit;
  except
    PrimaryError := GetExceptionMessage;
    Log('Primary .NET runtime download failed: ' + PrimaryError);
  end;

  DeleteFile(ExpandConstant('{tmp}\{#RuntimeFileName}'));
  try
    Log('Downloading .NET 9 Desktop Runtime x64 from the fallback source.');
    DownloadTemporaryFile(
      '{#RuntimeFallbackUrl}',
      '{#RuntimeFileName}',
      '{#RuntimeSha256}',
      nil);
  except
    Result := '无法下载 .NET 9 Desktop Runtime。主下载源：' + PrimaryError +
      '；备用下载源：' + GetExceptionMessage;
  end;
end;

function CloseRunningImeTool: Boolean;
var
  UpdateProcessId: Integer;
  TaskkillArguments: String;
  ResultCode: Integer;
begin
  { The update installer is a child of ImeTool. Never use taskkill /T here because that
    terminates the installer itself together with the parent process tree. }
  UpdateProcessId := StrToIntDef(ExpandConstant('{param:UPDATEPID|0}'), 0);
  if UpdateProcessId > 0 then
    TaskkillArguments := '/F /PID ' + IntToStr(UpdateProcessId)
  else
    { Manual installs do not provide UPDATEPID. A portable copy can run outside the install
      directory, so Restart Manager cannot discover it by target-file usage. }
    TaskkillArguments := '/F /IM ImeTool.exe';

  Result := Exec(
    ExpandConstant('{sys}\taskkill.exe'),
    TaskkillArguments,
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

  Log('Microsoft.WindowsDesktop.App 9.x x64 is required and will be installed.');
  Result := DownloadDotNet9DesktopRuntime;
  if Result <> '' then
    Exit;

  RuntimePath := ExpandConstant('{tmp}\{#RuntimeFileName}');
  Log('Starting .NET 9 Desktop Runtime x64 installer.');
  if not ShellExec('', RuntimePath, '/install /quiet /norestart', '', SW_SHOW,
    ewWaitUntilTerminated, ResultCode) then
  begin
    Result := '无法启动 .NET 9 Desktop Runtime 安装程序。';
    Exit;
  end;

  if (ResultCode = 1641) or (ResultCode = 3010) then
  begin
    RuntimeRestartRequired := True;
    NeedsRestart := True
  end
  else if ResultCode <> 0 then
  begin
    Result := Format('.NET 9 Desktop Runtime 安装失败，错误代码：%d。', [ResultCode]);
    Exit;
  end;

  if not IsDotNet9DesktopRuntimeInstalled then
    Result := '.NET 9 Desktop Runtime 安装完成，但完整性检查未通过。请重启系统后重试安装。'
  else
    Log('.NET 9 Desktop Runtime x64 installation and verification completed.');
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
