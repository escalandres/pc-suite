# Proceso de Release — PCSuite

> **Referencia:** Task 17.5 (notas HTML) + Task 17.6 (firma PGP)  
> **Workflow:** `.github/workflows/publish.yml` (manual, `workflow_dispatch`)

---

## Resumen rápido

```
1. Local: empaquetar.bat + ISCC + escribir notas.md + push a main
2. GitHub Actions: "Publish PCSuite" → Run workflow (skip_sign: false)
3. Automático: manifiesto + notas HTML + firma PGP + GitHub Release + deploy Vercel
4. Verificar: curl latest.json / notes / .sig / .pub
```

---

## Checklist previo (local)

- [ ] **Versión** actualizada en `Directory.Build.props` → `<Version>X.Y.Z</Version>`
- [ ] **Artefactos** generados:
  ```powershell
  .\empaquetar.bat                    # dist/PCSuite-X.Y.Z-win-x64.zip
  ISCC instalador.iss                 # dist/instalador/PCSuite-Setup-X.Y.Z-win64.exe
  ```
- [ ] **Notas de versión** escritas en `docs/landing/notas/X.Y.Z.md` (formato markdown estándar)
- [ ] **Commit + push** a `main` (pc-suite repo)
- [ ] **Secret** `PGP_PRIVATE_KEY` configurado en GitHub (solo primera vez)

---

## Ejecutar release (GitHub Actions)

1. Abrir: **GitHub → pc-suite repo → Actions → "Publish PCSuite"**
2. Click **"Run workflow"** (rama `main`)
3. Configurar inputs:
   | Input | Valor | Notas |
   |---|---|---|
   | `version` | *(vacío)* | Lee de `Directory.Build.props` automáticamente |
   | `skip_sign` | `false` | **Producción = false**; `true` solo para testing sin firma PGP |
4. Click **"Run workflow"** → esperar ~3-5 min

---

## Qué hace el workflow (automático)

| Paso | Acción | Output |
|---|---|---|
| 1 | Checkout pc-suite + aes-studio | - |
| 2 | Setup .NET 10, GPG, gh CLI | - |
| 3 | Import PGP key desde `secrets.PGP_PRIVATE_KEY` | Clave lista para firmar |
| 4 | Validar artefactos en `dist/` | Fail fast si faltan |
| 5 | `publicar.ps1 -Version X.Y.Z -SkipSign:$false` | Ver abajo |
| 6 | Copia `site/*` → `../aes-studio/public/` | - |
| 7 | Commit + push a `aes-studio` | Trigger Vercel deploy |
| 8 | Log resumen con URLs finales | Ver abajo |

### Detalle de `publicar.ps1` (Paso 5)

```
├─ SHA-256 instalador + portable
├─ Genera site/pcsuite/latest.json
│   ├─ releaseNotesUrl → https://aes-studio.dev/notes/X.Y.Z.html
│   ├─ signingKeyFingerprint (inyectado tras firmar)
│   └─ artifacts[setup-win64, portable-win64] con URLs GitHub Releases
├─ Genera site/notes/X.Y.Z.html (markdown → HTML + estilos --pcs-*)
├─ gpg --detach-sign --armor --local-user releases@aes-studio.dev
│   → site/pcsuite/latest.json.sig
├─ gpg --export --armor releases@aes-studio.dev
│   → site/pcsuite/latest.json.pub
├─ Copia .sig + .pub → site/archive/X.Y.Z/
└─ gh release create pcsuite-vX.Y.Z (sube ambos binarios)
```

---

## URLs resultantes (públicas)

| Qué | URL |
|---|---|
| **Manifiesto updater** | `https://aes-studio.dev/pcsuite/latest.json` |
| **Notas de versión** | `https://aes-studio.dev/notes/X.Y.Z.html` |
| **Firma PGP (detached)** | `https://aes-studio.dev/pcsuite/latest.json.sig` |
| **Clave pública** | `https://aes-studio.dev/pcsuite/latest.json.pub` |
| **Binarios (GitHub Releases)** | `https://github.com/escalandres/pc-suite/releases/tag/pcsuite-vX.Y.Z` |
| **Historial inmutable** | `https://aes-studio.dev/pcsuite/archive/` |

---

## Verificación post-release

```bash
# 1. Versión y fingerprint en manifiesto
curl -sL https://aes-studio.dev/pcsuite/latest.json | jq -r '.version, .signingKeyFingerprint'
# Debe mostrar: X.Y.Z  y  79C6 BC2D 81EF 6F6E 3AE7 9E30 AB3C 1312 2F97 6179

# 2. Firma y clave pública existen
curl -sfL -o /dev/null https://aes-studio.dev/pcsuite/latest.json.sig && echo "✓ .sig OK"
curl -sfL -o /dev/null https://aes-studio.dev/pcsuite/latest.json.pub && echo "✓ .pub OK"

# 3. Notas HTML accesibles
curl -sfL https://aes-studio.dev/notes/X.Y.Z.html | grep -q "PCSuite X.Y.Z" && echo "✓ Notes OK"

# 4. Verificación PGP manual (opcional)
curl -sLO https://aes-studio.dev/pcsuite/latest.json
curl -sLO https://aes-studio.dev/pcsuite/latest.json.sig
curl -sLO https://aes-studio.dev/pcsuite/latest.json.pub
gpg --import latest.json.pub
gpg --verify latest.json.sig latest.json
# "Good signature from AES Studio <releases@aes-studio.dev>"
```

---

## Troubleshooting común

| Error | Causa | Solución |
|---|---|---|
| `Falta dist/instalador/...` | No corrió ISCC | Ejecutar `ISCC instalador.iss` antes |
| `gh release create failed` | Tag ya existe / token | Verificar permisos `contents:write`; borrar tag si es re-release |
| `gpg: signing failed` | Clave no importada / passphrase | Verificar secret `PGP_PRIVATE_KEY`; clave debe ser sin passphrase |
| `Vercel deploy failed` | Build error Astro | Ver logs en Vercel dashboard; suele ser tipo TS sin `@types/node` |
| `Workflow stuck` | Timeout 30 min | Revisar step "Run publicar.ps1" en logs; artefactos muy grandes |

---

## Rotación de clave PGP (emergencia)

```powershell
# 1. Generar nueva clave (máquina segura)
cd src/Tools/GenerateReleaseKey && dotnet run

# 2. Actualizar secret en GitHub
gh secret set PGP_PRIVATE_KEY --body "$(cat releases@aes-studio.dev.sec.asc)" --repo escalandres/pc-suite

# 3. Actualizar recurso embebido en app (pc-suite)
cp releases@aes-studio.dev.pub.asc src/PCSuite.Infrastructure/Keys/ManifestSigningKey.asc
# Rebuild app → nueva release obligatoria (usuarios DEBEN actualizar)

# 4. Actualizar clave pública en sitio
cp releases@aes-studio.dev.pub.asc ../aes-studio/public/pcsuite/latest.json.pub

# 5. Publicar nueva versión
# → Updater detecta fingerprint distinto → TOFU warning en UI:
#    "La clave de firma cambió. Verifica el fingerprint en aes-studio.dev antes de actualizar."
```

---

## Seguridad — Recordatorios

- ✅ **Clave privada** SOLO en `GitHub Secrets → PGP_PRIVATE_KEY` (nunca en repo, ni logs, ni FS)
- ✅ **Workflow manual** (`workflow_dispatch`) — control total, sin triggers accidentales
- ✅ **Clave pública embebida** en app (`EmbeddedResource`) — updater NO la descarga
- ✅ **TOFU automático** — cambio de fingerprint = warning explícito al usuario
- ✅ **Artefactos inmutables** — `site/archive/X.Y.Z/` nunca se sobrescribe

---

## Referencias cruzadas

| Archivo | Qué hace |
|---|---|
| `.github/workflows/publish.yml` | Workflow CI/CD completo |
| `scripts/publicar.ps1` | Genera manifiesto, notas, firma, release |
| `src/Tools/GenerateReleaseKey/` | Tool para generar clave Ed25519 |
| `src/PCSuite.Infrastructure/Updates/ManifestSignatureVerifier.cs` | Verificación PGP + TOFU |
| `src/PCSuite.Infrastructure/Updates/PcsuiteUpdateService.cs` | Integración en CheckAsync/DownloadAsync |
| `docs/landing/CI_CD_PGP_KEY_MANAGEMENT.md` | Detalle gestión claves en CI/CD |
| `aes-studio/public/notes/X.Y.Z.html` | Notas HTML servidas estáticamente por Vercel |

---

## Contacto / Escalación

- **Repo principal:** `escalandres/pc-suite`
- **Sitio:** `escalandres/aes-studio` (Vercel)
- **Issues release:** GitHub Issues en pc-suite con label `release`
- **Clave PGP:** `releases@aes-studio.dev` (fingerprint: `79C6 BC2D 81EF 6F6E 3AE7 9E30 AB3C 1312 2F97 6179`)