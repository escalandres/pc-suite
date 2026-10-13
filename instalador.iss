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
;   2. PrepareToInstall: busca la clave ARP del AppId en HKLM y HKCU
;      ("todos los usuarios" o "solo yo": el asistente permite ambas).
;      - BLINDAJE DE ELEVACIÓN (InitializeSetup): si la instalación previa
;        es "todos los usuarios" (HKLM) y este Setup NO va elevado, se
;        aborta YA con instrucciones — elegir "solo para mí" contra una
;        instalación en Program Files acaba en MoveFile code 5.
;      - Si hay desinstalador en el directorio (escaneo unins000-unins010,
;        que el registro puede estar desactualizado), se ejecuta en modo
;        silencioso SIN _?=: así se auto-limpia (exe+dat+msg+clave) vía su
;        copia en %TEMP%. El Exec espera el trabajo principal y un sondeo
;        determinista espera a que la auto-limpieza termine (el exe
;        desaparece del directorio) — nada de Sleep a ciegas.
;      - VERIFICACIÓN REAL: el exit 0 del desinstalador miente en silencio
;        cuando no puede borrar archivos (sin permisos); si el exe de la
;        app sigue en el directorio, se aborta con la pista de elevación.
;      - Estado huérfano (clave existe, desinstalador no: actualización
;        rota previa): limpiar la clave fantasma y los restos, y seguir
;        instalando. Este paso EXISTE para reparar ese estado.
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

; Log de instalación en %TEMP% (Setup Log *.zip): imprescindible para
; diagnosticar actualizaciones (p. ej. por qué falló la desinstalación previa).
SetupLogging=yes

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

[UninstallRun]
; Higiene de firewall: PCSuite crea en ejecución (con UAC puntual) dos reglas
; entrantes program-scoped — TCP <puerto del servicio> y UDP 5354 (discovery),
; ambas con el nombre fijo de abajo. Sin la app son inertes, pero un equipo SIN
; PCSuite no debe dejar restos en el firewall: eliminarlas al desinstalar.
; Best-effort (sin fallo si no existen, sin elevar si el modo no lo permite).
Filename: "{cmd}"; Parameters: "/C netsh advfirewall firewall delete rule name=""PCSuite Local Share"" >NUL 2>&1"; \
  Flags: runhidden; RunOnceId: "DelFirewallRules"

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

  // Blindaje de elevación: actualizar una instalación "todos los usuarios"
  // (Program Files + HKLM) exige que ESTE Setup vaya elevado. Con
  // PrivilegesRequiredOverridesAllowed=dialog es fácil elegir "solo para
  // mí" y acabar en un estado a mitad: el desinstalador previo salta sus
  // fallos en silencio (exit 0 sin haber borrado nada) y la copia muere
  // con "MoveFile failed, code 5" en PCSuite.exe. Mejor abortar YA con
  // instrucciones claras que romper la instalación a mitad.
  if RegKeyExists(HKLM, UninstallKey) and not IsAdminInstallMode() then
  begin
    MsgBox(
      'La versión instalada de PCSuite es "para todos los usuarios"' + #13#10 +
      '(carpeta Program Files) y solo puede actualizarse con permisos' + #13#10 +
      'de administrador.' + #13#10#13#10 +
      'Vuelve a ejecutar este instalador y elige "Instalar para todos ' +
      'los usuarios" (o ejecútalo como administrador).',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

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

// ---------------------------------------------------------------------
// Desinstalación de la versión anterior
// ---------------------------------------------------------------------

// Inno numera los desinstaladores unins000, unins001, ... cuando quedan
// restos de intentos previos. Escanear 0..10 en el DIRECTORIO (no el
// registro: la clave ARP puede apuntar a un exe que ya no existe).
function FindUninstaller(const InstallPath: String): String;
var
  I: Integer;
  Candidate: String;
begin
  Result := '';

  for I := 0 to 10 do
  begin
    Candidate := AddBackslash(InstallPath) + 'unins' + Format('%.3d', [I]) + '.exe';
    if FileExists(Candidate) then
    begin
      Result := Candidate;
      Exit;
    end;
  end;
end;

// Busca la clave ARP del AppId en HKLM ("todos los usuarios") y luego en
// HKCU ("solo yo"). Devuelve la raíz para poder borrar la clave correcta.
function GetOldUninstallEntry(out RootKey: Integer): Boolean;
begin
  RootKey := HKLM;
  if RegKeyExists(HKLM, UninstallKey) then
  begin
    Result := True;
    Exit;
  end;

  RootKey := HKCU;
  if RegKeyExists(HKCU, UninstallKey) then
  begin
    Result := True;
    Exit;
  end;

  Result := False;
end;

// Directorio de la instalación previa: de la clave ARP (InstallLocation,
// siempre lo escribe Inno) y si falta, el valor por defecto.
function GetOldInstallPath(): String;
var
  Location: String;
begin
  if RegQueryStringValue(HKLM, UninstallKey, 'InstallLocation', Location) or
     RegQueryStringValue(HKCU, UninstallKey, 'InstallLocation', Location) then
  begin
    Result := Location;
  end
  else
  begin
    Result := ExpandConstant('{autopf}\{#AppName}');
  end;

  // Sin contrabarra final.
  if (Length(Result) > 3) and (Copy(Result, Length(Result), 1) = '\') then
    Delete(Result, Length(Result), 1);
end;

// Borra restos del desinstalador (exe/dat/msg) con CUALQUIER numeración
// unins000-unins010. Sin _?= el desinstalador ya se limpia solo: esto es
// cinturón y tirantes para el caso de timeout del sondeo.
procedure DeleteUninstallFiles(const OldDir: String);
var
  I: Integer;
  FindRec: TFindRec;
  DirWithSlash: String;
begin
  DirWithSlash := AddBackslash(OldDir);

  for I := 0 to 10 do
  begin
    if FindFirst(DirWithSlash + 'unins' + Format('%.3d', [I]) + '.*', FindRec) then
    try
      repeat
        DeleteFile(DirWithSlash + FindRec.Name);
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// Sin _?=, el desinstalador copia SU FINAL a %TEMP% para auto-borrarse y
// el proceso original termina ANTES. La señal de finalización TOTAL es que
// el exe desaparece del directorio (la auto-limpieza es su último acto).
// Sondeo determinista — nada de Sleep a ciegas.
function WaitForUninstallCompletion(const OldDir: String): Boolean;
var
  Attempts: Integer;
begin
  Result := True;

  for Attempts := 1 to 30 do
  begin
    if FindUninstaller(OldDir) = '' then Exit;
    Sleep(500);
  end;

  Result := False;
end;

// Tras el asistente, justo antes de copiar archivos. Devolver un texto
// no vacío aborta la instalación mostrándolo (la versión anterior queda
// intacta y desinstalable desde "Aplicaciones").
//
// OJO con el orden: el directorio previo ({app}) ya se resolvió (la clave
// aún existía), así que borrarla aquí no afecta a la reutilización.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RootKey: Integer;
  OldDir: String;
  Uninstaller: String;
  ResultCode: Integer;
begin
  Result := '';

  // Primera instalación: no hay clave ARP, no hay nada que hacer.
  if not GetOldUninstallEntry(RootKey) then Exit;

  OldDir := GetOldInstallPath();
  Uninstaller := FindUninstaller(OldDir);

  if Uninstaller = '' then
  begin
    // Estado huérfano: la clave ARP existe pero su desinstalador NO
    // (desinstalación rota de una actualización anterior). Sin exe no hay
    // nada que ejecutar: limpiar la clave fantasma y los restos dat/msg, y
    // dejar que la instalación nueva sobreescriba los archivos. NO abortar
    // — este estado es justamente el que este paso existe para reparar.
    DeleteUninstallFiles(OldDir);
    RegDeleteKeyIncludingSubkeys(RootKey, UninstallKey);
    Exit;
  end;

  // Sin _?=: el desinstalador se auto-limpia (exe+dat+msg+clave ARP) vía su
  // copia en %TEMP%. Esperamos el proceso principal y sondeamos hasta que
  // la auto-limpieza termina (exe desaparece). WorkingDir = %TEMP% (no el
  // directorio que se va a borrar: Windows no elimina el CWD de un proceso).
  ResultCode := -1;
  if Exec(
      Uninstaller,
      '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES',
      GetEnv('TEMP'), SW_HIDE, ewWaitUntilTerminated, ResultCode)
    and (ResultCode = 0) then
  begin
    // El exit 0 del desinstalador NO es garantía: en modo silencioso
    // salta los archivos que no puede borrar (p. ej. sin permisos en
    // Program Files) y aún así devuelve 0. Verificar el resultado REAL:
    // si el exe de la app sigue en el directorio, la desinstalación
    // no se completó y la copia fallaría igual ("MoveFile, code 5").
    if FileExists(AddBackslash(OldDir) + '{#AppExeName}') then
    begin
      Result :=
        'La versión anterior no se llegó a desinstalar por completo' + #13#10 +
        '(su ejecutable sigue en el disco: archivos bloqueados o sin ' +
        'permisos suficientes).' + #13#10#13#10 +
        'Ejecuta este instalador con permisos de administrador' + #13#10 +
        'y elige "Instalar para todos los usuarios".';
      Exit;
    end;

    if not WaitForUninstallCompletion(OldDir) then
    begin
      // La desinstalación terminó bien pero la auto-limpieza no llegó a
      // completar. Borrar restos y la clave manualmente.
      DeleteUninstallFiles(OldDir);
    end;

    // La copia en %TEMP% normalmente borra la clave; idempotente por si acaso.
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
