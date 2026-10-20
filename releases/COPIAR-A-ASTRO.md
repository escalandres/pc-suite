# Cómo llevar releases\<versión>\ al repo Astro (aes-studio.dev)

> **ACTUALIZADO 2026-10-20 (Task 022)** — la salida de `publicar.bat` ya NO se
> dispersa en `site\pcsuite\` + `site\archive\<v>\` + `site\notes\`: TODO lo
> que hay que subir a aes-studio.dev vive en UNA carpeta autocontenida por
> release en la raíz del proyecto, con los NOMBRES EXACTOS de sus destinos
> desplegados (copia sin renombres). Los nombres de archivo son parte del
> contrato: `latest.json`/`.sig`/`.pub`/`manifest.json`/`<versión>.html`.

> La landing sigue viviendo como páginas Astro en el repo aes-studio con i18n:
> - `https://aes-studio.dev/pcsuite/` → inglés (idioma por defecto)
> - `https://aes-studio.dev/pcsuite/es/` → español
>
> Fuentes en aes-studio: `src/pages/pcsuite/` (+ `es/`), textos en
> `src/i18n/pcsuite.ts`, estilos en `src/styles/pcsuite.css`, layout en
> `src/layouts/PcsuiteLayout.astro`, pintado en `src/components/pcsuite/`.

## Qué genera publicar.bat (Task 022)

```
releases\0.7.0\
├── latest.json        ← manifiesto live del updater (bytes firmados)
├── latest.json.sig    ← firma PGP sobre ESOS bytes exactos
├── latest.json.pub    ← clave pública de firma (Ed25519 releases@aes-studio.dev)
├── manifest.json      ← copia del historial inmutable (mismos bytes que latest.json)
└── 0.7.0.html         ← notas de versión (desde docs\landing\notas\0.7.0.md)
```

La carpeta de cada versión queda congelada al publicar (regla de oro abajo).

## Qué se copia en cada publicación

`publicar.bat` genera la carpeta `releases\<versión>\`. La copia al repo
Astro es MANUAL — este es el mapa exacto para la versión `<v>` recién
publicada:

```
DE (pc-suite, releases\<v>\)                →  A (repo Astro, public/)
──────────────────────────────────────────────────────────────────────
releases\<v>\latest.json                    →  public\pcsuite\latest.json            (sobrescribir: NUEVO live)
releases\<v>\latest.json.sig   ⚠ CRÍTICO   →  public\pcsuite\latest.json.sig        (sobrescribir)
releases\<v>\latest.json.pub                →  public\pcsuite\latest.json.pub        (sobrescribir)
releases\<v>\manifest.json                  →  public\pcsuite\archive\<v>\manifest.json     (carpeta nueva, inmutable)
releases\<v>\latest.json.sig   (otra vez)   →  public\pcsuite\archive\<v>\latest.json.sig   (la misma firma, segundo destino)
releases\<v>\latest.json.pub   (otra vez)   →  public\pcsuite\archive\<v>\latest.json.pub   (la misma clave, segundo destino)
releases\<v>\<v>.html                      →  public\notes\<v>.html                  (debe casar con releaseNotesUrl)
site\assets\logo.svg  (solo si cambió)      →  public\pcsuite\assets\logo.svg
```

Después: commit + push (a `dev`; producción se despliega desde `master`).

Las carpetas de versiones ANTERIORES (`releases\0.5.0\`, `releases\0.5.1\`,
`releases\0.6.0\`…) ya están desplegadas en el repo Astro: no se re-copian
salvo para reparar su archive (verificación/recuperación — para eso viajan
versionadas en este repo).

## ⚠ Obligatorio desde 0.5.2

- **`latest.json.sig`**: los updaters con firma (Task 17.6) rechazan EN
  SILENCIO un manifiesto sin `.sig` — la app se quedaría "actualizada para
  siempre" sin ver nada. Si copias un archivo, copia el otro.
- **`notes\<v>.html`** debe casar EXACTO con el `releaseNotesUrl` del
  manifiesto: `https://aes-studio.dev/notes/<v>.html` (la raíz del sitio —
  así las copia también el workflow de CI, que mapea
  `releases\<v>\` → `public\` archivo por archivo).

## Regla de oro: latest.json es INMUTABLE tras publicar

La firma PGP es un contrato sobre los BYTES EXACTOS de `latest.json`
desplegado. Si el JSON mutara después de firmarse, todos los updaters
instalados la rechazarían ("manifiesto alterado") y las actualizaciones no
aparecerían nunca.

**No edites NUNCA `releases\<v>\latest.json` a mano.** Si hay que re-publicar
una versión: borra la carpeta `releases\<v>\` COMPLETA, re-ejecuta
`publicar.bat` (regenera manifiesto + firma + notas desde cero) y vuelve a
copiar al repo Astro (live + archive de esa versión).

## Verificación post-deploy (2 minutos)

```
curl -s https://aes-studio.dev/pcsuite/latest.json | head -5
curl -s -o NUL -w "%{http_code}" https://aes-studio.dev/pcsuite/latest.json.sig   → 200
curl -s -o NUL -w "%{http_code}" https://aes-studio.dev/notes/<v>.html            → 200
```

## Qué NUNCA se copia

- `site\index.html` → entraría en conflicto con las páginas Astro
  (`dist/pcsuite/index.html` se genera en el build). Es la copia estática
  ENGLISH solo para pruebas locales con `serve-site.mjs`.
- `site\assets\styles.css` y `site\assets\main.js` → en Astro viven en
  `src/styles/pcsuite.css` y el `<script>` de `PcsuiteLanding.astro`.
- Los binarios (`dist\…exe|.zip`) → viven en GitHub Releases, jamás en
  Vercel (el deploy del sitio queda en KBs).

Si cambias textos de la landing, cámbialos en `src/i18n/pcsuite.ts`
(ambos idiomas) y refleja la versión EN en `site\index.html` para que el
espejo local siga igual.

## Contrato permanente (no cambiar jamás)

- Manifiesto del updater: `https://aes-studio.dev/pcsuite/latest.json`
- Historial: `https://aes-studio.dev/pcsuite/archive/<versión>/manifest.json`
- Las apps instaladas pinnean esa URL: moverla rompe la actualización.

## Probar localmente ANTES de desplegar

No abrir `site\index.html` con doble clic: el `fetch` no funciona vía
`file://` (verías "Could not load version information"). Servir el sitio:

```
node scripts\serve-site.mjs          (desde la raíz de pc-suite)
→ http://localhost:8123/                       (espejo EN de /pcsuite/)
→ http://localhost:8123/pcsuite/latest.json   (manifiesto: serve-site.mjs
                                               lo mapea a releases\<máxima>\)
→ http://localhost:8123/notes/0.6.0.html       (notas, idem)
```

Para probar el Astro completo (EN + ES): en aes-studio,
`pnpm astro build && pnpm astro preview` → `http://localhost:4321/pcsuite/`
y `/pcsuite/es/`.

## Enlaces de descarga

La landing apunta directo a GitHub Releases
(github.com/escalandres/pc-suite) — Vercel solo sirve KBs de HTML/JSON.
