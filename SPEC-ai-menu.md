# Spec: Menú IA + markItDown + Ollama (PCSuite)

## Objective

Añadir una nueva página "IA" a PCSuite que permita:
1. Detectar si Python y markItDown están instalados en el sistema.
2. Instalar markItDown vía pip si no está presente.
3. Seleccionar un archivo (PDF, DOCX, XLSX, PPTX, imagen, audio, HTML, etc.) y convertirlo a Markdown para usarlo como contexto en IA.
4. El archivo `.md` se genera en la **misma ubicación** que el archivo original (junto al archivo de entrada), con el mismo nombre y extensión `.md`.
5. El usuario puede abrir la carpeta del archivo generado o copiar su ruta al portapapeles.
5. Detectar si Ollama está instalado en el sistema.
6. Listar los modelos de Ollama instalados localmente (con nombre, tamaño y fecha de modificación).

markItDown (Microsoft) es un paquete Python que convierte muchos formatos de archivo a Markdown. Ollama es un ejecutable independiente para ejecutar modelos de lenguaje locales. PCSuite orquesta ambos CLIs, sin ser el motor de conversión ni de inferencia.

## ASSUMPTIONS I'M MAKING

1. markItDown se distribuye como paquete Python (`pip install markitdown`).
2. Python puede o no estar instalado en el sistema del usuario.
3. Se detecta Python con `python --version` / `python3 --version`.
4. Se detecta markItDown con `python -m pip show markitdown`.
5. Se instala con `python -m pip install markitdown` (puede requerir UAC si el entorno de Python lo exige).
6. La conversión se ejecuta con `python -m markitdown <ruta>` y el Markdown sale por stdout.
7. La conversión puede ser lenta (PDFs con OCR, audios con transcripción) → usa el patrón de JOB existente (responde `{jobId}` al instante, desenlace por evento/polling).
8. La selección de archivo reusa el diálogo nativo `system.pickFiles` (ya implementado, Task 026).
9. El resultado Markdown se devuelve como texto estructurado (no se guarda en disco por defecto; el usuario lo copia).
10. Los cambios de protocolo son aditivos: compatible con versiones anteriores.
11. Ollama se distribuye como ejecutable independiente (`ollama` en el PATH).
12. Se detecta Ollama con `ollama --version`.
13. Se listan modelos con `ollama list` (parseo de NAME / SIZE / MODIFIED).
14. Ollama NO se instala automáticamente desde PCSuite (es una instalación grande y compleja); solo se detecta y lista modelos.

## Tech Stack

- Backend: .NET 10, C# (mismo patrón que ClamAV / Windows operations)
- Frontend: React 19, TypeScript, Tailwind CSS 4, Vite
- Comunicación: Typed message bridge (WebView2)
- Externo: Python + markItDown (paquete de usuario, no incluido en PCSuite), Ollama (ejecutable de usuario, no incluido en PCSuite)

## Commands

```
# Backend build
dotnet build src/PCSuite.Desktop

# Backend tests
dotnet test

# Frontend build
npm --prefix frontend run build

# Frontend lint/typecheck
npm --prefix frontend run check
# (o equivalente según package.json)
```

## Project Structure (files to touch)

Backend (.NET):
- `src/PCSuite.Contracts/OperationIds.cs` — añadir `ai.detect`, `ai.install`, `ai.convert`
- `src/PCSuite.Contracts/BridgeErrorCodes.cs` — añadir `AiFailed`, `AiNotInstalled`, `AiConversionFailed`
- `src/PCSuite.Contracts/AiResults.cs` (nuevo) — DTOs: `AiStatusResult`, `AiMutationResult`, `AiConvertResult`
- `src/PCSuite.Core/AI/IAiService.cs` (nuevo) — interfaz de servicio
- `src/PCSuite.Infrastructure/AI/AiService.cs` (nuevo) — implementación: detect python/pip/markitdown, install, convert
- `src/PCSuite.Infrastructure/Operations/AiOperations.cs` (nuevo) — operacións del puente: `AiDetectOperation`, `AiInstallOperation`, `AiConvertOperation`, `OllamaDetectOperation`, `OllamaListModelsOperation`
- `src/PCSuite.Desktop/MainForm.cs` — instanciar servicio y registrar operaciones en el router

Frontend (React/TypeScript):
- `frontend/src/types/bridge.ts` — añadir OperationIds y tipos de resultado IA
- `frontend/src/pages/Ai.tsx` (nuevo) — página IA: detectar, instalar, seleccionar archivo, convertir, mostrar markdown, copiar
- `frontend/src/layouts/AppLayout.tsx` — añadir `ai` a `PageId` y `NAV_ITEMS`
- `frontend/src/App.tsx` — añadir ruta `case 'ai': return <AiPage />`

## Code Style

Seguir las convenciones existentes del repo:

- C#: nombres PascalCase, interfaces con `I` prefijo, excepciones tipadas con código de error + mensaje apto para UI (nunca stack traces), `OperationResult.Ok/Fail`, JSON camelCase en DTOs.
- TS: componentes funcionales con hooks, `bridge.call<T>` para operaciones, `OperationIds.ai*` desde `types/bridge.ts`, UI con `lucide-react` iconos, estilos inline con variables CSS `--pcs-*`, diálogos de confirmación con `createPortal` (lección 027).
- Ejemplo de operación C# (basado en `WindowsRunCommandOperation`): validar payload → crear job → fire-and-forget → `_jobs.Complete/Fail`.
- Ejemplo de servicio C# (basado en `ClamAvService`): detectar ejecutable, ejecutar proceso con `IProcessRunner`, traducir códigos de salida a mensajes controlados.

## Testing Strategy

- Backend: xUnit tests para `AiService` (detección, instalación, conversión con mock de `IProcessRunner`) y `AiOperations` (mapeo de payloads y errores).
- Frontend: Vitest para la página IA (renderizado, flujo de detectar/instalar/convertir, manejo de errores).
- Coverage: igual que el resto del proyecto (no nuevo umbral exigido).
- No hay tests de integración con Python real (dependería del entorno); se mockea `IProcessRunner`.

## Boundaries

- Always: validar entradas (ruta absoluta existente para convertir), mensajes de error controlados sin stack traces, no ejecutar comandos arbitrarios (sólo `python -m markitdown` con argv fijo), usar UAC por operación si es necesario.
- Ask first: añadir dependencia NuGet nueva, cambiar el patrón de jobs, modificar `MainForm.cs` fuera del registro de operaciones.
- Never: commit secrets, editar vendor directories, remover tests fallando sin aprobación, exponer una línea de comando libre al frontend.

## Success Criteria

- [ ] `ai.detect` devuelve `{installed: boolean, pythonPath?: string, markitdownVersion?: string, detail?: string}`
- [ ] `ai.install` ejecuta `pip install markitdown` y devuelve outcome estructurado (succeeded/exitCode/detail)
- [ ] `ai.convert` acepta `{path}`, valida ruta existente, ejecuta `python -m markitdown <path>`, genera `<archivo>.md` junto al original y devuelve `{succeeded, exitCode, outputPath, detail}`
- [ ] `ollama.detect` devuelve `{installed: boolean, ollamaPath?: string, version?: string, detail?: string}`
- [ ] `ollama.listModels` devuelve `{models: [{name, size?, modified?, digest?}]}`
- [ ] Página IA en sidebar con icono, navegable por hash `#/ai`
- [ ] UI muestra estado de markItDown (instalado/no), botón instalar, selector de archivo, botón convertir, ruta del archivo .md generado, botones "Abrir carpeta" y "Copiar ruta"
- [ ] UI muestra estado de Ollama (instalado/no), versión, botón "Listar modelos", tabla de modelos con nombre/tamaño/fecha
- [ ] Backend compila sin warnings nuevos; frontend pasa `npm run check` y build
- [ ] xUnit y Vitest pasan para el nuevo código

## Open Questions

1. ¿Debe la conversión usar el patrón de JOB (como escaneos ClamAV / comandos Windows) o puede ser una operación directa (respuesta síncrona)? — Recomendado: JOB, porque markitdown puede tardar minutos en archivos grandes.
2. ¿Qué icono usar en el sidebar para "IA"? (candidato: `Cpu` o `Sparkles` de lucide-react; `Sparkles` ya lo usa Limpiador).
3. ¿markitdown requiere dependencias extra (OCR, transcripción) que deban instalarse por separado? → La v1 usa markitdown base; extras quedan como nota en la UI.