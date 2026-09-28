# Plan de Acción — PCSuite

**PCSuite es una aplicación de escritorio para Windows construida con .NET 10 y
WebView2.** El host nativo (WinForms + WebView2) es el centro del proyecto; el
frontend React/TypeScript corre embebido en el WebView2 como capa de UI interna
— no es una web app desplegable ni un proyecto independiente.

Fuente: `PCSuite_Development_Plan.md` (secciones 47–56).

| Feature | Estado | Fecha |
|---|---|---|
| Task 001 — Bootstrap de la app de escritorio (sln + 6 proyectos .NET 10 + WebView2 + puente `getSystemInfo` + Home + logging + tests) | ✅ Completado | 2026-09-21 |
| Task 002 — Tool Framework (ITool, IModule, ToolRegistry, ToolSearchService + JSON Formatter, UUID, Base64, Hash, Password Generator) | ✅ Completado | 2026-09-22 |
| Task 003 — Process & Job Engine (IProcessRunner, IJobManager, IJobEventPublisher) | ✅ Completada | 2026-09-22 |
| Task 004 — Developer Providers (Node, npm, pnpm, Bun, Claude Code — detección, versión, actualización y lanzamiento) | ✅ Completado | 2026-09-22 |
| UI — Página "Herramientas dev" (bridge ops `developer.*` + página React con detección, actualización y lanzamiento) | ✅ Completado | 2026-09-22 |
| Task 005 — Elevation Framework (IElevationManager, PCSuite.Elevated, PoC + tests de seguridad) | ✅ Completado | 2026-09-22 |
| Task 006 — WinGet (IWinGetService, operaciones estructuradas, UAC) | ✅ Completado | 2026-09-22 |
| Task 007 — WSL (IWslService: status/list/update/shutdown/start) | ✅ Completado | 2026-09-25 |
| UI — Páginas "Paquetes" (winget.*) y "WSL" (wsl.*) en el frontend React | ✅ Completado | 2026-09-25 |
| Task 008 — ClamAV (IClamAvService + historial de escaneos) | ✅ Completado | 2026-09-25 |
| UI — Página "ClamAV" (clamav.* en el frontend React: estado, install/updateDefinitions, 3 escaneos e historial) | ✅ Completado | 2026-09-25 |
| Task 009 — SSH / PGP (SshKeyService, PgpService + tests) — dividida en 7 partes (9.1–9.7) | ✅ Completado | 2026-09-25 |
| ├ 9.1 — SSH: contratos + núcleo (modelos `SshKeyInfo`, payloads/resultados `ssh.*` en Contracts, `ISshKeyService` + parser/fingerprint .NET puro en Core) + 22 tests, fingerprints cross-validados con `ssh-keygen -lf` | ✅ Completada | 2026-09-25 |
| ├ 9.2 — SSH: infraestructura — `SshKeyService` (generación por argv fijo con `ssh-keygen`, detección, listado deduplicado por fingerprint, inspección, importación atómica sin sobrescritura, abrir carpeta) + 32 tests (+2 de round-trip del parser) | ✅ Completada | 2026-09-25 |
| ├ 9.3 — SSH: operaciones del puente `ssh.*` (6) + registro en el composition root de MainForm + tests de contrato/seguridad (argv fijo, payloads hostiles, sin elevación) | ✅ Completada | 2026-09-25 |
| ├ 9.4 — PGP: contratos + núcleo (`IPgpService`, modelos, `pgp.*` en OperationIds) + keyring store tolerante a E/S con escritura atómica + tests | ✅ Completada | 2026-09-25 |
| ├ 9.5 — PGP: claves con BouncyCastle — generación (nombre/email/algoritmo/expiración/passphrase), importación, exportación, listado/inspección + tests round-trip | ✅ Completada | 2026-09-25 |
| ├ 9.6 — PGP: cifrado/descifrado/firma/verificación + operaciones del puente `pgp.*` + registro DI + tests round-trip criptográficos | ✅ Completada | 2026-09-25 |
| └ 9.7 — Integración final: README, build + suite completa en verde, la app compila y lanza | ✅ Completada | 2026-09-25 |
| Task 010 — Production UX (temas, settings, favoritos, búsqueda global, atajos, notificaciones/activity, clipboard, first-run) — dividida en 9 partes (10.1–10.9) | ✅ Completada | 2026-09-26 |
| ├ 10.1 — Temas: sistema de design tokens centralizado + dark/light/system (dark como experiencia primaria) con persistencia de la preferencia | ✅ Completada | 2026-09-26 |
| ├ 10.2 — Settings: página "Ajustes" (sidebar ⚙) con las preferencias de UX (tema, toggles de notificaciones/detección) sobre el store de preferencias | ✅ Completada | 2026-09-26 |
| ├ 10.3 — Favoritos: estrella en tools/ops, sección "⭐ Favorites" en la sidebar con navegación, persistente | ✅ Completada | 2026-09-26 |
| ├ 10.4 — Búsqueda global: paleta Ctrl+K (command palette) sobre tools, ops, ajustes y devs con tags y fuzzy matching | ✅ Completada | 2026-09-26 |
| ├ 10.5 — Atajos de teclado: registro global de shortcuts + overlay de ayuda con la lista | ✅ Completada | 2026-09-26 |
| ├ 10.6 — Notificaciones + Activity center: toasts globales (éxito/aviso/error) y panel Activity con operaciones en curso/completadas y su detalle | ✅ Completada | 2026-09-26 |
| ├ 10.7 — Smart detection (clipboard): detección de JSON/JWT/clave SSH/PEM con sugerencia de tool; contenido sensible solo en memoria, jamás persistido (§21) | ✅ Completada | 2026-09-26 |
| ├ 10.8 — First-run experience: pantalla de bienvenida/onboarding en el primer arranque | ✅ Completada | 2026-09-26 |
| └ 10.9 — Integración final: README, build + suite completa en verde, la app compila y lanza | ✅ Completada | 2026-09-26 |
| Task 011 — Limpiador (limpieza de archivos temporales en raíces fijas) — dividida en 4 partes (11.1–11.4) | ✅ Completada | 2026-09-28 |
| ├ 11.1 — Contratos + núcleo: `ICleanerService` (Core) + modelos/payloads/resultados `cleaner.*` en Contracts + OperationIds + tests | ✅ Completada | 2026-09-27 |
| ├ 11.2 — Infraestructura: `CleanerService` (escaneo y borrado seguro en raíces fijas, tolerancia a bloqueos/ACL, sin saltar reparse points) + tests | ✅ Completada | 2026-09-27 |
| ├ 11.3 — Operaciones del puente `cleaner.scan` / `cleaner.clean` + registro en composition root + tests de contrato/seguridad | ✅ Completada | 2026-09-27 |
| └ 11.4 — UI: página "Limpiador" (nav habilitada, escaneo con tamaños, confirmación y resultados) + build y recorrido | ✅ Completada | 2026-09-28 |

## Desglose Task 009 — decisiones de diseño (previas a implementar)

- **SSH**: generación con `ssh-keygen` de Windows (implementación madura; argv
  fijo vía `IProcessRunner`, nunca shell; si no está disponible → error
  controlado `NOT_INSTALLED` con mensaje claro). Detección, listado,
  inspección, fingerprint SHA256 e importación de claves públicas 100% en
  .NET con BouncyCastle (`OpenSshPublicKeyUtilities`), testeables sin
  binarios externos.
- **PGP**: BouncyCastle en proceso (`Org.BouncyCastle.Bcpg.OpenPgp`) — GnuPG
  no existe en Windows por defecto. Keyring propio en
  `%APPDATA%\PCSuite\pgp` con escritura atómica (tmp+move, patrón del
  historial de ClamAV); claves secretas protegidas con passphrase (S2K) cuando
  el usuario la indica.
- Sin elevación (UAC) en toda la task: todo escribe en directorios de usuario.
- Nunca se registran passphrase, texto claro ni material de clave privada.
- Sonda previa verificada: `ssh-keygen` presente en
  `C:\WINDOWS\System32\OpenSSH\`; BC 2.x produce y lee el contenedor real
  `openssh-key-v1` (round-trip comprobado).

## Desglose Task 010 — decisiones de diseño (previas a implementar)

- **Frontend-first**: todo el trabajo vive en `frontend/` (React/TS); sin
  nuevas mutaciones nativas ni elevación. Único contacto opcional con el
  puente en 10.7: una operación tipada de solo lectura de clipboard si el
  gesto manual no basta — nunca logging ni persistencia de su contenido.
- **Persistencia**: preferencias de UX (tema, favoritos, toggles, first-run
  hecho) en `localStorage` del WebView2. `ponytail:` vs §30 (SQLite): para
  claves-valor de UX locales SQLite sería over-engineering; si el nativo las
  necesita algún día → store JSON atómico (patrón `ClamAvHistoryStore`).
- **Activity/Notificaciones**: se alimentan del ciclo de vida de las llamadas
  al puente que el propio frontend dispara (request/response + eventos), sin
  instrumentar el host; sin stack traces ni datos sensibles en la UI.
- **Clipboard (§20–21)**: detección puntuada y sugerencia de tool; nada del
  contenido se escribe a disco ni a logs — los datos sensibles (claves,
  tokens, JWT, contraseñas) solo viven en memoria.
- **Commits**: cada parte se cierra con `UI: ...`; el cierre de la task con
  `Task 010: Production UX - ...`.

## Desglose Task 011 — decisiones de diseño (previas a implementar)

- **Alcance**: "Archivos temporales" (§16 "Future: Temporary file cleanup"):
  3 categorías de raíces FIJAS — Temp del usuario (`%LOCALAPPDATA%\Temp`),
  `C:\Windows\Temp` y la caché de Windows Update
  (`SoftwareDistribution\Download`). El frontend solo envía ids de categoría
  (`userTemp`/`windowsTemp`/`updateCache`) validados contra la lista cerrada
  del backend; jamás rutas ni argumentos.
- **Borrado seguro**: enumeración recursiva con `IgnoreInaccessible` y
  `AttributesToSkip = ReparsePoint` — una junction dentro de la raíz no puede
  sacar el borrado fuera de ella (test explícito). Sin elevación: lo que la
  ACL no permite se omite y se contabiliza (`skippedFiles` + `detail`), nunca
  rompe la operación ni pide UAC.
- **2 operaciones**: `cleaner.scan` (payload cero; tamaños/ficheros por
  categoría) y `cleaner.clean` (payload `{"categories":[ids]}`; ids
  desconocidos/vacío → `CLEANER_FAILED` controlado, sin eco de la entrada).
  `cleaner.clean` entra en el regex de MUTATION (toast); `cleaner.scan` se
  registra como cualquier lectura de página.
- **UI**: página Limpiador con escaneo → tamaños por categoría con checkbox →
  `window.confirm` antes de borrar → resultado (borrados/liberados/omitidos).
- **Commits**: cada parte se cierra con `Task 011: ...`; la UI con
  `UI: Task 011 (11.4) ...`.

## Reglas del proyecto (aplican a todas las tasks)

- Trabajo incremental: la app debe compilar y lanzarse al cierre de cada task.
- El frontend solo habla con el nativo por el puente de mensajes tipado; nunca
  se expone ejecución arbitraria de comandos a la UI.
- La app corre siempre con privilegios de usuario normal; la elevación es por
  operación (Task 005), nunca del proceso completo.
- Sin secretos en logs; sin stack traces en la UI.
- El frontend (`frontend/`) es un artefacto de build interno: produce
  `frontend/dist`, que el host WebView2 sirve desde el host virtual
  `https://app.pcsuite.local`. No se despliega por separado.
