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

**Nunca ejecutar la app como Administrador.** La elevación es por operación y
llega en la Task 005.

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
| `PCSuite.Elevated` | Proceso auxiliar para operaciones elevadas (Task 005) |
| `tests/PCSuite.Application.Tests` | Tests xUnit |
| `frontend/` | UI embebida (React + TS + Tailwind v4/Vite); compila a `dist/` para el WebView2 |

### Protocolo puente (UI ↔ nativo)

La UI nunca toca el SO directamente; todo pasa por el puente:

- Solicitud: `{"id", "operation", "payload?"}`
- Respuesta: `{"id", "success", "result?" | "error": {"code", "message"}}`
- Evento (nativo → UI): `{"event", "payload?"}`

Operaciones permitidas (Task 001): `system.info`.
Eventos: `app.ready`.

Errores conocidos: `MALFORMED_MESSAGE`, `OPERATION_NOT_FOUND`,
`OPERATION_FAILED`, `INTERNAL_ERROR`. La UI jamás muestra stack traces.

## Tests

```powershell
dotnet test PCSuite.sln
```

Cubren serialización del protocolo, enrutado del comando y la operación
`system.info` (incluida la política de allow-list).

## Logs

`%LOCALAPPDATA%\PCSuite\Logs\pcsuite-yyyy-MM-dd.log`
