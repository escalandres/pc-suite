# Cómo llevar site\ al repo Astro (Vercel)

> **ACTUALIZADO 2026-10-14** — la landing ya NO se copia como HTML estático.
> Vive como páginas Astro en el repo aes-studio, con i18n:
> - `https://aes-studio.dev/pcsuite/` → inglés (idioma por defecto)
> - `https://aes-studio.dev/pcsuite/es/` → español
>
> Fuentes en aes-studio: `src/pages/pcsuite/` (+ `es/`), textos en
> `src/i18n/pcsuite.ts`, estilos en `src/styles/pcsuite.css`, layout en
> `src/layouts/PcsuiteLayout.astro`, pintado en `src/components/pcsuite/`.

## Qué se copia en cada publicación (solo datos)

`publicar.bat` genera todo en `site\`. La copia al repo del sitio es manual:

```
DE (pc-suite, generado)                      →  A (repo Astro, public/)
────────────────────────────────────────────────────────────
site\pcsuite\latest.json                     →  public\pcsuite\latest.json          (sobrescribir)
site\pcsuite\latest.json.sig  ⚠ CRÍTICO      →  public\pcsuite\latest.json.sig
site\pcsuite\latest.json.pub                 →  public\pcsuite\latest.json.pub
site\archive\<versión>\  (todo el contenido)→  public\pcsuite\archive\<versión>\   (carpeta nueva, inmutable)
site\notes\<versión>.html                    →  public\notes\<versión>.html
site\assets\logo.svg  (solo si cambió)      →  public\pcsuite\assets\logo.svg
```

Después: commit + push (a `dev`; producción se despliega desde `master`).

## ⚠ Obligatorio desde 0.5.2

- **`latest.json.sig`**: los updaters con firma (Task 17.6) rechazan EN
  SILENCIO un manifiesto sin `.sig` — la app se quedaría "actualizada para
  siempre" sin ver nada. Si copias un archivo, copia el otro.
- **`notes\<versión>.html`** debe casar EXACTO con el `releaseNotesUrl` del
  manifiesto: `https://aes-studio.dev/notes/<versión>.html` (la raíz del
  sitio — así las copia también el workflow de CI, que vuelca `site\*` en
  `public\`).

## Verificación post-deploy (2 minutos)

```
curl -s https://aes-studio.dev/pcsuite/latest.json | head -5
curl -s -o NUL -w "%{http_code}" https://aes-studio.dev/pcsuite/latest.json.sig    → 200
curl -s -o NUL -w "%{http_code}" https://aes-studio.dev/notes/0.6.0.html            → 200
```

## Qué NUNCA se copia

- `site\index.html` → entraría en conflicto con las páginas Astro
  (`dist/pcsuite/index.html` se genera en el build). Es la copia estática
  ENGLISH solo para pruebas locales con `serve-site.mjs`.
- `site\assets\styles.css` y `site\assets\main.js` → en Astro viven en
  `src/styles/pcsuite.css` y el `<script>` de `PcsuiteLanding.astro`.

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
→ http://localhost:8123/             (espejo EN de /pcsuite/)
→ http://localhost:8123/pcsuite/latest.json  (manifiesto)
```

Para probar el Astro completo (EN + ES): en aes-studio,
`pnpm astro build && pnpm astro preview` → `http://localhost:4321/pcsuite/`
y `/pcsuite/es/`.

## Enlaces de descarga

La landing apunta directo a GitHub Releases
(github.com/escalandres/pc-suite) — Vercel solo sirve KBs de HTML/JSON.
