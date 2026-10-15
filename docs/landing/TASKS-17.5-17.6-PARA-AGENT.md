# Task 17.5 — Changelog por versión en la página

> **Documento para agente de IA.** Instrucciones para implementar la vista
> de notas de versión en el sitio de AES Studio (Astro/Vercel) y cablear el
> generador de notas al pipeline de publicación de pc-suite.

## Contexto

La landing de PCSuite (`https://aes-studio.dev/pcsuite/`) ya enlaza a las
notas de versión mediante `releaseNotesUrl` del manifiesto (`latest.json`).
Ese campo apunta a URLs como:

```
https://aes-studio.dev/notes/0.5.0.html
```

**Esa URL da 404** porque ninguna página HTML existe ahí. Las notas de
versión viven como Markdown en el repo `pc-suite`:

```
pc-suite/docs/landing/notas/0.5.0.md     ← notas de la 0.5.0
pc-suite/docs/landing/notas/<vers>.md    ← (se crean con cada release)
```

Y el `releaseNotesUrl` del manifiesto apunta al *sitio*, no a GitHub:
el usuario final no debería salir de aes-studio.dev para leer qué cambió.

## Qué hacer (REPO aes-studio)

### 1. Estructura de archivos

Crear `public/notes/` en el repo Astro:

```
public/notes/
├── 0.5.0.html    ← generado a partir del markdown (ver paso 2)
└── <vers>.html   ← una página por versión publicada
```

### 2. Generar el HTML de la nota

El contenido fuente es el Markdown del repo pc-suite
(`docs/landing/notas/0.5.0.md`). Hay dos formas de generarlo:

**Opción A (recomendada): publicar.ps1 lo hace automático**

Extender `scripts/publicar.ps1` en pc-suite para que, además de generar
el manifiesto, convierta el markdown a un HTML mínimo con el mismo
wrapper visual de la landing (tokens `--pcs-*`) y lo escriba en
`site/notes/<versión>.html`. Así el flujo `publicar.bat` lo produce todo
junto y el agente de aes-studio solo copia.

**Opción B: manual la primera vez**

Convertir el markdown a HTML con cualquier conversor (pandoc, marked,
incluso pegar en un editor). El resultado debe ser una página HTML
estándar (sin build de Astro) que se coloca en `public/notes/0.5.0.html`.

### 3. Template HTML mínimo para las notas

```html
<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>PCSuite <versión> — Notas de versión</title>
  <link rel="stylesheet" href="/pcsuite/assets/styles.css">
  <link rel="icon" type="image/svg+xml" href="/pcsuite/assets/logo.svg">
</head>
<body>
  <nav class="nav">
    <a class="nav-brand" href="/pcsuite/">
      <img src="/pcsuite/assets/logo.svg" alt="PCSuite" width="28" height="28">
      <span>PCSuite</span>
    </a>
    <div class="nav-links">
      <a href="/pcsuite/#descarga">Descarga</a>
      <a href="/pcsuite/#actualizar">Actualizar</a>
    </div>
    <span class="nav-studio">AES Studio</span>
  </nav>
  <main class="section">
    <h1>PCSuite <versión></h1>
    <p class="section-sub">Publicado el <fecha></p>
    <!-- CONTENIDO: el markdown convertido a HTML -->
  </main>
  <footer><!-- mismo footer que la landing --></footer>
</body>
</html>
```

Reutiliza `styles.css` de la landing (`/pcsuite/assets/styles.css`) — no
crear CSS nuevo. Los tokens `--pcs-*` ya estilan todo lo necesario.

### 4. Verificación

Tras deployar: `https://aes-studio.dev/notes/0.5.0.html` debe responder
200 y mostrar las notas. El enlace "Notas de versión" en la landing debe
navegar ahí sin 404.

---

## Qué hacer (REPO pc-suite)

Extender `scripts/publicar.ps1` para que en cada versión:

1. Lea `docs/landing/notas/<versión>.md` (si existe)
2. Lo convierta a HTML con el template de arriba
3. Lo escriba en `site/notes/<versión>.html`

Así el flujo `publicar.bat` produce TODO: manifiesto + notas + la landing
nunca se toca.

---

# Task 17.6 — Firma PGP del manifiesto de actualizaciones

> **Documento para agente de IA.** Diseño de la firma criptográfica del
> canal de actualizaciones. La implementación del updater vive en el repo
> `pc-suite` (C#); la distribución de la clave pública vive en el sitio
> `aes-studio` (Astro/Vercel).

## Contexto y motivación

Hoy el manifiesto (`latest.json`) protege **integridad** con SHA-256: lo
que descargas es exactamente lo que se publicó. Pero NO protege
**autenticidad**: un atacante que comprometa el hosting (Vercel, DNS)
puede sustituir tanto el manifiesto como los binarios con sus propios
hashes — el SHA-256 validaría perfectamente contra artefactos maliciosos.

La firma PGP del manifiesto resuelve esto: solo AES Studio con su clave
privada puede producir un manifiesto válido.

**La infraestructura ya existe**: PCSuite tiene un servicio PGP completo
(Task 009: `PgpService` con BouncyCastle en proceso, sin gpg externo).
La firma del manifiesto reutiliza esa infraestructura.

## Arquitectura

```
publicar.bat (pc-suite)
  1. genera latest.json (como hoy)
  2. genera latest.json.sig (firma PGP detached con clave privada)
  3. genera latest.json.pub (clave pública ASCII-armored)
  4. escribe el fingerprint SHA-256 de la clave pública en latest.json
     → campo nuevo "signingKeyFingerprint"
  5. copia latest.json + .sig + .pub a site\

Updater (PcsuiteUpdateService.CheckAsync)
  1. descarga latest.json + latest.json.sig
  2. valida la firma contra la clave pública
  3. si la firma no coincide → Available=false silencioso
  4. TOFU: primera conexión archiva el fingerprint;
     cambio posterior → aviso "¿estás siendo atacado?"
```

## Qué hacer (REPO pc-suite)

### 1. Clave de firma de releases

Generar una clave PGP dedicada (separada de la personal del autor):

```
Algoritmo: RSA 4096 o EdDSA (Curve25519)
Identidad: "AES Studio <releases@aes-studio.dev>"
Propósito: "PCSuite release manifest signing"
```

Se puede generar con el propio `PgpService` de PCSuite (BouncyCastle)
o con gpg CLI. La clave privada se guarda FUERA del repo
(secreto de la estación de build — igual que una clave de firma
Authenticode).

### 2. `publicar.ps1` firma el manifiesto

Tras generar `latest.json`:

```powershell
# Usar BouncyCastle vía un helper C# o gpg CLI:
gpg --detach-sign --armor --local-user releases@aes-studio.dev `
  --output site\pcsuite\latest.json.sig site\pcsuite\latest.json
gpg --export --armor releases@aes-studio.dev > site\pcsuite\latest.json.pub
```

Añadir al JSON un campo informativo:

```json
{
  "signingKeyFingerprint": "AB12 CD34 EF56 ..."
}
```

### 3. `PcsuiteUpdateService` valida la firma

Extender `CheckAsync`:

```csharp
// Antes de confiar en el manifiesto:
// 1. GET latest.json + latest.json.sig (en paralelo)
// 2. Validar firma PGP contra la clave pública embebida en la app
//    (hardcoded como recurso, NO descargada — si el atacante
//    compromete el hosting, no puede sustituir la clave de validación)
// 3. Firma inválida → Available=false (silencioso, como hoy)
```

La clave pública se embebe en `PCSuite.Infrastructure` como
`ManifestSigningKey.asc` (Build Action: Embedded Resource). El
updater NO descarga la clave del sitio (eso sería circular: el
atacante sustituiría también la clave).

### 4. TOFU (Trust On First Use)

Primera ejecución:
- La app no tiene fingerprint archivado → confía y lo guarda en
  `%LOCALAPPDATA%\PCSuite\Data\update-key-fingerprint.txt`

Versiones posteriores:
- El fingerprint del manifiesto firmado coincide con el archivado → OK
- NO coincide → aviso explícito: *"La clave de firma cambió. Verifica
  el fingerprint en aes-studio.dev antes de actualizar."*

### 5. Tests

- Firma válida → check pasa
- Manifiesto alterado (1 byte cambiado) → firma inválida → check rechaza
- Clave sustituida → fingerprint no coincide → aviso TOFU

## Qué hacer (REPO aes-studio)

- Copiar `latest.json.sig` y `latest.json.pub` a `public/pcsuite/`
  (junto al `latest.json`, parte del flujo de publicación)
- Mostrar el fingerprint de la clave pública en la landing (sección
  "Verificación adicional" o pie) para que el humano pueda verificarlo
  manualmente si quiere

## Flujo de publicación completo tras 17.6

```
empaquetar.bat → ISCC → publicar.bat
  ├─ hash de artefactos
  ├─ latest.json + latest.json.sig + latest.json.pub
  ├─ gh release create (binarios a GitHub)
  └─ site\: manifiesto + firma + clave + notas + landing
       → push del repo Astro → Vercel deploy
```

---

## Orden de implementación recomendado

1. **17.5 primero** (S): cierra el 404 de las notas, hace el canal
   público completo desde el punto de vista del usuario. Solo
   afecta al publicador y al sitio.

2. **17.6 después** (M): añade autenticidad criptográfica al canal.
   Requiere generar la clave, extender el publicador, extender el
   updater C# y añadir tests. Puede esperar a que el ciclo base
   (17.1–17.5) esté validado con la 0.5.1 en real.

## Referencias

- Propuesta original: `pc-suite/docs/landing/PCSuite_Landing_Propuesta.md` (§5: seguridad, §8: updater)
- Infraestructura PGP existente: `pc-suite/src/PCSuite.Infrastructure/PGP/`
- Notas de versión: `pc-suite/docs/landing/notas/<versión>.md`
- Publicador: `pc-suite/scripts/publicar.ps1`
- Updater: `pc-suite/src/PCSuite.Infrastructure/Updates/PcsuiteUpdateService.cs`
