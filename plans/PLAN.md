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
| Task 009 — SSH / PGP (SshKeyService, PgpService + tests) | ⏳ Pendiente | — |
| Task 010 — Production UX (búsqueda global, favoritos, clipboard, Activity, notificaciones, temas, settings) | ⏳ Pendiente | — |

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
