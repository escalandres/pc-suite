# CI/CD — Gestión segura de la clave PGP de firma de manifiestos (Task 17.6)

> **Referencia:** Task 17.6, sección "Qué hacer (REPO pc-suite)" → "Clave de firma de releases" y "publicar.ps1 firma el manifiesto"

---

## Principio de seguridad

| Clave | Ubicación | Acceso |
|---|---|---|
| **Privada (`.sec.asc`)** | **Solo en CI/CD** (secreto protegido) | `publicar.ps1` en agente de build |
| **Pública (`.pub.asc`)** | Repo `pc-suite` (embebida) + `aes-studio/public/pcsuite/` | Cualquiera (verificación) |

**La clave privada NUNCA toca el sistema de archivos del repo ni logs.**

---

## Generación inicial (una sola vez)

```powershell
# En máquina de desarrollo SEGURA (offline preferiblemente)
cd pc-suite/src/Tools/GenerateReleaseKey
dotnet run
```

Salida relevante:
```
Fingerprint guardado en: ...\output\fingerprint.txt
Clave privada guardada en: ...\output\releases@aes-studio.dev.sec.asc
Clave pública  guardado en: ...\output\releases@aes-studio.dev.pub.asc
signingKeyFingerprint (para latest.json): 79C6 BC2D 81EF 6F6E 3AE7 9E30 AB3C 1312 2F97 6179
```

**Acciones inmediatas:**
1. ✅ Copiar `releases@aes-studio.dev.pub.asc` → `src/PCSuite.Infrastructure/Keys/ManifestSigningKey.asc` (embebido)
2. ✅ Copiar `releases@aes-studio.dev.pub.asc` → `../aes-studio/public/pcsuite/latest.json.pub`
3. ✅ Guardar `fingerprint.txt` y `releases@aes-studio.dev.sec.asc` en **gestor de secretos** (ver abajo)
4. ❌ **Borrar** la carpeta `output/` y el keyring local (`release-keyring/`)

---

## Almacenamiento del secreto en CI/CD

### GitHub Actions (recomendado)

```yaml
# .github/workflows/publish.yml
name: Publish PCSuite

on:
  workflow_dispatch:
    inputs:
      version:
        description: 'Version to publish (empty = Directory.Build.props)'
        required: false
      skip_sign:
        description: 'Skip PGP signing (testing only)'
        type: boolean
        default: false

env:
  GPG_KEY_ID: releases@aes-studio.dev

jobs:
  publish:
    runs-on: windows-latest
    permissions:
      contents: write   # para gh release create
      id-token: write   # para OIDC si se usa

    steps:
      - name: Checkout pc-suite
        uses: actions/checkout@v4
        with:
          repository: escalandres/pc-suite
          fetch-depth: 0

      - name: Checkout aes-studio (site)
        uses: actions/checkout@v4
        with:
          repository: escalandres/aes-studio
          path: ../aes-studio
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Install GPG
        run: choco install gnupg -y

      - name: Import PGP private key from secret
        env:
          PGP_PRIVATE_KEY: ${{ secrets.PGP_PRIVATE_KEY }}
        run: |
          echo "$env:PGP_PRIVATE_KEY" | gpg --batch --yes --import
          gpg --list-secret-keys --keyid-format=long

      - name: Trust imported key ultimately (needed for --local-user)
        run: |
          $fpr = (gpg --list-secret-keys --with-colons --fingerprint "releases@aes-studio.dev" | Select-String 'fpr::::::::([0-9A-F]{40}):').Matches.Groups[1].Value
          echo "$fpr:6:" | gpg --import-ownertrust

      - name: Run publicar.ps1
        working-directory: ./scripts
        run: |
          .\publicar.ps1 -SkipSign:$false
          # Si se pasa -SkipSign:$true omite la firma (solo testing)

      - name: Copy site/ to aes-studio repo
        run: |
          Copy-Item -Recurse -Force site\* ../aes-studio/public/

      - name: Commit & push aes-studio
        working-directory: ../aes-studio
        run: |
          git config user.name "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git add -A
          git diff --cached --quiet || git commit -m "chore: deploy site for v$Version"
          git push
```

### Configurar el secreto en GitHub

1. Settings → Secrets and variables → Actions → **New repository secret**
2. Name: `PGP_PRIVATE_KEY`
3. Value: **contenido completo** de `releases@aes-studio.dev.sec.asc` (incluyendo `-----BEGIN PGP PRIVATE KEY BLOCK-----` y `-----END PGP PRIVATE KEY BLOCK-----`)
4. ⚠️ **No** guardar el passphrase (la clave se generó sin passphrase para CI automatizado)

### Azure DevOps (alternativa)

```yaml
# azure-pipelines.yml
variables:
- group: pgp-secrets  # Variable group vinculado a Key Vault

steps:
- task: Bash@3
  displayName: 'Import PGP private key'
  env:
    PGP_PRIVATE_KEY: $(PGP_PRIVATE_KEY)
  inputs:
    targetType: inline
    script: |
      echo "$PGP_PRIVATE_KEY" | gpg --batch --yes --import
      fpr=$(gpg --list-secret-keys --with-colons --fingerprint "releases@aes-studio.dev" | grep -oP 'fpr::::::::\K[0-9A-F]{40}')
      echo "$fpr:6:" | gpg --import-ownertrust
```

En **Azure Key Vault** / **Variable Group**:
- Secret name: `PGP_PRIVATE_KEY`
- Value: contenido del `.sec.asc`

---

## Flujo completo de publicación (CI)

```
┌─────────────────────────────────────────────────────────────────┐
│                     AGENTE DE BUILD (Windows)                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. Checkout pc-suite + aes-studio                              │
│  2. Import PGP private key desde SECRETO (env var)              │
│     gpg --import <<< $PGP_PRIVATE_KEY                           │
│     gpg --import-ownertrust (ultimate trust)                    │
│                                                                 │
│  3. publicar.ps1 ejecuta:                                       │
│     a) SHA-256 artefactos (dist/)                               │
│     b) Genera latest.json (con releaseNotesUrl → /notes/)       │
│     c) Genera site/notes/<vers>.html (markdown → HTML)          │
│     d) gpg --detach-sign --local-user releases@aes-studio.dev  │
│        → latest.json.sig                                        │
│     e) gpg --export --armor releases@aes-studio.dev             │
│        → latest.json.pub                                        │
│     f) Extrae fingerprint → actualiza latest.json               │
│     g) Copia .sig + .pub a site/archive/<vers>/                 │
│     h) gh release create (binarios a GitHub Releases)           │
│                                                                 │
│  4. Copia site/* → ../aes-studio/public/                        │
│  5. Commit + push a aes-studio → Vercel deploy automático       │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Verificación post-deploy

### 1. Verificar que la firma existe y es válida

```bash
# En local o CI posterior
curl -sL https://aes-studio.dev/pcsuite/latest.json.sig | head -5
# Debe mostrar: -----BEGIN PGP SIGNATURE-----

curl -sL https://aes-studio.dev/pcsuite/latest.json.pub | head -5
# Debe mostrar: -----BEGIN PGP PUBLIC KEY BLOCK-----
```

### 2. Verificar fingerprint en latest.json

```bash
curl -sL https://aes-studio.dev/pcsuite/latest.json | jq -r .signingKeyFingerprint
# Debe coincidir: "79C6 BC2D 81EF 6F6E 3AE7 9E30 AB3C 1312 2F97 6179"
```

### 3. Verificar notas HTML

```bash
curl -sL https://aes-studio.dev/notes/0.5.1.html | grep -c "PCSuite 0.5.1"
# Debe devolver 1
```

### 4. Test de verificación PGP manual

```bash
# Descargar manifiesto y firma
curl -sLO https://aes-studio.dev/pcsuite/latest.json
curl -sLO https://aes-studio.dev/pcsuite/latest.json.sig
curl -sLO https://aes-studio.dev/pcsuite/latest.json.pub

# Importar clave pública y verificar
gpg --import latest.json.pub
gpg --verify latest.json.sig latest.json
# Debe mostrar: "Good signature from AES Studio <releases@aes-studio.dev>"
```

---

## Rotación de clave (si se compromete)

1. **Generar nueva clave** con `GenerateReleaseKey` (nuevo fingerprint)
2. **Actualizar secreto en CI/CD** (`PGP_PRIVATE_KEY`)
3. **Actualizar recurso embebido** en `pc-suite`:
   - Reemplazar `src/PCSuite.Infrastructure/Keys/ManifestSigningKey.asc`
   - Rebuild + nueva release de la app (los usuarios DEBEN actualizar)
4. **Actualizar sitio** (`aes-studio/public/pcsuite/latest.json.pub`)
5. **Publicar nueva versión** → el updater detectará fingerprint distinto → **TOFU warning** en la UI:
   > *"La clave de firma cambió. Verifica el fingerprint en aes-studio.dev antes de actualizar."*

---

## Checklist de seguridad

- [ ] Clave privada **solo** en secretos de CI/CD (GitHub Actions / Azure Key Vault)
- [ ] Clave privada **nunca** en logs (usar `set +x` / `env:` en steps sensibles)
- [ ] Clave generada **sin passphrase** (para CI desatendido) — compensado con:
  - [ ] Acceso al secreto restringido a workflow `publish.yml`
  - [ ] Workflow solo ejecutable vía `workflow_dispatch` (manual)
  - [ ] Agente `windows-latest` efímero (se destruye tras el job)
- [ ] Clave pública embebida en la app (`ManifestSigningKey.asc` como `EmbeddedResource`)
- [ ] Clave pública publicada en sitio (`latest.json.pub`) para verificación humana
- [ ] Fingerprint en `latest.json` para referencia rápida
- [ ] Tests PGP pasando (`dotnet test --filter PgpService`)

---

## Referencias

- `pc-suite/scripts/publicar.ps1` — funciones `Convert-MarkdownToHtml`, `Sign-Manifest`
- `pc-suite/src/PCSuite.Infrastructure/Updates/ManifestSignatureVerifier.cs` — verificación + TOFU
- `pc-suite/src/PCSuite.Infrastructure/Updates/PcsuiteUpdateService.cs` — integración en CheckAsync/DownloadAsync
- `aes-studio/src/components/pcsuite/PcsuiteLanding.astro` — sección "Verificación adicional"
- `aes-studio/public/pcsuite/latest.json` — contrato público con `signingKeyFingerprint`