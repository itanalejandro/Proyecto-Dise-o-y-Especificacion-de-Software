; Based on the team's v3 package, with per-user installation and no security-policy changes.
#ifndef SourceDir
  #error SourceDir must be provided by tools/Ftracker.Packaging
#endif
#ifndef AppVersion
  #define AppVersion "3.0.0-beta.1"
#endif
#ifndef NumericVersion
  #define NumericVersion "3.0.0.0"
#endif

[Setup]
AppId={{D9A24C6F-91F2-4F92-A723-53531F32A89F}
AppName=Ftracker
AppVersion={#AppVersion}
VersionInfoVersion={#NumericVersion}
AppPublisher=Equipo Ftracker
DefaultDirName={localappdata}\Programs\Ftracker
DefaultGroupName=Ftracker
PrivilegesRequired=lowest
MinVersion=10.0
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=Ftracker-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\Ftracker.Desktop\Assets\ftracker.ico
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
DisableProgramGroupPage=yes
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\Ftracker.exe
; No [UninstallDelete]: the database and evidence under LocalAppData\Ftracker are retained.

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Ftracker\Ftracker"; Filename: "{app}\Ftracker.exe"; WorkingDir: "{app}"
Name: "{userprograms}\Ftracker\Desinstalar Ftracker"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Ftracker"; Filename: "{app}\Ftracker.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Ftracker.exe"; Description: "Abrir Ftracker"; Flags: nowait postinstall skipifsilent
