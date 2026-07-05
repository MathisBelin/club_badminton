; Script Inno Setup — installeur « Club de Badminton »
; Compilation : ISCC.exe installer\ClubBadminton.iss   (Inno Setup 6)
; Prérequis : avoir publié l'exe autonome au préalable :
;   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

#define MyAppName "Club de Badminton"
#define MyAppVersion "1.1.1"
#define MyAppPublisher "Club de Badminton"
#define MyAppExeName "BadmintonClub.exe"
#define PublishDir "..\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{B7A3F2E1-2C4D-4A6B-9E1F-CB0AD5E7F001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\ClubBadminton
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=ClubBadminton-Setup-{#MyAppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; client_secret.json inclus uniquement s'il est présent (NE PAS partager l'installeur s'il l'inclut)
Source: "{#PublishDir}\client_secret.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent


