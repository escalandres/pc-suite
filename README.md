# PCSuite

**Aplicación de escritorio para Windows — .NET 10 + WebView2.**

PCSuite es un ejecutable WinForms nativo que aloja un WebView2; dentro del
WebView2 corre la UI (React/TypeScript) embebida en la app. No es una web app:
el frontend no se sirve a la red ni se despliega por separado — es la capa de
presentación interna del programa de escritorio.

## Requisitos

- Windows 10/11
- .NET SDK 10.0.401 o superior (solo para compilar)
- WebView2 Runtime (preinstalado en Windows 11)
- Node.js 24+ y npm (solo para compilar el frontend)

## Compilar y ejecutar

**Opción rápida** — doble clic en `iniciar.bat` (compila frontend + .NET y
lanza la ventana):

```bat
iniciar.bat
```

**Manualmente:**

```powershell
# 1. Frontend (produce frontend/dist, la UI embebida)
npm --prefix frontend install
npm --prefix frontend run build

# 2. App de escritorio
dotnet build PCSuite.sln
dotnet run --project src/PCSuite.Desktop
```

La app abre una ventana de escritorio con la pantalla Inicio de PCSuite.

**Nunca ejecutar la app como Administrador.** La elevación es puntual, por
operación y con UAC (ver «Elevación por operación» más abajo).

### Desarrollo (hot reload de la UI)

```powershell
# Terminal 1: dev server de Vite (solo para desarrollar la UI)
npm --prefix frontend run dev

# Terminal 2: la app apunta al dev server
$env:PCSUITE_DEV_URL = "http://localhost:5173"
dotnet run --project src/PCSuite.Desktop
```

Sin `PCSUITE_DEV_URL`, la ventana carga `frontend/dist` desde el host virtual
`https://app.pcsuite.local` (un host interno del WebView2, sin red real).

## Arquitectura

El proceso de escritorio es el centro; todo lo demás son sus capas:

| Proyecto (ensamblado) | Rol dentro de la app de escritorio |
|---|---|
| `PCSuite.Desktop` | **El ejecutable**: ventana WinForms, WebView2, puente de mensajes, política de navegación |
| `PCSuite.Contracts` | Tipos del protocolo puente (request/response/event) compartidos por ambas capas |
| `PCSuite.Core` | Abstracciones: `IOperation`, `OperationContext`, `OperationResult` |
| `PCSuite.Application` | `CommandRouter`: enrutado de operaciones con allow-list, nunca lanza excepciones |
| `PCSuite.Infrastructure` | Logging a archivo y operaciones concretas (`system.info`) |
| `PCSuite.Elevated` | Proceso auxiliar elevado (UAC) para operaciones puntuales |
| `tests/PCSuite.Application.Tests` | Tests xUnit |
| `frontend/` | UI embebida (React + TS + Tailwind v4/Vite); compila a `dist/` para el WebView2 |

### Protocolo puente (UI ↔ nativo)

La UI nunca toca el SO directamente; todo pasa por el puente:

- Solicitud: `{"id", "operation", "payload?"}`
- Respuesta: `{"id", "success", "result?" | "error": {"code", "message"}}`
- Evento (nativo → UI): `{"event", "payload?"}`

Operaciones permitidas (Tasks 001–009): `system.info`, `tools.list`,
`tools.search`, `tool.execute`, `developer.list`, `developer.checkUpdate`,
`developer.update`, `developer.launch`, `winget.detect`, `winget.search`,
`winget.list`, `winget.updates`, `winget.install`, `winget.upgrade`,
`winget.upgradeAll`, `winget.uninstall`, `wsl.status`, `wsl.list`,
`wsl.update`, `wsl.shutdown`, `wsl.start`, `clamav.status`, `clamav.install`,
`clamav.updateDefinitions`, `clamav.quickScan`, `clamav.fullScan`,
`clamav.customScan`, `clamav.history`, `ssh.detect`, `ssh.list`,
`ssh.generate`, `ssh.inspect`, `ssh.importPublicKey`, `ssh.openDirectory`,
`pgp.generateKey`, `pgp.listKeys`, `pgp.importKey`, `pgp.exportKey`,
`pgp.encrypt`, `pgp.decrypt`, `pgp.sign`, `pgp.verify`.
Eventos: `app.ready`.

Errores conocidos: `MALFORMED_MESSAGE`, `OPERATION_NOT_FOUND`,
`OPERATION_FAILED`, `INTERNAL_ERROR`, y los controlados de las tools de
desarrollador: `NOT_SUPPORTED`, `NOT_INSTALLED`, `ELEVATION_REQUIRED`,
`UPDATE_FAILED`, `UPDATE_TIMEOUT`, `LAUNCH_FAILED`, los de winget:
`WINGET_FAILED`, `WINGET_TIMEOUT`, los de wsl: `WSL_FAILED`,
`WSL_TIMEOUT`, los de clamav: `CLAMAV_FAILED`, `CLAMAV_TIMEOUT`, los de
ssh: `SSH_FAILED`, `SSH_TIMEOUT` y los de pgp: `PGP_FAILED` (además de
`NOT_INSTALLED` cuando falta `ssh-keygen`).
La UI jamás muestra stack traces.

### Páginas de la UI

- **Inicio** — `system.info`.
- **Herramientas** — framework de tools (Task 002): lista/búsqueda y ejecución
  con editor JSON genérico.
- **Paquetes** — winget (Task 006): detección, búsqueda, instalados,
  actualizaciones y las 4 mutaciones con confirmación (id de paquete como único
  payload).
- **WSL** — Task 007: estado, distribuciones y update/shutdown/start con
  confirmación (update avisa que Windows puede pedir UAC).
- **ClamAV** — Task 008: estado del antivirus, instalación, actualización de
  definiciones, escaneos rápido/completo/personalizado (con ruta como único
  payload validado en .NET) e historial de escaneos.
- **Herramientas dev** — providers de desarrollador (Task 004).
- **Portapapeles** — smart detection (Task 010): al pegar contenido analiza
  JSON/JWT/clave SSH/PEM/tokens y sugiere la herramienta adecuada; lo sensible
  solo vive en memoria, jamás se persiste (§21).
- **Actividad** — centro de actividad (Task 010): operaciones en curso
  (spinner), completadas con duración y sus errores; los fallos de CLI
  (`succeeded:false`) se registran como error con `detail`.

### Elevación por operación (Task 005)

La app **nunca** se ejecuta como Administrador: cada operación que lo exige se
eleva puntualmente vía UAC (verb `runas`; el diálogo lo genera Windows — la app
no lo simula ni lo automatiza):

- `IElevationManager` (Core) lanza `PCSuite.Elevated.exe` **solo** si el
  identificador está en la allow-list `ElevatedOperationIds`: las operaciones
  desconocidas se rechazan en el proceso padre con **cero** prompts UAC.
- El proceso elevado (auxiliar de vida corta) expone un pipe nombrado con ACL
  restringida al SID del usuario actual (una sola instancia), valida la
  identidad del cliente y un nonce de 32 bytes comparado en tiempo constante,
  y habla JSON-lines con tope de 1 MiB por mensaje. No existe shell genérico:
  solo las operaciones registradas en el hijo pueden ejecutarse.
- Operación PoC: `elevated.adminInfo` (solo lectura: usuario, grupos del
  token y si el token elevado es administrador real).
- Códigos de error del protocolo elevado: `ELEVATION_CANCELLED`,
  `ELEVATION_TIMEOUT`, `ELEVATION_FAILED` y `UNAUTHORIZED`.

Los tests cubren autorización, allow-list, payload inválido, framing/tope e
IPC completo (servidor y cliente en el mismo proceso, sin pasar por UAC).

### WinGet (Task 006)

`IWinGetService` (Core) con su implementación `WinGetService` (Infrastructure)
gestiona paquetes a través del CLI winget con 8 operaciones puente: detección,
búsqueda, instalados, actualizaciones, install, upgrade, upgradeAll y
uninstall.

Seguridad (§10.1 / §11):

- El frontend solo envía payload tipado (`{"query"}` / `{"packageId"}`); la
  validación y la construcción de los **argv fijos** viven en .NET
  (`WinGetArguments`: id `^[A-Za-z0-9][A-Za-z0-9._+-]*$` ≤128, query 1..128
  sin controles ni guion inicial) — nunca una línea `winget …` arbitraria y
  nunca un shell. La validación ocurre **antes** de crear cualquier proceso.
- Las 4 mutaciones (install/upgrade/upgradeAll/uninstall) prueban primero sin
  privilegios; solo un fallo que parezca de permisos (salidas 5, 740,
  `0x80070005`, `0x800702E4` o marcas «access denied»/«acceso denegado»)
  reintentan elevadas contra la allow-list `ElevatedOperationIds`
  (`elevated.winget.*`), con **revalidación del id dentro del propio proceso
  elevado** (segunda barrera: cero procesos con entrada inválida). Cancelar el
  UAC de Windows vuelve `ELEVATION_CANCELLED`; el UAC nunca lo simula la app.
- Todas las invocaciones se serializan (semáforo 1:1 — dos winget en paralelo
  corrompen el índice de fuentes) y declaran UTF-8 en la salida: con la
  codepage OEM cada acento se vuelve mojibake y **desplaza** las columnas de
  las tablas posicionales.
- winget solo imprime texto (sin `--output json`): `WinGetTableParser` se
  ancla en las líneas de separación, corta columnas por posición
  (locale-independiente) y parsea **todas** las tablas (`winget upgrade` imprime
  dos), descartando resúmenes, notas y filas con id no válido.
- Los fallos del CLI no son errores del puente: vuelven como
  `WinGetMutationResult` estructurado (`succeeded`, `exitCode`, `elevated`,
  `detail` = última línea saneada ≤300 caracteres) o como `WINGET_FAILED` /
  `WINGET_TIMEOUT` con mensaje controlado — nunca salidas crudas ni stack
  traces. Timeouts: versión 30 s, consultas 120 s, mutaciones 900 s.

Los fixtures de tests son capturas reales (UTF-8) del CLI winget v1.29 en
locale español, congeladas en `tests/PCSuite.Application.Tests/Fixtures/`.

### WSL (Task 007)

`IWslService` (Core) con su implementación `WslService` (Infrastructure)
expone 5 operaciones puente de **payload cero**: estado, distribuciones,
actualizar, apagar y arrancar WSL.

Seguridad (§12 / §53):

- El frontend solo puede solicitar una de las 5 operaciones: **nunca** envía
  argumentos `wsl.exe`. Cada operación construye su argv fijo en .NET
  (`WslArguments`: `--version`, `--status`, `--list --verbose`, `--update`,
  `--shutdown`, `--exec true`) — cualquier payload que llegue se ignora por
  completo (ni se parsea → nunca `MALFORMED`) y jamás hay shell arbitrario.
- Solo `wsl.update` puede elevar: si falla por permisos reintenta vía
  `ElevatedOperationIds.WslUpdate` (allow-list, dos barreras) con revalidación
  dentro del proceso elevado. `--shutdown` y `--exec true` **jamás** piden UAC
  y ningún caso muestra un UAC preventivo (Windows es el único que genera el
  diálogo; la app no lo simula).
- Todas las invocaciones se serializan (semáforo 1:1) y declaran UTF-16LE sin
  BOM en la salida: wsl.exe escribe texto UTF-16 crudo en la tubería y con la
  codepage OEM cada acento se vuelve mojibake.
- `wsl.status` degrada con control: sin `wsl.exe` → `installed:false`; una
  lectura que falla → valor nulo sin romper el resto. Los fallos del CLI no
  son errores del puente: mutaciones vuelven `WslMutationResult`
  (`succeeded`, `exitCode`, `elevated`, `detail`) y las lecturas lanzan
  `WSL_FAILED` / `WSL_TIMEOUT` con mensaje controlado — nunca salidas crudas
  ni stack traces. Timeouts: lecturas 30 s, shutdown/start 120 s, update 900 s.

Los fixtures de tests son capturas reales (UTF-16LE decodificado) del CLI wsl
v2.7.14 en locale español, congeladas en
`tests/PCSuite.Application.Tests/Fixtures/`.

### ClamAV (Task 008)

`IClamAvService` (Core) con su implementación `ClamAvService` (Infrastructure)
expone 7 operaciones puente: estado, instalación, actualización de
definiciones, escaneo rápido/completo/personalizado e historial de escaneos.

Seguridad (§12 / §53):

- El frontend solo envía payload estructurado: `{"path"}` para
  `clamav.customScan` (validado en .NET: no vacío, sin NUL, ruta absoluta,
  normalizada con `GetFullPath` y existente) y **payload cero** para el resto
  (cualquier payload que llegue se ignora por completo). El argv de clamscan y
  freshclam se construye fijo en .NET (`ClamAvArguments`) — jamás hay shell
  arbitrario.
- `clamav.install` delega en `IWinGetService.InstallAsync("Cisco.ClamAV")` con
  idempotencia (ya instalado → mensaje controlado); son las **únicas** 2
  operaciones que declaran elevación (`install` y `updateDefinitions`), y
  siempre de forma preventiva: primero corren sin privilegios y solo un fallo
  con marcas de permisos («permission denied»/«permiso denegado», HRESULTs
  `0x80070005`/`0x800702E4`) reintenta vía
  `ElevatedOperationIds.ClamavUpdateDefinitions` (allow-list, dos barreras,
  argv fijo vacío y payload ignorado dentro del proceso elevado). Los escaneos
  **jamás** piden UAC.
- Escaneos y freshclam se serializan (semáforo 1:1) sobre las mismas
  definiciones; `clamav.status` e `clamav.history` responden fuera de la
  cola. Timeouts: lecturas 30 s, update 900 s, quick 900 s, custom 1 h,
  full 2 h. La salida va sin `OutputEncoding` (CRT OEM de freshclam/clamscan,
  a diferencia del UTF-16LE de wsl.exe).
- Exit 0/1 = éxito (1 = amenazas, con la lista `threats`); exit 2 con resumen
  interpretable = éxito parcial con mensaje fijo; resto → detalle saneado
  ≤300 caracteres o `CLAMAV_FAILED` / `CLAMAV_TIMEOUT` con mensaje
  controlado — nunca salidas crudas ni stack traces. El historial
  (`ClamAvHistoryStore`, 50 entradas, escritura atómica) devuelve `[]` ante
  datos corruptos o fallos de E/S.
- `clamav.status` degrada con control: sin `clamscan.exe` → `installed:false`
  (nunca un error) y la ausencia de `freshclam.exe` solo desactiva la
  actualización de definiciones.

ClamAV no está instalado en la máquina de desarrollo: los fixtures de tests
son salidas sintéticas con el formato documentado de clamscan/freshclam (las
etiquetas del resumen son SIEMPRE inglesas) y los tests jamás tocan los
directorios reales del equipo.

### SSH / PGP (Task 009)

`ISshKeyService` y `IPgpService` (Core) con sus implementaciones en
Infrastructure exponen 6 operaciones puente `ssh.*` (detect, list, generate,
inspect, importPublicKey, openDirectory) y 8 `pgp.*` (generateKey, listKeys,
importKey, exportKey, encrypt, decrypt, sign, verify).

Arquitectura: la generación de claves SSH usa el `ssh-keygen` de Windows con
**argv fijo** construido en .NET vía `IProcessRunner` (nunca shell); si no
existe el binario → `NOT_INSTALLED` con mensaje controlado. Detección,
listado, inspección, fingerprint e importación son **parseo .NET puro**
(`SshKeyParser`: OpenSSH/PEM/Base64, deduplicación por fingerprint,
`SHA256:` en Base64) sobre `~/.ssh` — nunca se ejecuta nada para leer claves.
PGP es **100 % BouncyCastle en proceso** (nunca `gpg` ni procesos externos):
keyring propio en `%APPDATA%\PCSuite\pgp` (`keyring.json` + anillos
`<FINGERPRINT>.pub.asc`/`.sec.asc`, escritura atómica y lectura tolerante a
corrupción).

Seguridad (§5 / §6 / §12):

- El frontend solo envía payloads tipados camelCase (fingerprints, emails,
  texto a cifrar, anillos armor); la validación y la resolución (prefijo de
  fingerprint o email exacto, sin ambigüedad) ocurren en .NET. Nada del
  payload se convierte en argv arbitrario y **cero shell** en toda la task.
- Ninguna operación `ssh.*`/`pgp.*` declara elevación (`RequiresElevation =
  false` en las 14): la app jamás se ejecuta como Administrador por esta
  task y no hay prompts UAC en su flujo.
- Sin `ILogger` en `PgpService` y logging mínimo en SSH: la contraseña, el
  texto plano, la clave privada y el argv (que puede contener la passphrase)
  **jamás se registran**; los errores son mensajes estáticos en español y
  nunca incluyen stack traces ni reflejan la entrada del usuario.
- Firmas: `pgp.sign` produce una firma detached armor; `pgp.verify` devuelve
  `valid` como **resultado legítimo** (firma inválida o desconocida →
  `valid:false` con detalle, `success:true`), no como error del puente. El
  descifrado distingue passphrase incorrecta, mensaje no cifrado para el
  keyring local y mensaje corrupto con códigos fijos.
- Límites: anillos/entradas armor acotados a 1 MiB, texto plano a 4 MiB,
  contraseñas con tope y sin NUL; las claves privadas solo salen del keyring
  si se pide `includePrivate` explícito.

### Production UX (Task 010)

Frontend puro — sin operaciones nativas nuevas. Todo se persiste en
`localStorage` con prefijo `pcsuite.` (`ponytail:` frente al §30 SQLite, que
sería over-engineering para claves-valor de UI):

- **Temas** — oscuro (por defecto), claro y «sistema» (`matchMedia`).
- **Ajustes** — notificaciones y detección de portapapeles como toggles
  persistidos; el tema también se cambia desde ahí.
- **Favoritos** — estrellas en el menú y en las tarjetas de herramientas;
  se agrupan bajo «FAVORITOS».
- **Búsqueda global (Ctrl + K)** — paleta difusa sobre páginas, herramientas,
  acciones de tema y ayuda.
- **Atajos** — registro global (`Ctrl + K`, `?`, `Ctrl + ,`) con panel de ayuda.
- **Notificaciones** — toasts al terminar operaciones (éxito solo en
  mutaciones; errores siempre), auto-dismiss a los 5 s y respetando el toggle
  de Ajustes. Las operaciones ruidosas (`tools.list`, `system.info`, búsquedas)
  no generan actividad ni toasts.
- **Actividad** — consumida desde el ciclo de vida del puente (`bridge.onCall`),
  sin instrumentar el host.
- **Primer arranque** — overlay de bienvenida que se cierra con «Empezar».

## Tests

```powershell
dotnet test PCSuite.sln
```

Cubren serialización del protocolo (puente y elevación), enrutado con
allow-list, el motor de procesos/jobs, los providers de desarrollador, las
herramientas, el framework de elevación (autorización, IPC, payload inválido),
winget (parser de tablas contra fixtures reales, validación/argv, detección,
mutaciones con elevación por permisos y codificación UTF-8 de la salida), wsl
(parser contra fixtures reales, payload cero ignorado, elevación solo en
`--update` y codificación UTF-16LE de la salida), clamav (parser contra
fixtures sintéticos, argv fijos, historial con E/S tolerantes, payload
ignorado salvo `customScan` y elevación solo en freshclam por permisos) y
ssh/pgp (parser y fingerprint cross-validados con `ssh-keygen -lf`, argv fijo
de generación, payloads hostiles ignorados, keyring en carpetas temporales
que jamás tocan `~/.ssh` ni `%APPDATA%` real, round-trips criptográficos
cifrar→descifrar y firmar→verificar con BouncyCastle, passphrase jamás
reflejada en errores y firma inválida como resultado `valid:false`, no como
error).

## Logs

`%LOCALAPPDATA%\PCSuite\Logs\pcsuite-yyyy-MM-dd.log`
