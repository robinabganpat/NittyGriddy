; Inno Setup script for NittyGriddy.
;
; Built by tools\build-release.ps1, which passes the version, the folder holding the published
; application and the output folder:
;   ISCC /DAppVersion=2.0.0 /DSourceDir=<publish folder> /DOutputDir=<Releases\2.0.0> installer\NittyGriddy.iss
;
; The installer is per-user: it needs no administrator rights and installs to
; %LOCALAPPDATA%\Programs\NittyGriddy, a fixed folder, so "Start with Windows" keeps pointing at the
; right program after an update.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\release\NittyGriddy"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

#define AppName "NittyGriddy"
#define AppExe "NittyGriddy.exe"
#define AppUrl "https://github.com/robinabganpat/NittyGriddy"
#define AppAuthor "Robin Ganpat"
#define RuntimePage "https://dotnet.microsoft.com/download/dotnet/9.0"

[Setup]
; Identifies the application to Windows; never change it, or updates install side by side
AppId={{9F6959DA-13BE-4C47-BE43-EE3B4BE233A2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppAuthor}
AppCopyright=Copyright (C) 2025-2026 {#AppAuthor}. Licensed under the GNU GPL v3.
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppAuthor}
VersionInfoDescription={#AppName} {#AppVersion} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
SetupIconFile=..\App\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; The name of the application's single-instance mutex: Setup asks to close a running NittyGriddy first
AppMutex=NittyGriddy.SingleInstance
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; "Start with Windows" is a setting inside NittyGriddy; remove what it wrote when uninstalling
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#AppName}"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// NittyGriddy runs on Microsoft's .NET 9 Desktop Runtime, which is installed machine-wide.
//
// Setup deliberately does not download or run the runtime installer itself. An installer that fetches and
// starts another executable is what antivirus machine-learning models are trained to flag as a "dropper",
// and an earlier build that did so was flagged by several of them. Setup only points to Microsoft's page.
function HasDesktopRuntime: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  FindRec: TFindRec;
begin
  Result := False;

  if RegGetValueNames(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Pos('9.', Names[I]) = 1 then
        Result := True;

  // The registry entry can be missing for a runtime that came with an SDK; look for the files too
  if not Result then
    if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\9.*'), FindRec) then
    begin
      Result := True;
      FindClose(FindRec);
    end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if (CurStep <> ssPostInstall) or WizardSilent or HasDesktopRuntime then
    Exit;

  if MsgBox(
       'NittyGriddy needs Microsoft''s .NET 9 Desktop Runtime, which is not installed on this computer.' + #13#10#13#10 +
       'Open Microsoft''s download page now? Choose ".NET Desktop Runtime 9", "Windows x64", then install it.',
       mbConfirmation, MB_YESNO) = IDYES then
    ShellExecAsOriginalUser('open', '{#RuntimePage}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;
