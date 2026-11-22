; Inno Setup installer for CsvProjectXml.Maui
; Place this file in the CsvProjectXml.Maui project directory,
; beside CsvProjectXml.Maui.csproj.

#define MyAppName "CsvProjectXml"
#define MyAppVersion "1.0.0"
#define MyAppExeName "CsvProjectXml.Maui.exe"
#define PublishDir "bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

[Setup]
; Keep this AppId unchanged for future updates to this application.
AppId={{D541B1F2-5A3A-4CB9-BFE0-7C9F35E4A89C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=installer-output
OutputBaseFilename=CsvProjectXml-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Include the complete self-contained MAUI publish output, including wwwroot.
; Exclude PDB debug symbols and the WebView2 user-data/cache directory.
; The WebView2 directory is created beside the EXE at runtime and can contain
; lock files, browser cache, and machine/user-specific state. It must not ship.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,CsvProjectXml.Maui.exe.WebView2\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: postinstall nowait skipifsilent
