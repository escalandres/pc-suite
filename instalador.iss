; =====================================================================
; instalador.iss — instalador Setup.exe de PCSuite (Inno Setup 6.5+)
;
; Requisitos previos:
;   1. Inno Setup 6.5 o superior instalado (usa ISPP, incluido).
;   2. .\empaquetar.bat ejecutado: este script empaqueta lo que hay en
;      dist\PCSuite-win-x64 (app autocontenida + helper + frontend/dist).
;
; Compilar (genera dist\instalador\PCSuite-Setup-<version>-win64.exe):
;   - Doble clic en este archivo (Compil32) → "Compile".
;   - O desde terminal:
;       "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" instalador.iss
;
; Nota: Inno Setup genera instaladores .exe. Los .msi requieren WiX,
; otra herramienta distinta.
;
; Icono propio: opcional. Añade un .ico al repo y descomenta SetupIconFile.
; =====================================================================

#define AppName "PCSuite"
#define AppExeName "PCSuite.exe"
#define AppPublisher "AES Studio"
#define AppUrl "https://aes-studio.dev"

; ---- Versión: del recurso de versión de PCSuite.exe, que a su vez sale
; de <Version> en Directory.Build.props (misma fuente que empaquetar.bat).
; GetVersionNumbersString devuelve 4 partes ("1.0.0.0"): se quita la última.
; Si falla, ejecuta antes .\empaquetar.bat (el exe debe existir en dist\).
#define FullVersion GetVersionNumbersString(SourcePath + "dist\PCSuite-win-x64\" + AppExeName)
#define AppVersion Copy(FullVersion, 1, RPos(".", FullVersion) - 1)

[Setup]
; Id estable: no cambiar entre versiones para que actualizar no deje
; duplicados en "Aplicaciones" ni pierda la desinstalación previa.
AppId={{8F4A2C1D-6B3E-4E7F-9A5B-1C2D3E4F5A6B}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

; Instalación para todos (Program Files) o solo el usuario (LocalAppData):
; el asistente lo pregunta (PrivilegesRequiredOverridesAllowed).
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; Solo Windows x64 (la app se publica win-x64 autocontenida).
; Requiere Inno Setup 6.5+: el valor moderno es "x64compatible".
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

; Apariencia del asistente y compresión (Carpeta portable ~126 MB -> exe).
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

OutputDir=dist\instalador
OutputBaseFilename={#AppName}-Setup-{#AppVersion}-win64

; Icono del instalador: el mismo logo de la app.
SetupIconFile=src\PCSuite.Desktop\PCSuite.ico

[Languages]
; El primero es el idioma por defecto del asistente.
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; \
  GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Todo el paquete de empaquetar.bat. Se excluyen:
;   - *.pdb: simbolos, sobran en la maquina destino.
;   - PCSuite.exe.WebView2: perfil del navegador WebView (cookies, caches)
;     que aparece si alguien ejecuto la app portable desde dist\ antes
;     de compilar el instalador. Nunca debe distribuirse.
Source: "dist\PCSuite-win-x64\*"; DestDir: "{app}"; \
  Excludes: "*.pdb,PCSuite.exe.WebView2"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; \
  Tasks: desktopicon

[Run]
; Checkbox "Ejecutar PCSuite" al terminar (omitido en instalación silenciosa).
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
  Flags: nowait postinstall skipifsilent
