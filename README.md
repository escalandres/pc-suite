# PCSuite

Suite de utilidades para Windows (.NET 10 + WebView2 + React/TypeScript).

## Requisitos

- .NET SDK 10.0.401 o superior
- Node.js 24+ y npm (para el frontend)
- WebView2 Runtime (incluido en Windows 11 y en Windows 10 actualizado)

## Compilar

```powershell
# Frontend
npm --prefix frontend install
npm --prefix frontend run build

# Solución completa + tests
dotnet build PCSuite.sln
dotnet test PCSuite.sln
```

## Ejecutar

```powershell
dotnet run --project src/PCSuite.Desktop
```

La app **nunca debe ejecutarse como Administrador**; la elevación es por
operación y llega en la Task 005.

### Desarrollo con hot reload

```powershell
# Terminal 1: dev server de Vite
npm --prefix frontend run dev

# Terminal 2: app apuntando al dev server
$env:PCSUITE_DEV_URL = "http://localhost:5173"
dotnet run --project src/PCSuite.Desktop
```

Sin `PCSUITE_DEV_URL`, la app sirve `frontend/dist` mediante el host virtual
`https://app.pcsuite.local`.

## Arquitectura

| Proyecto | Responsabilidad |
|---|---|
| `PCSuite.Contracts` | Mensajes del protocolo puente (request/response/event) y IDs |
| `PCSuite.Core` | Abstracciones: `IOperation`, `OperationContext`, `OperationResult` |
| `PCSuite.Application` | `CommandRouter`: enrutado de operaciones con allow-list, nunca lanza excepciones |
| `PCSuite.Infrastructure` | Logging a archivo y operaciones concretas (`system.info`) |
| `PCSuite.Desktop` | WinForms + WebView2, puente de mensajes, origen/policy de navegación |
| `PCSuite.Elevated` | Proceso auxiliar para operaciones elevadas (Task 005) |
| `frontend/` | SPA React + TypeScript + Tailwind v4 (Vite) |

### Protocolo puente

- Solicitud: `{"id", "operation", "payload?"}`
- Respuesta: `{"id", "success", "result?" | "error": {"code", "message"}}`
- Evento: `{"event", "payload?"}`

Operaciones permitidas (Task 001): `system.info`.
Eventos: `app.ready`.

Errores conocidos: `MALFORMED_MESSAGE`, `OPERATION_NOT_FOUND`,
`OPERATION_FAILED`, `INTERNAL_ERROR`. Las UI jamás muestran stack traces.

## Logs

`%LOCALAPPDATA%\PCSuite\Logs\pcsuite-yyyy-MM-dd.log`

## Tests

```powershell
dotnet test PCSuite.sln
```

Cubren serialización del protocolo, enrutado del comando y la operación
`system.info` (incluida la política de allow-list).
