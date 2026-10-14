# Cómo llevar site\ al repo Astro (Vercel)

`publicar.bat` genera todo en `site\`. La copia al repo del sitio es manual
y el mapeo es EXACTO — la landing, sus assets y el manifiesto van JUNTOS
dentro de `public\pcsuite\`:

```
DE (pc-suite, generado)              →  A (repo Astro, public/)
─────────────────────────────────────────────────────────────
site\index.html                      →  public\pcsuite\index.html
site\assets\logo.svg                 →  public\pcsuite\assets\logo.svg
site\assets\styles.css               →  public\pcsuite\assets\styles.css
site\assets\main.js                  →  public\pcsuite\assets\main.js
site\pcsuite\latest.json             →  public\pcsuite\latest.json
site\archive\<versión>\manifest.json →  public\pcsuite\archive\<versión>\manifest.json
```

Quedando:

```
public\pcsuite\
├── index.html            ← la landing (se sirve en /pcsuite/)
├── assets\               ← logo + estilos + JS
├── latest.json           ← el manifiesto del updater (NUNCA se mueve de URL)
└── archive\              ← historial inmutable por versión
```

Después: commit + push → Vercel despliega solo.

- Resultado público: `https://aes-studio.dev/pcsuite/`
- Manifiesto del updater: `https://aes-studio.dev/pcsuite/latest.json`
- Los enlaces de descarga dentro de la landing apuntan directamente a
  GitHub Releases (github.com/escalandres/pc-suite) — Vercel solo sirve KBs.

## Nada de site\ va a la raíz de public\

La raíz de tu Astro es tu homepage del estudio. PCSuite vive bajo
`public\pcsuite\` con namespace propio — aunque mañana haya más apps, este
path no cambia jamás (las apps instaladas pinnean la URL del manifiesto).

## Probar localmente ANTES de desplegar

No abrir `site\index.html` con doble clic: el `fetch` no funciona vía
`file://` (verías "No se pudo cargar la información de versión"). Servir
el sitio:

```
node scripts\serve-site.mjs          (desde la raíz de pc-suite)
→ http://localhost:8123/             (landing en la raíz para pruebas)
→ http://localhost:8123/pcsuite/latest.json  (manifiesto)
```
