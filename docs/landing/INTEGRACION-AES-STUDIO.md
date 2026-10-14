# Integración del sitio de PCSuite al proyecto aes-studio

> **Documento para agente de IA.** Instrucciones exactas y autocontenidas para integrar la landing y el manifiesto de actualizaciones de **PCSuite** (proyecto `pc-suite`) dentro del proyecto **aes-studio** (Astro, deploy en Vercel vía GitHub).
>
> **Contexto del contrato (no renegociable):** la app PCSuite instalada consulta `https://aes-studio.dev/pcsuite/latest.json` para buscar actualizaciones y valida el SHA-256 de cada artefacto contra ese manifiesto. La ruta `/pcsuite/latest.json` **es un contrato permanente**: nunca se mueve, nunca se renombra. Los binarios NO se alojan aquí — viven en GitHub Releases del repo `escalandres/pc-suite`; este sitio solo sirve la landing (KBs) y el manifiesto (KBs).

---

## 1. Fuente: qué copiar y de dónde

El proyecto **pc-suite** (repo local: `C:\Users\andre\Documentos\Git_projects\pc-suite`) genera todo en su carpeta `site\` mediante `publicar.bat`. Contenido de `site\`:

```
site\
├── index.html              ← landing de PCSuite (HTML estático)
├── COPIAR-A-ASTRO.md       ← (solo documentación, no se copia)
├── assets\
│   ├── logo.svg            ← logo PCSuite
│   ├── styles.css          ← estilos (tokens --pcs-*, tema oscuro)
│   └── main.js             ← JS que pinta la página desde latest.json
├── pcsuite\
│   └── latest.json         ← 🔒 MANIFIESTO del updater (contrato permanente)
└── archive\
    └── <versión>\manifest.json  ← historial inmutable por versión
```

## 2. Destino: mapeo exacto de archivos

Todo va **dentro de `public/pcsuite/`** en el repo aes-studio — nunca en la raíz de `public/` (ahí vive la homepage del estudio, `src/pages/index.astro`, que NO se toca).

```
DE (pc-suite\site\)                        →  A (aes-studio\public\pcsuite\)
──────────────────────────────────────────────────────────────────────
site\index.html                            →  public\pcsuite\index.html
site\assets\logo.svg                       →  public\pcsuite\assets\logo.svg
site\assets\styles.css                     →  public\pcsuite\assets\styles.css
site\assets\main.js                        →  public\pcsuite\assets\main.js
site\pcsuite\latest.json                   →  public\pcsuite\latest.json
site\archive\<versión>\manifest.json      →  public\pcsuite\archive\<versión>\manifest.json
```

Estructura resultante en aes-studio:

```
public\pcsuite\
├── index.html
├── assets\
│   ├── logo.svg
│   ├── styles.css
│   └── main.js
├── latest.json
└── archive\
    └── 0.5.0\manifest.json
```

**Astro sirve `public/` tal cual** — no hay que registrar nada en `src/`, ni crear componentes, ni tocar el router. Los archivos estáticos de `public/` se publican en la raíz del dominio: `public\pcsuite\index.html` → `https://aes-studio.dev/pcsuite/`.

## 3. Pasos del agente

1. **Verificar la fuente**: confirmar que `site\pcsuite\latest.json` existe en el repo pc-suite y que `site\index.html` está presente. Si falta algo: correr `publicar.bat` en pc-suite (requiere `dist\` compilado — ver `empaquetar.bat` + `instalador.iss`).

2. **Crear el destino** si no existe: `public\pcsuite\` y `public\pcsuite\assets\`, `public\pcsuite\archive\`.

3. **Copiar** los 6 archivos del mapeo (sección 2). NO copiar `COPIAR-A-ASTRO.md` ni nada más de `site\`.

4. **Probar local** antes de commitear:
   ```bash
   # desde la raíz de aes-studio
   astro dev
   # abrir http://localhost:4321/pcsuite/
   ```
   Verificación: la página carga con estilos (tema oscuro), el chip de versión muestra la versión del manifiesto (ej. "1.2.4"), y la sección Descargas muestra dos tarjetas con SHA-256. Si en su lugar aparece *"No se pudo cargar la información de versión"*: el `latest.json` no está en `public\pcsuite\` o se copió a otra ruta.

5. **Commitear** (siguiendo las convenciones del repo aes-studio):
   ```bash
   git add public/pcsuite/
   git commit -m "feat: añade sitio de PCSuite (landing + manifiesto de actualizaciones)"
   git push origin main
   ```

6. **Vercel despliega solo** (deploy automático desde GitHub). Verificar en producción tras ~1 minuto:
   - `https://aes-studio.dev/pcsuite/` → landing con versión y descargas
   - `https://aes-studio.dev/pcsuite/latest.json` → el manifiesto JSON
   - Los botones de descarga deben apuntar a `github.com/escalandres/pc-suite/releases/...`

## 4. Para PUBLICAR una nueva versión de PCSuite (flujo repetible)

El agente de pc-suite corre `publicar.bat`, que **regenera** `site\pcsuite\latest.json` + `site\archive\<nueva-versión>\manifest.json`. Entonces en aes-studio:

1. Copiar SOLO los archivos que cambian: `site\pcsuite\latest.json` → `public\pcsuite\latest.json` (sobreescribe el anterior — el manifiesto siempre apunta a la última) y `site\archive\<nueva>\manifest.json` → `public\pcsuite\archive\<nueva>\manifest.json` (nueva carpeta).
2. La landing (`index.html`, `assets\`) NO cambia entre versiones — se pinta sola desde `latest.json`.
3. Commit + push.

**Inmutabilidad**: nunca editar ni borrar un `archive\<versión>\manifest.json` existente. Si `publicar.bat` aborta con *"la versión ya está publicada"*: la versión ya existe en el historial y no debe regenerarse — subir el número de versión en pc-suite.

## 5. Errores comunes (evitar)

| Error | Causa | Solución |
|---|---|---|
| *"No se pudo cargar la información de versión"* en la landing | `latest.json` no está en `public\pcsuite\`, o la landing se abrió vía `file://` | Verificar la ruta exacta; probar con `astro dev`, jamás con doble clic en el HTML |
| `/pcsuite/` da 404 en producción | La carpeta quedó en la raíz de `public\` en vez de dentro de `public\pcsuite\`, o el push no se hizo | Verificar mapeo sección 2 |
| La homepage del estudio desaparece | Alguien copió `index.html` de PCSuite a `public\index.html` o `src\pages\` | NO tocar `src\pages\index.astro` ni `public\index.html`; la landing vive SOLO en `public\pcsuite\` |
| El manifiesto apunta a URLs muertas | Se editó a mano el JSON | Nunca editar `latest.json` — siempre regenerarlo con `publicar.bat` en pc-suite |

## 6. Qué NO hay que hacer aquí

- **No alojar binarios** en este repo (los `.exe`/`.zip` viven en GitHub Releases de `escalandres/pc-suite` — este sitio solo sirve KBs).
- **No crear componentes Astro** para la landing: es HTML estático intencionalmente (se actualiza solo leyendo `latest.json`, sin rebuild del sitio).
- **No cambiar la ruta** `/pcsuite/…`: es el contrato que la app instalada tiene pinneado. Reestructurar = romper las actualizaciones de todos los PCSuite instalados.
- **No añadir `pcsuite/` a `astro.config.mjs`**: `public/` no requiere configuración.

## 7. Referencias cruzadas

- Contrato del manifiesto y flujo de publicación completa: `pc-suite\docs\landing\PCSuite_Landing_Propuesta.md` (§4 manifiesto, §7 publicar.bat)
- Mapa de copia en el repo fuente: `pc-suite\site\COPIAR-A-ASTRO.md`
- Task 017 en el plan maestro: `pc-suite\plans\PLAN.md`
