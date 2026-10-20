# Proceso de Release — PCSuite

> **Referencia:** Task 17.5 (notas HTML) + Task 17.6 (firma PGP) + Task 19.6 (SignRelease)
> **Local:** `publicar.bat` (todo en uno: manifiesto + notas + firma + verificación)

---

## Resumen rápido

```
1. Local:   bumpea <Version> en Directory.Build.props
            empaquetar.bat + ISCC instalador.iss      (dist/)
            escribe docs\landing\notas\<vers>.md
2. Local:   .\publicar.bat
            → SHA-256 de los binarios EXACTOS
            → releases\<vers>\ (carpeta autocontenida e inmutable, Task 022)
            → notas HTML en releases\<vers>\<vers>.html
            → inyecta signingKeyFingerprint ANTES de firmar
            → firma con SignRelease (PgpService en proceso — SIN gpg)
              → latest.json.sig + latest.json.pub
            → --verify: la firma se valida contra la clave EMBEBIDA del
              updater; si no cuadra, ABORTA (jamás se publica un
              manifiesto que ningún updater aceptaría)
3. GitHub:  release pcsuite-v<vers> (web UI) con ESOS binarios exactos
4. Vercel:  copia al repo Astro (ver releases\COPIAR-A-ASTRO.md):
              latest.json + latest.json.sig + .pub + archive\ + notes\
            → push a dev → merge a master → deploy
5. Verificar (curl): latest.json / .sig (200) / notes (200)
```

---

## Firma: qué hace publicar.bat exactamente

El pipeline usa **SignRelease** (`src\Tools\SignRelease`) — el mismo stack
BouncyCastle/PgpService que la app, **sin gpg CLI**:

- `--fingerprint-only` → el fingerprint de la clave de releases para
  INYECTARLO en el JSON **antes** de firmar (la firma cubre el contenido
  FINAL exacto; si el JSON mutara después, todos los updaters la
  rechazarían — bug corregido 2026-10-19).
- Firma detached armoured → `latest.json.sig` + exporta `latest.json.pub`.
- `--verify` → sanity check contra la clave pública **embebida** en el
  binario (`ManifestSigningKey.asc`, Task 17.6): la misma que validará
  cada updater instalado. Si pasa aquí, pasa allí.

**La clave de firmas** es `releases@aes-studio.dev` (Ed25519), fingerprint
`79C6 BC2D 81EF 6F6E 3AE7 9E30 AB3C 1312 2F97 6179`. Su mitad privada vive
en el keyring de la app (`%APPDATA%\PCSuite\pgp`) — NUNCA en el repo. El
respaldo (`.sec.asc`) está en `GenerateReleaseKey\bin\...\output\`: guárdalo
fuera de la máquina; perderla dispara el aviso TOFU en todos los updaters
instalados.

> `-UseGpg` queda como **legacy**: firma con gpg CLI (requiere la clave
> privada importada en el keyring de gpg). El CI (`publish.yml`) aún usa
> esta vía con el secret `PGP_PRIVATE_KEY` — produce firmas válidas porque
> es la MISMA clave.

## Checklist previo (local)

- [ ] **Versión** actualizada en `Directory.Build.props` → `<Version>X.Y.Z</Version>`
- [ ] **Artefactos** generados:
  ```powershell
  .\empaquetar.bat                    # dist/PCSuite-X.Y.Z-win-x64.zip
  ISCC instalador.iss                 # dist/instalador/PCSuite-Setup-X.Y.Z-win64.exe
  ```
- [ ] **Notas** escritas: `docs\landing\notas\X.Y.Z.md` (el pipeline las
      convierte a HTML; sin el .md, el release sale sin notas y el
      `releaseNotesUrl` da 404)
- [ ] **Suite verde** en tu rama (la suite completa, 0 warnings)

## Ejecución

```powershell
.\publicar.bat          # firma + verifica + imprime los hashes
```

- **Inmutabilidad**: si la versión ya existe en `releases\<vers>\`, ABORTA.
- Si `gh` CLI está autenticado crea el release solo; si no (lo normal),
  imprime el comando exacto para la web UI.
- **Sube a GitHub los binarios EXACTOS que acabas de firmar**: si
  recompilas algo entre medias, los hashes del manifiesto no coincidirán
  con los del release y el updater rechazará la descarga.

## Despliegue del sitio

`releases\COPIAR-A-ASTRO.md` es el mapa exacto (incluye `.sig` — obligatorio
desde 0.5.2). Verificación:

```
curl -s https://aes-studio.dev/pcsuite/latest.json | head -5
curl -s -o NUL -w "%{http_code}" https://aes-studio.dev/pcsuite/latest.json.sig   → 200
curl -s -o NUL -w "%{http_code}" https://aes-studio.dev/pcsuite/notes/X.Y.Z.html  → 200
```

## Desenlaces del updater (referencia)

| Situación | Comportamiento del updater |
|---|---|
| Manifiesto firmado, versión mayor | Badge → panel → descarga con SHA-256 → "Instalar ahora" |
| `.sig` ausente o inválida | Check **silencioso** (la app funciona igual); descarga imposible |
| Clave de firma ROTADA | Banner ámbar "Clave de firma cambiada" + descarga bloqueada hasta verificación manual |
| Hash del binario ≠ manifiesto | Descarga falla, archivo temporal BORRADO, nunca se ofrece instalar |
