; Script Inno Setup para o GetIt
; Configurado para instalacao inicial e atualizacoes futuras automaticas (in-place upgrade)

#define MyAppName "GetIt"
#define MyAppVersion "0.0.7"
#define MyAppPublisher "Lucas Jordao"
#define MyAppURL "https://github.com/lucasjordaoreal/getit"
#define MyAppExeName "GetIt.exe"
#define MyAppId "{{4A2D2397-A619-4F54-9452-9E1A334336B1}}"

[Setup]
; AppId fixo: essencial para que o instalador reconheça versões anteriores e atualize na mesma pasta!
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Instalação por padrão no diretório de programas (Program Files se admin, ou pasta de programas do usuário)
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes

; Configuração de saída do executável de instalação
OutputDir=..\bin\Installer
OutputBaseFilename=GetIt-Setup-v{#MyAppVersion}
SetupIconFile=..\GetIt.App\icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; ==============================================================================
; REGRAS INTELIGENTES DE ATUALIZAÇÃO (UPGRADE IN-PLACE)
; ==============================================================================
; Se o GetIt estiver aberto durante o update, fecha automaticamente para não travar DLLs
CloseApplications=yes
CloseApplicationsFilter=GetIt.exe
RestartApplications=no

; Lembra as opções e a pasta da versão instalada anteriormente
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes

; Permite instalar para o usuário atual sem exigir obrigatoriamente senha de Admin
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Registro no Adicionar/Remover Programas do Windows
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Copia tudo da pasta de publicação (incluindo GetIt.exe, Libs/, Engine/, etc.)
Source: "..\GetIt.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
