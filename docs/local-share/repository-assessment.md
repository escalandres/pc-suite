# Local Share — Repository Assessment

**Fase:** 0 (Reconocimiento del repositorio)
**Fecha:** 2026-10-11
**Autor:** Agente de IA (OpenCode)

---

## 1. Arquitectura de PCSuite

### 1.1 Proyectos

| Proyecto | Responsabilidad | Referencias |
|---|---|---|
| `PCSuite.Core` | Interfaces de dominio, modelos, contratos puros | Ninguna (leaf) |
| `PCSuite.Contracts` | DTOs del bridge, OperationIds, EventIds, BridgeJson | Ninguna |
| `PCSuite.Application` | Servicios de aplicación, JobManager, ToolRegistry | Core, Contracts |
| `PCSuite.Infrastructure` | Implementaciones (procesos, WinGet, ClamAV, SSH, PGP, tools) | Core, Contracts, Application |
| `PCSuite.Desktop` | Host WinForms + WebView2, composition root, MainForm | Todas |
| `PCSuite.Elevated` | Helper para UAC (runas), elevación por operación | Core, Contracts, Infrastructure |
| `PCSuite.Blazor` | Piloto Blazor Hybrid (Task 015, bloqueado) | Core, Application, Infrastructure, Contracts |

### 1.2 Comunicación Frontend ↔ Backend

- **Canal:** `window.chrome.webview.postMessage` → `WebViewBridge` → `CommandRouter`
- **Operaciones:** allow-list registrada en el constructor de MainForm (≈40 ops)
- **Eventos:** `IEventPublisher.SendEvent()` → `DeferredEventPublisher` → `WebViewBridge` → React listeners
- **Serialización:** `BridgeJson` (camelCase, `WhenWritingNull` — campos ausentes ≠ false/null)
- **Errores:** `BridgeErrorCodes` tipados, mensajes controlados sin stack traces
- **Patrón Job:** operaciones largas (escaneos) devuelven `{jobId}` al instante; desenlace por `job.event` con `Job.Result`; cancelación por `job.cancel` → `IJobManager.RequestCancel` → token por job

### 1.3 Componentes reutilizables

| Componente | Ubicación | Uso en Local Share |
|---|---|---|
| `IProcessRunner` | Core/Processes | No aplica (transferencias no son procesos) |
| `IJobManager` | Core/Jobs | **Sí** — cada transferencia es un Job con ciclo de vida, cancelación y progreso |
| `IJobEventPublisher` | Core/Jobs | **Sí** — eventos de progreso de transferencia hacia la UI |
| `IEventPublisher` | Core/Events | **Sí** — eventos de descubrimiento de dispositivos, solicitudes entrantes |
| `BridgeJson` | Contracts | **Sí** — serialización de DTOs del bridge |
| `FileLoggerProvider` | Infrastructure/Logging | **Sí** — logging estructurado de transferencias |
| `ElevationManager` | Infrastructure/Elevation | **Sí** — regla de firewall si el usuario la autoriza |
| `PCSuitePaths` | Infrastructure | **Sí** — directorio de datos (dispositivos emparejados, historial) |
| Design tokens CSS | frontend/src/index.css | **Sí** — `--pcs-*` vars, tema oscuro, density |

### 1.4 Frontend (React + TypeScript)

- **Estructura:** páginas en `frontend/src/pages/`, componentes en `components/`
- **Routing:** hash-based (`#/page`, `#/page/tool-id`), validado en App.tsx
- **Navegación:** `NAV_ITEMS` en AppLayout.tsx, sidebar colapsable con favoritos
- **Estado:** React hooks (`useState`, `useEffect`), sin state manager externo
- **Bridge client:** `bridge.call<tipo>(OperationIds.x, payload, timeout)` y `bridge.on(EventIds.x, handler)`
- **Styling:** Tailwind CSS 4 + inline styles con `var(--pcs-*)`
- **Build:** Vite → `frontend/dist` (embebido en WebView2 via host virtual `https://app.pcsuite.local`)

### 1.5 Testing

- **Framework:** xUnit, 711+ tests en `tests/PCSuite.Application.Tests`
- **Patrones:** FakeProcessRunner, FakeElevationManager, FakeWinGetService, InlineProgress, ClamAvFixture
- **Verificación:** `dotnet test` — todo en verde salvo 1 test ambiental (junction)

### 1.6 Empaquetado

- `empaquetar.bat` → self-contained zip (51 MB) + carpeta portable
- `instalador.iss` → Setup.exe (38 MB) via Inno Setup
- `iniciar.bat` → dev: compila frontend + .NET + lanza

---

## 2. Decisiones técnicas para Local Share

### 2.1 Transporte: TCP + `SslStream` (no HTTP)

**Decisión:** protocolo TCP binario custom sobre `SslStream`, NO ASP.NET Core/HTTP.

**Justificación:**
- Evita añadir ~50MB de paquetes ASP.NET Core al Desktop project
- `SslStream` es built-in en .NET 10, maduro y excelente
- Protocolo binario con frames de longitud prefijada es más ligero que HTTP overhead
- Más fácil de testear (dos `TcpClient` en localhost sin infraestructura web)
- La propuesta sugiere HTTP como opción pero acepta "un protocolo de streaming equivalente"

**Estructura del protocolo:**
```
[Header: 4 bytes magic + 1 byte version + 1 byte messageType + 4 bytes payloadLength]
[Payload: JSON o binario según messageType]
```

### 2.2 Descubrimiento: mDNS custom (UDP multicast)

**Decisión:** implementar un responder mDNS mínimo via UDP multicast, no depender de bibliotecas externas.

**Justificación:**
- **Zeroconf** (NuGet): poco mantenida, última actualización hace 2+ años
- **Makaretu.Dns**: más completa pero compleja para el caso de uso
- **Windows DNS-SD WinRT**: acceder WinRT desde .NET 10 Desktop es problemático
- mDNS es un protocolo bien documentado (RFC 6762) y un responder mínimo para anunciarse en la LAN es ~200 líneas
- El servicio anuncia `_pcshare._tcp.local` con el nombre del equipo y puerto
- **Fallback obligatorio:** conexión manual por IP:puerto (Fase 16.2), mDNS es mejora posterior (Fase 16.3)

### 2.3 Cifrado: TLS autofirmado con TOFU

- Generar certificado autofirmado por instalación al primer arranque (`CertificateRequest`)
- Emparejamiento = intercambio de huellas SHA-256 de certificado (TOFU)
- `ProtectedData` (DPAPI) para persistir claves privadas
- `SslStream` valida la huella del par contra la lista de dispositivos emparejados

### 2.4 Integridad: SHA-256 en streaming

- El emisor computa el hash mientras lee el archivo (`IncrementalHash`)
- Envía el hash en los metadatos antes del contenido
- El receptor computa el hash mientras escribe (`IncrementalHash`)
- Al final: comparar. Si no coincide → descartar archivo temporal, marcar transferencia como fallida
- No hay "fase de verificación" separada — ya se verificó durante la transferencia

### 2.5 Sistema de archivos

- Carpeta de recepción configurable (default: `Documents\PCSuite\Received`)
- Archivos temporales en subcarpeta `.pcshare-tmp` con nombres GUID
- Validación de rutas: rechazar absolutas, `..`, UNC, caracteres de control
- `Path.GetFullPath()` + verificación de que empieza con el directorio de recepción
- No sobrescribir: si existe, añadir sufijo `(1)`, `(2)`...

---

## 3. Operaciones del bridge

```
localshare.status          → estado del servicio (activo/detenido/puerto/errores)
localshare.start           → iniciar listener + mDNS
localshare.stop            → detener listener + mDNS
localshare.devices         → listar dispositivos detectados
localshare.pair            → emparejar (payload: { address, port, pin })
localshare.revoke          → revocar confianza (payload: { deviceId })
localshare.send            → iniciar envío (payload: { deviceId, items: [{path, relativePath}] })
localshare.accept          → aceptar solicitud entrante (payload: { transferId })
localshare.reject          → rechazar solicitud entrante (payload: { transferId })
localshare.transfers       → listar transferencias (activas + historial)
localshare.config          → leer/escribir configuración
```

**Eventos:**
```
localshare.device-discovered    → nuevo dispositivo mDNS detectado
localshare.device-lost          → dispositivo dejó de responder
localshare.incoming-request     → solicitud de transferencia entrante
localshare.transfer-progress    → progreso (bytes, velocidad, ETA)
localshare.transfer-completed   → transferencia terminada (éxito/fallo/cancelada)
```

---

## 4. Ubicación del módulo

```
src/
├── PCSuite.Core/
│   └── LocalShare/
│       ├── ILocalShareService.cs      (interfaz de dominio)
│       ├── LocalShareModels.cs        (Device, Transfer, TransferItem, enums)
│       └── LocalShareSettings.cs      (configuración tipada)
├── PCSuite.Contracts/
│   └── (DTOs + OperationIds + EventIds para localshare.*)
├── PCSuite.Infrastructure/
│   └── LocalShare/
│       ├── LocalShareService.cs       (implementación: ciclo de vida, coordinación)
│       ├── Discovery/
│       │   └── MdnsResponder.cs      (UDP multicast, RFC 6762 mínimo)
│       ├── Protocol/
│       │   ├── ShareProtocol.cs      (frames binarios, handshake, mensajes)
│       │   └── TlsServer.cs          (SslStream listener + certificados)
│       ├── Transfers/
│       │   ├── TransferServer.cs     (recepción de archivos)
│       │   ├── TransferClient.cs     (envío de archivos)
│       │   └── IntegrityStream.cs    (SHA-256 en streaming)
│       ├── Pairing/
│       │   ├── PairingManager.cs     (TOFU, huellas, dispositivos confiables)
│       │   └── CertificateStore.cs   (generación + DPAPI)
│       └── Storage/
│           └── SafeFileWriter.cs     (validación de rutas, nombres temporales)
├── PCSuite.Desktop/
│   └── MainForm.cs                    (registro de operaciones localshare.*)
└── frontend/src/pages/
    └── LocalShare.tsx                (página principal del módulo)
```

---

## 5. Riesgos y mitigaciones

| Riesgo | Severidad | Mitigación |
|---|---|---|
| mDNS no funciona en algunas redes (aislamiento AP, VLAN) | Medio | Conexión manual por IP:puerto como fallback obligatorio desde Fase 16.2 |
| Firewall bloquea el puerto | Medio | Diagnóstico claro + regla opcional via ElevationManager con UAC |
| Certificados autofirmados generan warnings | Bajo | TOFU: emparejamiento explícito con verificación de huella, luego confianza |
| Archivos muy grandes (>10GB) | Bajo | Streaming con buffers de 64KB — no hay límite de tamaño inherente |
| Transferencia interrumpida | Medio | MVP: marcar como fallida, eliminar temporal. Post-MVP: reanudación por bloques |
| Race condition en múltiples transferencias | Bajo | IJobManager serializa; TransferServer usa SemaphoreSlim para aceptar una a la vez |

---

## 6. Plan de implementación ajustado

| Fase | Descripción | Estimación | Precedencia |
|---|---|---|---|
| 16.1 | Diseño y contratos (estados, protocolo, bridge ops) | 0.5 día | Antes de todo |
| 16.2 | Transporte TCP+TLS, transferencia streaming, conexión manual | 2-3 días | Núcleo — hacer primero |
| 16.3 | mDNS descubrimiento + emparejamiento | 2-3 días | Después del núcleo |
| 16.4 | Integridad SHA-256, escritura segura, colisiones | 1-2 días | Integrado con 16.2 |
| 16.5 | UI React (5 vistas) | 2-3 días | En paralelo con 16.3 |
| 16.6 | Robustez, firewall, docs, empaquetado | 1-2 días | Final |

**Total estimado:** 8-13 días (más agresivo que el 13-24 de la propuesta original por reutilizar IJobManager y conocimiento profundo del repo).

---

## 7. Bibliotecas NuGet necesarias

| Paquete | Versión | Propósito | Tamaño añadido |
|---|---|---|---|
| `System.Security.Cryptography.ProtectedData` | 9.0.x | DPAPI para claves de certificados | ~50 KB |
| *(nada más)* | — | TcpClient, SslStream, SHA256, Aes: todo built-in | — |

**Sin dependencias externas para mDNS** — se implementa el responder custom.

---

## 8. Conclusiones

1. **La arquitectura de PCSuite soporta Local Share sin cambios estructurales.** El patrón Job (IJobManager) resuelve el ciclo de vida de transferencias; el bridge existante transporta todas las operaciones; el EventPublisher lleva progreso a la UI.

2. **La pieza nueva más compleja es el servidor TCP+TLS**, pero `SslStream` de .NET 10 es maduro y el protocolo binario es simple de definir.

3. **mDNS se pospone a 16.3** para asegurar que el transporte funcione primero con conexión manual.

4. **El MVP se puede testear completamente en localhost** (dos instancias en puertos distintos), eliminando la necesidad de dos equipos físicos para CI.

---

*Siguiente paso: Fase 16.1 — Diseño y contratos (estados, protocolo, operaciones del bridge).*
