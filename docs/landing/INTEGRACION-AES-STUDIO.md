# Integración del sitio de PCSuite al proyecto aes-studio

> **Documento para agente de IA.** Instrucciones exactas y autocontenidas para integrar la landing y el manifiesto de actualizaciones de **PCSuite** (proyecto `pc-suite`) dentro del proyecto **aes-studio** (Astro, deploy en Vercel vía GitHub).
>
> **Contexto del contrato (no renegociable):** la app PCSuite instalada consulta `https://aes-studio.dev/pcsuite/latest.json` para buscar actualizaciones y valida el SHA-256 de cada artefacto contra ese manifiesto. La ruta `/pcsuite/latest.json` **es un contrato permanente**: nunca se mueve, nunca se renombra. Los binarios NO se alojan aquí — viven en GitHub Releases del repo `escalandres/pc-suite`; este sitio solo sirve la landing (KBs) y el manifiesto (KBs).

> **⚠ ACTUALIZADO 2026-10-20 (Task 022):** los artefactos de release de
> pc-suite ya NO viven en `site\pcsuite\` + `site\archive\` + `site\notes\`
> sino en la carpeta autocontenida `releases\<versión>\` (raíz del proyecto),
> con los nombres exactos de sus destinos desplegados. Además, desde el
> 2026-10-14 la landing YA NO se copia como HTML estático: vive como páginas
> Astro (`src/pages/pcsuite/` con i18n) — `site\index.html` es solo el
> espejo local de pruebas y NUNCA se copia. El mapa de copia VIGENTE y
> autoritativo es `pc-suite\releases\COPIAR-A-ASTRO.md`.

---

## 1. Fuente: qué copiar y de dónde

El proyecto **pc-suite** (repo local: `C:\Users\andre\Documentos\Git_projects\pc-suite`) genera los artefactos de cada release en carpetas autocontenidas `releases\<versión>\` mediante `publicar.bat` (Task 022, 2026-10-20):

```
releases\
├── COPIAR-A-ASTRO.md              ← mapa de copia vigente (documentación, no se copia)
├── 0.5.0\manifest.json            ← historial inmutable por versión
├── 0.5.1\manifest.json
└── 0.6.0\
    ├── latest.json                ← 🔒 MANIFIESTO live del updater (contrato permanente)
    ├── latest.json.sig            ← firma PGP (obligatoria desde 0.5.2)
    ├── latest.json.pub            ← clave pública de firma
    ├── manifest.json              ← copia del historial (mismos bytes que latest.json)
    └── 0.6.0.html                 ← notas de versión

site\                              ← espejo estático EN SOLO para pruebas locales
├── index.html                     (NUNCA se copia: la landing son páginas Astro)
└── assets\{logo.svg, styles.css, main.js}
```

## 2. Destino: mapeo exacto de archivos

Todo va **dentro de `public/pcsuite/`** en el repo aes-studio — nunca en la raíz de `public/` (ahí vive la homepage del estudio, `src/pages/index.astro`, que NO se toca).

```
DE (pc-suite\releases\<versión>\)            →  A (aes-studio\public\)
──────────────────────────────────────────────────────────────────────
releases\<v>\latest.json                     →  public\pcsuite\latest.json            (sobrescribir: NUEVO live)
releases\<v>\latest.json.sig    ⚠ CRÍTICO    →  public\pcsuite\latest.json.sig
releases\<v>\latest.json.pub                →  public\pcsuite\latest.json.pub
releases\<v>\manifest.json                  →  public\pcsuite\archive\<v>\manifest.json   (carpeta nueva, inmutable)
releases\<v>\latest.json.sig   (2º destino)  →  public\pcsuite\archive\<v>\latest.json.sig
releases\<v>\latest.json.pub   (2º destino)  →  public\pcsuite\archive\<v>\latest.json.pub
releases\<v>\<v>.html                       →  public\notes\<v>.html                 (URL pineada por releaseNotesUrl)
site\assets\logo.svg  (solo si cambió)      →  public\pcsuite\assets\logo.svg
```

Estructura resultante en aes-studio (la landing y sus estilos viven como
páginas Astro en `src/pages/pcsuite/` + `src/styles/pcsuite.css` — no como
archivos copiados a `public\`):

```
public\pcsuite\
├── latest.json
├── latest.json.sig
├── latest.json.pub
├── assets\
│   └── logo.svg
└── archive\
    ├── 0.5.0\manifest.json
    └── 0.6.0\{manifest.json, latest.json.sig, latest.json.pub}
public\notes\
└── 0.6.0.html
```

**Astro sirve `public/` tal cual** — no hay que registrar nada en `src/`, ni crear componentes, ni tocar el router para estos estáticos. La landing en sí (`https://aes-studio.dev/pcsuite/`) la generan las páginas Astro del repo (`src/pages/pcsuite/`, textos en `src/i18n/pcsuite.ts`).

## 3. Pasos del agente

1. **Verificar la fuente**: confirmar que `releases\<versión>\latest.json` existe en el repo pc-suite (la carpeta autocontenida de la versión publicada). Si falta: correr `publicar.bat` en pc-suite (requiere `dist\` compilado — ver `empaquetar.bat` + `instalador.iss`).

2. **Crear el destino** si no existe: `public\pcsuite\`, `public\pcsuite\assets\`, `public\pcsuite\archive\` y `public\notes\`.

3. **Copiar** los archivos del mapeo (sección 2). NO copiar `releases\COPIAR-A-ASTRO.md` ni nada más de lo mapeado.

4. **Probar local** antes de commitear:
   ```bash
   # desde la raíz de aes-studio
   astro dev
   # abrir http://localhost:4321/pcsuite/
   ```
   Verificación: la página carga con estilos (tema oscuro), el chip de versión muestra la versión del manifiesto (ej. "0.6.0"), y la sección Descargas muestra dos tarjetas con SHA-256. Si en su lugar aparece *"No se pudo cargar la información de versión"*: el `latest.json` no está en `public\pcsuite\` o se copió a otra ruta.

5. **Commitear** (siguiendo las convenciones del repo aes-studio):
   ```bash
   git add public/pcsuite/ public/notes/
   git commit -m "feat: añade sitio de PCSuite (landing + manifiesto de actualizaciones)"
   git push origin main
   ```

6. **Vercel despliega solo** (deploy automático desde GitHub). Verificar en producción tras ~1 minuto:
   - `https://aes-studio.dev/pcsuite/` → landing con versión y descargas
   - `https://aes-studio.dev/pcsuite/latest.json` → el manifiesto JSON
   - `https://aes-studio.dev/pcsuite/latest.json.sig` → la firma PGP (200)
   - Los botones de descarga deben apuntar a `github.com/escalandres/pc-suite/releases/...`

## 4. Para PUBLICAR una nueva versión de PCSuite (flujo repetible)

El agente de pc-suite corre `publicar.bat`, que **regenera** la carpeta autocontenida `releases\<nueva-versión>\` (latest.json + manifest.json + .sig + .pub + notas HTML, todo firmado sobre los bytes finales). Entonces en aes-studio:

1. Copiar SOLO los archivos que cambian según el mapa de `releases\COPIAR-A-ASTRO.md`: `releases\<nueva>\latest.json{,.sig,.pub}` → `public\pcsuite\` (sobrescribe el live anterior — el manifiesto siempre apunta a la última) y `manifest.json` + `.sig` + `.pub` → `public\pcsuite\archive\<nueva>\` (carpeta nueva), `<nueva>.html` → `public\notes\`.
2. La landing (páginas Astro de `src/pages/pcsuite/`) NO cambia entre versiones — se pinta sola desde `latest.json`.
3. Commit + push.

**Inmutabilidad**: nunca editar ni borrar un `archive\<versión>\manifest.json` existente ni el `releases\<versión>\latest.json` firmado. Si `publicar.bat` aborta con *"la versión ya está publicada"*: la versión ya existe en el historial y no debe regenerarse — subir el número de versión en pc-suite (`Directory.Build.props`).

## 5. Errores comunes (evitar)

| Error | Causa | Solución |
|---|---|---|
| *"No se pudo cargar la información de versión"* en la landing | `latest.json` no está en `public\pcsuite\`, o la landing se abrió vía `file://` | Verificar la ruta exacta; probar con `astro dev`, jamás con doble clic en el HTML |
| `/pcsuite/` da 404 en producción | La carpeta quedó en la raíz de `public\` en vez de dentro de `public\pcsuite\`, o el push no se hizo | Verificar mapeo sección 2 |
| La homepage del estudio desaparece | Alguien copió `index.html` de PCSuite a `public\index.html` o `src\pages\` | NO tocar `src\pages\index.astro` ni `public\index.html`; la landing vive SOLO en `public\pcsuite\` |
| El manifiesto apunta a URLs muertas | Se editó a mano el JSON | Nunca editar `latest.json` — siempre regenerarlo con `publicar.bat` en pc-suite (la firma PGP es un contrato sobre los bytes exactos) |
| Updaters "no ven" la actualización jamás | Se copió el `latest.json` SIN su `latest.json.sig` | Copiar SIEMPRE el `.sig` junto al `latest.json` (obligatorio desde 0.5.2) |

## 6. Qué NO hay que hacer aquí

- **No alojar binarios** en este repo (los `.exe`/`.zip` viven en GitHub Releases de `escalandres/pc-suite` — este sitio solo sirve KBs).
- **No crear componentes Astro** para la landing: es HTML estático intencionalmente (se actualiza solo leyendo `latest.json`, sin rebuild del sitio).
- **No cambiar la ruta** `/pcsuite/…`: es el contrato que la app instalada tiene pinneado. Reestructurar = romper las actualizaciones de todos los PCSuite instalados.
- **No añadir `pcsuite/` a `astro.config.mjs`**: `public/` no requiere configuración.

## 7. Referencias cruzadas

- Contrato del manifiesto y flujo de publicación completa: `pc-suite\docs\landing\PCSuite_Landing_Propuesta.md` (§4 manifiesto, §7 publicar.bat) — propuesta histórica
- Proceso de release vigente: `pc-suite\docs\landing\RELEASE_PROCESS.md`
- **Mapa de copia en el repo fuente (autoritativo): `pc-suite\releases\COPIAR-A-ASTRO.md`**
- Task 017 y Task 022 en el plan maestro: `pc-suite\plans\PLAN.md`
