; Desktop Drawer installer (per-user, no administrator required).
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef ProductVersion
  #define ProductVersion "1.0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#ifndef OutputDir
  #define OutputDir "Output"
#endif

[Setup]
AppId={{93520BB2-541E-4923-B57C-7B7F3106DF3A}
AppName=Desktop Drawer
AppVerName=Desktop Drawer {#AppVersion}
AppVersion={#AppVersion}
AppPublisher=BEGINWITHF
AppPublisherURL=https://github.com/BEGINWITHF/Desktop-Drawer
AppSupportURL=https://github.com/BEGINWITHF/Desktop-Drawer/issues
AppUpdatesURL=https://github.com/BEGINWITHF/Desktop-Drawer/releases
AppCopyright=Copyright (C) 2026 BEGINWITHF
DefaultDirName={localappdata}\DesktopDrawer
DefaultGroupName=Desktop Drawer
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=DesktopDrawer-Setup-{#AppVersion}
SetupIconFile=..\src\DesktopDrawer\Assets\app.ico
UninstallDisplayIcon={app}\DesktopDrawer.exe
UninstallDisplayName=Desktop Drawer
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
AppMutex=Local\DesktopDrawer.SingleInstance
UsePreviousAppDir=yes
ShowLanguageDialog=auto
VersionInfoVersion={#ProductVersion}
VersionInfoProductName=Desktop Drawer
VersionInfoProductVersion={#ProductVersion}
VersionInfoCompany=BEGINWITHF
VersionInfoDescription=Desktop Drawer Setup
VersionInfoCopyright=Copyright (C) 2026 BEGINWITHF

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "chinesetraditional"; MessagesFile: "Languages\ChineseTraditional.isl"
Name: "japanese"; MessagesFile: "Languages\Japanese.isl"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "DesktopDrawer"; Flags: uninsdeletevalue

[CustomMessages]
english.MissingDotNet=Desktop Drawer requires the .NET 9 Desktop Runtime.%n%nOpen the download page now?
chinesesimplified.MissingDotNet=Desktop Drawer 需要 .NET 9 桌面运行时。%n%n现在打开下载页面吗？
chinesetraditional.MissingDotNet=Desktop Drawer 需要 .NET 9 桌面執行階段。%n%n現在開啟下載頁面嗎？
japanese.MissingDotNet=Desktop Drawer には .NET 9 デスクトップ ランタイムが必要です。%n%nダウンロード ページを開きますか？

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Desktop Drawer"; Filename: "{app}\DesktopDrawer.exe"; WorkingDir: "{app}"; Comment: "A paged desktop for Windows"

[Run]
Filename: "{app}\DesktopDrawer.exe"; Description: "{cm:LaunchProgram,Desktop Drawer}"; Flags: nowait postinstall skipifsilent

[Code]
function HasDotNet9Desktop: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  Keys: array[0..1] of String;
  K: Integer;
begin
  Result := False;
  Keys[0] := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  Keys[1] := 'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

  for K := 0 to 1 do
  begin
    if RegGetValueNames(HKLM, Keys[K], Names) then
    begin
      for I := 0 to GetArrayLength(Names) - 1 do
      begin
        if Pos('9.', Names[I]) = 1 then
        begin
          Result := True;
          Exit;
        end;
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := HasDotNet9Desktop;
  if not Result then
  begin
    if MsgBox(CustomMessage('MissingDotNet'), mbError, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/9.0', '', '',
        SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;
