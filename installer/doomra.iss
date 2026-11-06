#define AppVersion "0.50"
#ifndef Stage
  #error Pass /DStage pointing to the verified build directory
#endif
#ifndef Output
  #error Pass /DOutput pointing to the release directory
#endif

[Setup]
AppId={{46C9F6E4-94B0-42BB-A5AB-8205BC03ED4E}
AppName=VPN
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}.0.0
DefaultDirName={autopf}\doomra VPN
DisableDirPage=yes
DefaultGroupName=VPN
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\VPN.ico
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
OutputDir={#Output}
OutputBaseFilename=doomraVPN-{#AppVersion}-setup-windows-x64
SetupIconFile=..\assets\doomra.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
#ifdef SignRelease
SignTool=doomra
SignedUninstaller=yes
#endif

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "{#Stage}\doomraVPN.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\doomraVPN.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\doomra.ico"; DestDir: "{app}"; DestName: "VPN.ico"; Flags: ignoreversion
Source: "{#Stage}\sing-box.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\xray.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\sing-box-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\xray-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\VPN"; Filename: "{app}\doomraVPN.exe"; WorkingDir: "{app}"; IconFilename: "{app}\VPN.ico"

[InstallDelete]
Type: files; Name: "{autoprograms}\doomra VPN.lnk"

[Registry]
Root: HKLM64; Subkey: "SOFTWARE\doomra VPN"; Flags: uninsdeletekey

[Run]
Filename: "{app}\doomraVPN.exe"; Description: "Открыть VPN"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
const
  ServiceKey = 'SYSTEM\CurrentControlSet\Services\DoomraVpn';
var
  StoppedForUpgrade: Boolean;
  SetupCompleted: Boolean;

function RunHidden(const FileName, Arguments: String): Boolean;
var
  ExitCode: Integer;
begin
  Result := Exec(FileName, Arguments, ExpandConstant('{sys}'), SW_HIDE,
    ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

function AppCommand(const Arguments: String): Boolean;
begin
  Result := RunHidden(ExpandConstant('{app}\doomraVPN.exe'), Arguments);
end;

function ServiceCommand(const Arguments: String): Boolean;
begin
  Result := RunHidden(ExpandConstant('{sys}\sc.exe'), Arguments);
end;

function ServiceExists: Boolean;
begin
  Result := RegKeyExists(HKLM64, ServiceKey);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstalledPath, ExpectedPath: String;
begin
  Result := '';
  if CompareText(ExpandConstant('{app}'), ExpandConstant('{autopf}\doomra VPN')) <> 0 then begin
    Result := 'Для службы требуется стандартная папка приложения в Program Files.';
    Exit;
  end;
  if ServiceExists then begin
    ExpectedPath := '"' + ExpandConstant('{app}\doomraVPN.exe') + '" --service';
    if not RegQueryStringValue(HKLM64, ServiceKey, 'ImagePath', InstalledPath) or
       (CompareText(InstalledPath, ExpectedPath) <> 0) then begin
      Result := 'Обнаружена одноимённая служба с другим путём.';
      Exit;
    end;
    StoppedForUpgrade := AppCommand('--stop-service');
    if not StoppedForUpgrade then
      Result := 'Не удалось остановить службу VPN. Закройте клиент и повторите установку.';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Created: Boolean;
begin
  if CurStep <> ssPostInstall then Exit;
  if not AppCommand('--register-service-owner') then
    RaiseException('Не удалось зарегистрировать пользователя службы VPN.');
  Created := not ServiceExists;
  if Created and not ServiceCommand('create DoomraVpn binPath= "\"' +
    ExpandConstant('{app}\doomraVPN.exe') + '\" --service" start= auto DisplayName= "VPN"') then
    RaiseException('Не удалось зарегистрировать службу VPN.');
  if not Created and not ServiceCommand('config DoomraVpn DisplayName= "VPN"') then
    RaiseException('Не удалось обновить отображаемое имя службы VPN.');
  if not AppCommand('--start-service') then begin
    if Created then ServiceCommand('delete DoomraVpn');
    RaiseException('Не удалось запустить службу VPN.');
  end;
  SetupCompleted := True;
end;

procedure DeinitializeSetup;
begin
  if StoppedForUpgrade and not SetupCompleted then AppCommand('--start-service');
end;

function InitializeUninstall: Boolean;
begin
  Result := (not ServiceExists) or AppCommand('--stop-service');
  if Result then Result := AppCommand('--remove-service-settings');
  if Result and ServiceExists then Result := ServiceCommand('delete DoomraVpn');
  if not Result then
    MsgBox('Не удалось остановить службу или снять правила блокировки. Удаление прервано.', mbError, MB_OK);
end;
