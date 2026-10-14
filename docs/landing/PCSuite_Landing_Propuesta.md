# PCSuite — Landing Page y Gestión de Actualizaciones (Propuesta)

> Task 017. Documento de diseño para la página pública de descarga y el
> sistema de actualizaciones de PCSuite, incluida la validación de
> integridad SHA-256 de punta a punta.

- **Estado**: propuesta (pendiente de aprobación)
- **Autor**: sesión de desarrollo 2026-10-13
- **Precedentes**: `empaquetar.bat` (artefactos), `instalador.iss`
  (auto-actualización con desinstalación previa), Task 009 (infraestructura
  PGP en proceso), Task 016 (jobs con progreso + eventos al bridge).

---

## 1. Resumen y objetivos

Construir una presencia web mínima que cumpla tres funciones con UNA
sola página estática:

1. **Descubrir y descargar PCSuite** (instalador y portable).
2. **Servir el manifiesto de actualizaciones** que la app consulta.
3. **Publicar los SHA-256** de cada artefacto para verificación manual
   y como fuente de verdad del updater.

Objetivos no negociables:

- **Sin backend**: hosting estático (GitHub Pages / Cloudflare Pages /
  Netlify). Cero servidores que mantener, cero superficie de ataque.
- **La integridad se verifica dos veces**: el updater de la app valida
  SHA-256 antes de ejecutar nada, y el humano puede validarlo a mano
  desde la página.
- **El flujo de actualización reutiliza el instalador existente**: el
  Setup de Inno YA detecta la versión anterior y la desinstala en
  silencio antes de instalar. El updater de la app se reduce a
  "descargar, verificar, ejecutar" — no se reimplementa instalación.
- **Los datos de usuario jamás se tocan**: viven en
  `%LOCALAPPDATA%\PCSuite`, fuera de `{app}`; el ciclo de
  actualización no los mira.

## 2. Alcance

### Dentro

- Página única (`index.html`) + página de changelog por versión.
- Manifiesto `latest.json` (+ archivo histórico por versión).
- `publicar.bat`: genera manifiesto con SHA-256 y copia artefactos al
  sitio.
- Updater en la app: `update.check` / `update.download` / `update.install`
  con progreso por el patrón Job existente.
- Validación SHA-256 en streaming durante la descarga.

### Fuera (documentado, no bloquea)

- Firma Authenticode de los ejecutables (requiere certificado de
  pago; cuando exista, el manifiesto solo añade un campo).
- Canal beta / actualizaciones delta.
- Estadísticas de descarga (el hosting ya da logs si se quieren).
- Auto-update silencioso total: SIEMPRE hay confirmación humana antes
  de ejecutar el instalador (principio de consentimiento explícito).

## 3. Arquitectura

```
        (1) publica                   (2) descubre/descarga
┌──────────────┐   HTTP PUT    ┌─────────────┐    GET latest.json
│  publicar.bat│──────────────▶│  Hosting    │◀──────────────┐ │
│  (SHA-256 +  │   (manual o   │  estático   │               │ │
│   manifiesto)│    CI si hay) │  HTTPS      │               │ │
└──────────────┘               └─────────────┘               │ │
       ▲                              │                       │ │
       │ lee artefactos               │ (3) GET *.exe          │ │
┌──────┴───────┐                      ▼                       │ │
│ dist\        │             ┌──────────────┐                │ │
│  instalador\ │             │   PCSuite    │  (4) verifica  │ │
│  *.zip       │◀────────────│   (la app)   │      SHA-256   │ │
└──────────────┘  ejecuta    └──────────────┘                │ │
                  el Setup              │                     │ │
                  (auto-uninstall)      └──── (5) GET newer ──┘ │
                                        └───────────────────────┘
```

1. `publicar.bat` parte del `dist\` de `empaquetar.bat` + ISCC, computa
   SHA-256, escribe `latest.json` y copia todo a la carpeta del sitio
   (publicación manual: copiar/`git push`).
2. La página lee `latest.json` al cargar para pintar versión actual,
   enlaces y checksums (cero HTML regenerado por release).
3. La app consulta el manifiesto al arrancar (no bloqueante) y con el
   botón "Buscar actualizaciones".
4. Descarga el Setup a `%TEMP%` computando SHA-256 en streaming —
   mismo patrón que Local Share: el hash sale gratis con la descarga.
5. Coincide → confirmación → se ejecuta el Setup descargado → la app
   se cierra → el Setup desinstala la vieja e instala la nueva.

## 4. El manifiesto (`latest.json`)

```json
{
  "product": "PCSuite",
  "channel": "stable",
  "version": "1.2.4",
  "releasedAt": "2026-10-13T18:00:00Z",
  "releaseNotesUrl": "https://pcsuite.example/notes/1.2.4.html",
  "artifacts": [
    {
      "id": "setup-win64",
      "kind": "installer",
      "label": "Instalador (recomendado)",
      "url": "https://pcsuite.example/download/PCSuite-Setup-1.2.4-win64.exe",
      "size": 40631548,
      "sha256": "ab3f…(hex minúscula, 64)"
    },
    {
      "id": "portable-win64",
      "kind": "zip",
      "label": "Portable (.zip)",
      "url": "https://pcsuite.example/download/PCSuite-1.2.4-win-x64.zip",
      "size": 132000000,
      "sha256": "…"
    }
  ]
}
```

Reglas:

- La **fuente única de la versión** sigue siendo `<Version>` en
  `Directory.Build.props` — `publicar.bat` la lee igual que
  `empaquetar.bat`.
- El comparador de versiones es SemVer estricto (`1.2.10` > `1.2.9`;
  jamás lexicográfico). Campo `minimumRequiredVersion` reservado por si
  un día hay cambio de formato de datos.
- El manifiesto es **inmutable por versión**: cada release escribe
  `latest.json` Y `archive/<versión>/manifest.json`; el histórico
  nunca se sobrescribe.
- URLs absolutas con HTTPS (el manifiesto se puede servir desde el
  mismo dominio; los artefactos pueden vivir en otro CDN si algún día
  hace falta).

## 5. Validación SHA-256 (el corazón de la propuesta)

### Dónde se computa

`publicar.bat` — con `Get-FileHash` de PowerShell sobre los artefactos
YA compilados. Nunca a mano: el hash del artefacto publicado es el
hash del archivo exacto que se va a descargar.

### Dónde se verifica

**Camino automático (updater de la app):**

1. `HttpClient` con `ResponseHeadersRead` → bucle de 64 KB idéntico al
   de Local Share: cada bloque se escribe a
   `%TEMP%\PCSuite-Setup-<ver>.exe` y alimenta el `IncrementalHash`
   SHA-256.
2. Al terminar: hash computado vs `sha256` del manifiesto →
   **cualquier discrepancia aborta, borra el archivo y reporta** con
   mensaje claro ("descarga corrupta o manipulada; vuelve a
   intentarlo").
3. Solo con hash válido se ofrece el botón "Instalar ahora".

**Camino manual (humano):** la landing muestra el SHA-256 junto a
cada botón de descarga con botón de copiar; instrucciones de
verificación (`Get-FileHash`) al lado.

### Qué cubre y qué no (decirlo explícito)

- SHA-256 protege la **integridad**: un archivo corrupto o alterado en
  tránsito jamás se ejecuta.
- NO prueba **autenticidad** (quién lo firmó). Eso lo da:
  - a corto plazo: la **firma PGP del manifiesto** (Fase 3) usando la
    infraestructura BouncyCastle que la app YA tiene (Task 009); el
    updater valida la firma con la clave del autor (TOFU: la primera
    conexión la archiva; cambios de clave saltan aviso).
  - a largo plazo: Authenticode de los ejecutables (cuando haya
    certificado) — el manifiesto solo ganaría un campo
    `authenticodeThumbprint`.

## 6. La landing page

Una sola página, estática, diseño oscuro reutilizando los tokens de la
app (`--pcs-*`) para coherencia de marca:

| Sección | Contenido |
|---|---|
| **Hero** | Logo, tagline *"The Swiss Army Knife for Windows developers and power users"*, botón principal de descarga con versión + tamaño ("PCSuite 1.2.4 · 39 MB") |
| **Descargas** | Instalador (recomendado) y portable .zip; cada uno con su **SHA-256 visible** + copiar + instrucción de verificación |
| **Características** | Grid de los módulos reales: Antivirus, Software (WinGet), WSL, SSH, PGP, Limpieza, Seguridad, **Local Share**, Plugins, Tools |
| **Cómo actualizar** | Dos caminos: dentro de la app ("Buscar actualizaciones") y manual (descargar + ejecutar; "el instalador reemplaza la versión anterior solo") |
| **Changelog** | Enlace por versión (`/notes/<version>.html`), generado por `publicar.bat` desde un `CHANGELOG.md` |
| **Pie** | Requisitos (Windows 10/11 x64, WebView2), aviso "sin telemetría — la app no llama a casa salvo para buscar actualizaciones" |

Tecnología: HTML+CSS+JS planos (una página, cero build). El JS solo
hace `fetch('latest.json')` para pintar versión/checksums — la página
no se toca entre releases, se actualiza sola.

## 7. `publicar.bat` (pipeline de publicación)

```
publicar.bat
  1. assert: dist\PCSuite-win-x64 existe (empaquetar.bat ya corrió)
  2. assert: dist\instalador\PCSuite-Setup-<ver>-win64.exe existe
  3. leer <Version> de Directory.Build.props
  4. SHA-256 de ambos artefactos (Get-FileHash)
  5. generar site\latest.json y site\archive\<ver>\manifest.json
  6. copiar artefactos a site\download\ (los de la versión NUEVA;
     opcional --keep N para conservar los últimos N históricos)
  7. resumen: versión, tamaños, hashes → pantalla
  8. (manual) el usuario sube site\ al hosting
```

Con validaciones: si la versión ya existe en `archive\`, aborta
("ya publicaste 1.2.4; sube la versión"). El manifiesto jamás se
escribe dos veces para la misma versión.

## 8. Updater dentro de la app

### Operaciones del bridge (siguiendo convenciones existentes)

| Operación | Payload | Resultado / notas |
|---|---|---|
| `update.check` | cero | `{ available, currentVersion, latestVersion, sizeBytes, sha256, releaseNotesUrl }` — compara contra `latest.json`. La URL del manifiesto es constante en `Contracts` (con override en settings para pruebas) |
| `update.download` | cero | **Job** (patrón existente): descarga a `%TEMP%` con SHA-256 en streaming; responde `{ jobId }` al instante; progreso vía `job.event`; verifica hash al final; fallo de hash → job Failed con mensaje específico |
| `update.install` | `{ path }` | Valida que el archivo esté en `%TEMP%` con hash ya verificado → confirmación final en la UI → `ProcessRunner` lanza el Setup (`runas` normal: el propio Inno pide UAC si toca) → la app **se cierra** |

### UX de actualización

1. **Check al arrancar** (no bloqueante, ~1 request GET con timeout de
   5 s; opcional en settings, por defecto ON). Si hay novedad: badge
   discreto "Actualización disponible" en la UI — jamás un modal en la
   cara al abrir.
2. Página/panel "Acerca de": versión actual + botón **"Buscar
   actualizaciones"** + "Descargar e instalar" cuando exista.
3. Descarga con progreso (reutiliza la barra de jobs) → SHA-256
   verificado → **"Instalar ahora"** (única acción que ejecuta algo).
4. Al instalar: mensaje "PCSuite se cerrará para actualizar" → la app
   sale → el Setup (con su lógica actual) desinstala la previa e
   instala la nueva → el usuario reabre.
5. Sin conexión o sin novedad: la app funciona exactamente igual
   (el check NUNCA bloquea el arranque).

### Seguridad del updater

- Descarga solo sobre HTTPS (HttpClient rechaza TLS inválido).
- SHA-256 obligatorio antes de ofrecer "Instalar".
- El archivo descargado vive en `%TEMP%` del usuario; jamás en `{app}`
  ni con permisos especiales.
- `update.install` no acepta rutas arbitrarias: solo un archivo que
  el propio servicio descargó y verificó en esta sesión (se guarda el
  estado interno `verifiedPath`; cualquier otra ruta → rechazo).

## 9. Fases de implementación

| Fase | Contenido | Estimación |
|---|---|---|
| **17.0** | Esta propuesta + decisiones (hosting, dominio, auto-check ON/OFF) | — |
| **17.1** | `publicar.bat` con SHA-256 + manifiestos + validaciones | S |
| **17.2** | Landing estática (hero, descargas, checksums, features) | S–M |
| **17.3** | Updater: `update.check` + panel "Acerca de" + badge | M |
| **17.4** | `update.download` (Job + streaming SHA-256) + `update.install` + cierre de la app | M |
| **17.5** | Changelog por versión en la página (desde `CHANGELOG.md`) | S |
| **17.6** (opcional) | Firma PGP del manifiesto + validación en el updater (reusa Task 009) | M |

## 10. Criterios de aceptación

- [ ] Descargar el instalador desde la landing y verificar el SHA-256
      a mano (`Get-FileHash`) coincide con lo publicado.
- [ ] `publicar.bat` contra la misma versión dos veces → aborta.
- [ ] La app con 1.2.4 contra un manifiesto con 1.2.5 → badge +
      panel; descarga → hash OK → instalar → Setup reemplaza la
      versión → datos de usuario (settings, dispositivos emparejados,
      certificado Local Share) intactos.
- [ ] Manifiesto de prueba con SHA-256 ALTERADO → la descarga
      completa pero "Instalar" NUNCA se ofrece; mensaje claro; el
      archivo se borra.
- [ ] Sin red: la app arranca igual de rápido y no muestra error.
- [ ] La landing muestra los checksums sin intervención manual entre
      releases (se pinta de `latest.json`).

## 11. Decisiones (cerradas el 2026-10-13/14 por el autor)

1. **Hosting y dominio**: ✅ **CERRADA** — dominio del estudio
   (`aes-studio.dev`) con **URLs por producto**:
   `aes-studio.dev/pcsuite/latest.json` y `/pcsuite/download/…`.
   La landing de HOY es 100% PCSuite ("AES Studio" solo en el pie);
   una futura segunda app añade un nav — cero migraciones, cero
   manifiestos rotos. El porqué: las apps instaladas pinnean la URL
   del manifiesto; URLs planas quedarían rotas para siempre ante
   cualquier reestructuración.
2. **Auto-check al arrancar**: ON por defecto (reversible en settings)
   — es un GET de unos KB con timeout de 5 s que jamás bloquea.
3. **`CHANGELOG.md`**: formal desde 1.2.4 hacia adelante (sin
   retroactivo).
4. **Fase 17.6 (PGP)**: diferida — primero el ciclo base end-to-end;
   la firma del manifiesto entra cuando la base esté validada.

### Implementación 17.1 (ajustes menores de la propuesta original)

`publicar.bat` es un wrapper fino de un solo comando; la lógica vive
en `scripts/publicar.ps1` (PowerShell real — el caret-continuation de
batch para PS multi-línea resultó frágil). Extras implementados más
allá de lo propuesto:

- El JSON se valida (`ConvertFrom-Json`) ANTES de escribirse a disco.
- Re-hash de los artefactos YA copiados a `site\` — lo publicado es
  exactamente lo del manifiesto o aborta.
- Verificación práctica de la inmutabilidad: re-publicar 1.2.4 aborta
  con el mensaje de "sube la versión".
