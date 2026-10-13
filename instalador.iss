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
;
; FLUJO DE ACTUALIZACIÓN (versión anterior detectada):
;   1. InitializeSetup: si PCSuite se está ejecutando, se cierra
;      (el usuario lo confirma en modo interactivo; en instalación
;      silenciosa se cierra directamente). Sin esto, los archivos
;      bloqueados romperían la desinstalación previa.
;   2. PrepareToInstall: busca la clave de desinstalación del AppId en
;      HKLM y HKCU ("todos los usuarios" o "solo yo": el asistente
;      permite ambas, y la elección puede cambiar entre versiones).
;      Si existe, ejecuta el desinstalador anterior en modo silencioso
;      con el parámetro _?= — imprescindible: sin él, unins000.exe se
;      copia a %TEMP% y se relanza, y Exec solo esperaría a la copia,
;      no a la desinstalación real (carrera con la instalación nueva).
;      Con _?= el desinstalador NO se borra solo ni elimina su clave:
;      este script limpia ambos tras un resultado correcto.
;   3. La instalación nueva continúa como si fuese la primera (directorio
;      previo reutilizado: se resuelve antes de PrepareToInstall).
;      Los datos de usuario viven en %LOCALAPPDATA%\PCSuite, fuera de
;      {app}, así que la desinstalación no los toca.
; =====================================================================

#define AppName "PCSuite"
#define AppExeName "PCSuite.exe"
#define AppPublisher "AES Studio"
#define AppUrl "https://aes-studio.dev"

; AppId estable (no cambiar entre versiones). Se define el GUID sin llaves
; para reutilizarlo en [Code] como clave ARP (que las lleva):
;   Clave ARP: Software\Microsoft\Windows\CurrentVersion\Uninstall\{GUID}_is1
; En [Setup], "{{...}}" es la secuencia de escape de Inno para una llave
; literal; en [Code], un string Pascal admite las llaves directamente.
#define AppIdGuid "8F4A2C1D-6B3E-4E7F-9A5B-1C2D3E4F5A6B"

; ---- Versión: del recurso de versión de PCSuite.exe, que a su vez sale
; de <Version> en Directory.Build.props (misma fuente que empaquetar.bat).
; GetVersionNumbersString devuelve 4 partes ("1.0.0.0"): se quita la última.
; Si falla, ejecuta antes .\empaquetar.bat (el exe debe existir en dist\).
#define FullVersion GetVersionNumbersString(SourcePath + "dist\PCSuite-win-x64\" + AppExeName)
#define AppVersion Copy(FullVersion, 1, RPos(".", FullVersion) - 1)

[Setup]
; Id estable: no cambiar entre versiones para que actualizar no deje
; duplicados en "Aplicaciones" ni pierda la desinstalación previa.
; "{{..." es la llave literal de escape; el "}" final reconstruye
; AppId = "{GUID}" tal como lo registró la primera instalación.
AppId={{{#AppIdGuid}}
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

[Code]
const
  // Clave ARP que genera el propio Inno para este AppId (raíz aparte:
  // HKLM o HKCU según el modo de instalación elegido).
  // "{{#AppIdGuid}}" → tras ISPP: "{GUID}".
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppIdGuid}}_is1';

// ---------------------------------------------------------------------
// Detección de la app en ejecución
// ---------------------------------------------------------------------

// ¿Hay un proceso PCSuite.exe vivo? tasklist devuelve 0 incluso sin
// coincidencias ("INFO: No tasks..."), así que el filtro real es find:
// el código de salida del pipeline es el de find (0 = encontrado).
function IsAppRunning(): Boolean;
var
  ResultCode: Integer;
begin
  // Preinicializado a 1 ("no corriendo"): si Exec fallase, no dar un
  // falso positivo que bloquearía la instalación.
  ResultCode := 1;
  Exec(
    ExpandConstant('{cmd}'),
    '/C tasklist /FI "IMAGENAME eq {#AppExeName}" 2>NUL | find /I "{#AppExeName}" >NUL',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

// Cierre en dos fases: taskkill sin /F envía WM_CLOSE (la app puede
// guardar estado); si tras ~10 s sigue viva, cierre forzado.
procedure CloseApp();
var
  ResultCode: Integer;
  Attempts: Integer;
begin
  Exec(
    ExpandConstant('{cmd}'),
    '/C taskkill /IM {#AppExeName} >NUL 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  for Attempts := 1 to 20 do
  begin
    if not IsAppRunning() then Exit;
    Sleep(500);
  end;

  // Sigue viva (ventana oculta, bloqueada, depurador): forzar.
  Exec(
    ExpandConstant('{cmd}'),
    '/C taskkill /F /IM {#AppExeName} >NUL 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
end;

// Antes del asistente: la app no puede seguir corriendo durante la
// desinstalación de la versión anterior ni durante la copia de archivos.
function InitializeSetup(): Boolean;
begin
  Result := True;

  if not IsAppRunning() then Exit;

  if WizardSilent() then
  begin
    // Instalación silenciosa (/SILENT o /VERYSILENT): cerrar sin preguntar.
    CloseApp();
  end
  else if MsgBox(
      'PCSuite se está ejecutando y debe cerrarse para actualizar.' + #13#10 +
      'Las transferencias de Local Share en curso se cancelarán.' + #13#10#13#10 +
      '¿Cerrar PCSuite y continuar con la instalación?',
      mbConfirmation, MB_YESNO) = IDYES then
  begin
    CloseApp();
  end
  else
  begin
    MsgBox('Cierra PCSuite y vuelve a ejecutar el instalador.', mbInformation, MB_OK);
    Result := False;
    Exit;
  end;

  // No se pudo cerrar (p. ej. un depurador la retiene): abortar antes
  // de romper la desinstalación previa a mitad.
  if IsAppRunning() then
  begin
    if not WizardSilent() then
      MsgBox(
        'No se pudo cerrar PCSuite.' + #13#10 +
        'Ciérrala manualmente y vuelve a ejecutar el instalador.',
        mbError, MB_OK);
    Result := False;
  end;
end;

// ---------------------------------------------------------------------
// Desinstalación de la versión anterior
// ---------------------------------------------------------------------

// Busca la cadena de desinstalación del AppId en HKLM ("todos los
// usuarios") y luego en HKCU ("solo yo"). Devuelve la raíz usada para
// poder borrar la clave correcta tras desinstalar.
function GetOldUninstallString(
  out UninstallString: String; out RootKey: Integer): Boolean;
begin
  UninstallString := '';

  RootKey := HKLM;
  if RegQueryStringValue(HKLM, UninstallKey, 'UninstallString', UninstallString) then
  begin
    Result := True;
    Exit;
  end;

  RootKey := HKCU;
  if RegQueryStringValue(HKCU, UninstallKey, 'UninstallString', UninstallString) then
  begin
    Result := True;
    Exit;
  end;

  Result := False;
end;

// _?= deja sin borrar el desinstalador y sus datos (unins000.*): limpiarlos.
procedure DeleteUninstallFiles(const OldDir: String);
var
  FindRec: TFindRec;
  DirWithSlash: String;
begin
  DirWithSlash := AddBackslash(OldDir);

  if FindFirst(DirWithSlash + 'unins000.*', FindRec) then
  try
    repeat
      DeleteFile(DirWithSlash + FindRec.Name);
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

// Tras el asistente, justo antes de copiar archivos. Devolver un texto
// no vacío aborta la instalación mostrándolo (la versión anterior queda
// intacta y desinstalable desde "Aplicaciones").
//
// OJO con el orden: el directorio previo ({app}) ya se resolvió (la clave
// aún existía), así que borrarla aquí no afecta a la reutilización.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  UninstallString: String;
  RootKey: Integer;
  OldDir: String;
  ResultCode: Integer;
begin
  Result := '';

  // Primera instalación: no hay nada que desinstalar.
  if not GetOldUninstallString(UninstallString, RootKey) then Exit;

  OldDir := ExtractFilePath(RemoveQuotes(UninstallString));

  // _?= sin contrabarra final: evita que '"' escape la comilla de
  // cierre al tokenizar la línea de comandos.
  if (Length(OldDir) > 3) and (Copy(OldDir, Length(OldDir), 1) = '\') then
    Delete(OldDir, Length(OldDir), 1);

  ResultCode := -1;

  // _?= con el directorio real: el desinstalador corre in situ (sin copia
  // a %TEMP%) y Exec espera a que la desinstalación REAL termine. Debe ser
  // el ÚLTIMO parámetro; entre comillas porque la ruta puede tener espacios.
  // WorkingDir = %TEMP% (no el directorio que se va a borrar: Windows no
  // elimina el directorio que es CWD de un proceso).
  if Exec(
      RemoveQuotes(UninstallString),
      '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES _?="' + OldDir + '"',
      GetEnv('TEMP'), SW_HIDE, ewWaitUntilTerminated, ResultCode)
    and (ResultCode = 0) then
  begin
    // Éxito: limpiar lo que _?= deja (exe, dat, msg y la clave ARP).
    DeleteUninstallFiles(OldDir);
    RegDeleteKeyIncludingSubkeys(RootKey, UninstallKey);
  end
  else
  begin
    // Sin cambios: la versión anterior queda intacta y funcional.
    // Causa típica: instalación previa "todos los usuarios" desinstalada
    // desde un Setup sin elevar (Program Files requiere administrador).
    Result :=
      'No se pudo desinstalar la versión anterior (código ' +
      IntToStr(ResultCode) + ').' + #13#10 +
      'Desinstala PCSuite desde "Aplicaciones y características" y vuelve ' +
      'a ejecutar este instalador.';
  end;
end;
