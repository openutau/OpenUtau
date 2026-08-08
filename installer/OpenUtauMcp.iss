#define AppName "OpenUtau MCP"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppPublisher "OpenUtau MCP Contributors"
#define AppExeName "OpenUtau.exe"

#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif

[Setup]
AppId={{C1A4602B-0F36-4D8A-B7D8-4733DB0B2638}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\OpenUtau MCP
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputBaseFilename=OpenUtau-MCP-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
UninstallDisplayName={#AppName}

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    SaveStringToFile(ExpandConstant('{app}\installed-mcp.txt'), 'OpenUtau MCP installation marker' + #13#10, False);
  end;
end;
