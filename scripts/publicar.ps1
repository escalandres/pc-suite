# ============================================================================
# publicar.ps1 (Task 17.1, adaptado a GitHub Releases) — genera el manifiesto
# del updater y publica los artefactos como GitHub Release.
#
# Flujo (propuesta Task 17, decisión 2026-10-14):
#   1. Artefactos ya compilados (empaquetar.bat + ISCC) de la versión actual.
#   2. SHA-256 del archivo EXACTO que se sube (fuente de verdad del updater).
#   3. Manifiestos con URLs de GitHub Releases (los binarios viven ahí,
#      no en el sitio: el deploy de Vercel queda en KBs).
#   4. Si gh CLI está autenticado: crea el release con ambos assets.
#      Si no: imprime el comando exacto para copiar.
#
# Inmutabilidad: si la versión ya está en site\archive, aborta. El historial
# jamás se reescribe — las apps instaladas pinnean la URL del manifiesto.
# ============================================================================
param(
    [string]$Product = "pcsuite",
    [string]$Version = "",
    [string]$ReleaseRepo = "escalandres/pc-suite",
    [string]$TagPrefix = "pcsuite-v"
)

$ErrorActionPreference = "Stop"
Set-Location -Path (Join-Path $PSScriptRoot "..")

# Version: unica fuente = Directory.Build.props (mismo truco que empaquetar.bat)
if (-not $Version) {
    $line = Select-String -Path "Directory.Build.props" -Pattern "<Version>(.*?)</Version>" |
        Select-Object -First 1
    $Version = $line.Matches[0].Groups[1].Value
    if (-not $Version) { throw "No se pudo leer <Version> de Directory.Build.props" }
}

if ($Product -ne "pcsuite") {
    throw "Producto '$Product' no soportado todavia. Solo 'pcsuite'."
}

$tag = "$TagPrefix$Version"

Write-Host "============================================"
Write-Host " Publicar $Product v$Version"
Write-Host " Releases : github.com/$ReleaseRepo (tag $tag)"
Write-Host "============================================"

# ---------- 1. Artefactos de esta version ----------
$setupRel = "dist\instalador\PCSuite-Setup-$Version-win64.exe"
$zipRel   = "dist\PCSuite-$Version-win-x64.zip"
$setupName = "PCSuite-Setup-$Version-win64.exe"
$zipName   = "PCSuite-$Version-win-x64.zip"

if (-not (Test-Path $setupRel)) {
    throw "Falta $setupRel. Ejecuta antes: empaquetar.bat y luego ISCC instalador.iss"
}
if (-not (Test-Path $zipRel)) {
    throw "Falta $zipRel. Ejecuta antes: empaquetar.bat"
}

# ---------- 2. Inmutabilidad ----------
$archiveDir = "site\archive\$Version"
if (Test-Path (Join-Path $archiveDir "manifest.json")) {
    throw @"
La version $Version ya esta publicada (site\archive\$Version\manifest.json).
El historial es inmutable: sube la version en Directory.Build.props
para publicar una nueva.
"@
}

# ---------- 3. SHA-256 del archivo exacto que se sube ----------
Write-Host ""
Write-Host "[1/3] Computando SHA-256 de los artefactos..."

$setupHash = (Get-FileHash -Algorithm SHA256 $setupRel).Hash.ToLowerInvariant()
$zipHash   = (Get-FileHash -Algorithm SHA256 $zipRel).Hash.ToLowerInvariant()
$setupSize = (Get-Item $setupRel).Length
$zipSize   = (Get-Item $zipRel).Length
$relDate   = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")

Write-Host "        Instalador: $setupHash"
Write-Host "        Portable  : $zipHash"

# ---------- 4. Manifiestos con URLs de GitHub Releases ----------
Write-Host ""
Write-Host "[2/3] Generando manifiestos (URLs de GitHub Releases)..."

New-Item -ItemType Directory -Force -Path "site\pcsuite" | Out-Null
New-Item -ItemType Directory -Force -Path $archiveDir | Out-Null

$ghBase = "https://github.com/$ReleaseRepo/releases/download/$tag"

# Plantilla determinista: el orden y formato del JSON son parte del
# contrato con el updater. Los binarios NO se copian al sitio: viven
# en el release de GitHub (el deploy de Vercel queda en KBs).
$json = @"
{
  "product": "$Product",
  "channel": "stable",
  "version": "$Version",
  "releasedAt": "$relDate",
  "releaseNotesUrl": "https://github.com/$ReleaseRepo/releases/tag/$tag",
  "artifacts": [
    {
      "id": "setup-win64",
      "kind": "installer",
      "label": "Instalador (recomendado)",
      "url": "$ghBase/$setupName",
      "size": $setupSize,
      "sha256": "$setupHash"
    },
    {
      "id": "portable-win64",
      "kind": "zip",
      "label": "Portable (.zip)",
      "url": "$ghBase/$zipName",
      "size": $zipSize,
      "sha256": "$zipHash"
    }
  ]
}
"@

# Validar el JSON ANTES de escribirlo a disco.
$null = $json | ConvertFrom-Json

[IO.File]::WriteAllText(
    (Join-Path (Get-Location) "site\pcsuite\latest.json"),
    $json,
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    (Join-Path (Get-Location) "$archiveDir\manifest.json"),
    $json,
    [Text.UTF8Encoding]::new($false))

# ---------- 5. Release en GitHub (gh) ----------
Write-Host ""
Write-Host "[3/3] Publicando el release en GitHub..."

$ghAvailable = $false
try { $null = & gh --version 2>$null; $ghAvailable = ($LASTEXITCODE -eq 0) } catch { }

if ($ghAvailable) {
    # ¿Ya existe el release? (idempotencia a nivel GitHub)
    $existing = $false
    try {
        $null = & gh release view $tag --repo $ReleaseRepo 2>$null
        $existing = ($LASTEXITCODE -eq 0)
    } catch { }

    if ($existing) {
        Write-Host "  El release $tag ya existe en GitHub: solo se regenero el manifiesto."
    }
    else {
        $notesFile = "docs\landing\notas\$Version.md"
        $notesArgs = @()
        if (Test-Path $notesFile) { $notesArgs = @("--notes-file", $notesFile) }

        Write-Host "  gh release create $tag --repo $ReleaseRepo (subiendo ambos assets)..."
        & gh release create $tag --repo $ReleaseRepo `
            --title "PCSuite $Version" `
            @notesArgs `
            $setupRel $zipRel
        if ($LASTEXITCODE -ne 0) {
            throw "gh release create fallo. Revisa el mensaje de gh arriba."
        }
        Write-Host "  Release creado."
    }
}
else {
    Write-Host "  (gh CLI no disponible: crea el release con este comando)"
    Write-Host ""
    Write-Host "  gh release create $tag --repo $ReleaseRepo ``"
    Write-Host "    --title ""PCSuite $Version"" ``"
    if (Test-Path "docs\landing\notas\$Version.md") {
        Write-Host "    --notes-file docs\landing\notas\$Version.md ``"
    }
    Write-Host "    $setupRel $zipRel"
}

# ---------- Resumen ----------
$mb = { param($x) [Math]::Round($x / 1MB) }

Write-Host ""
Write-Host "============================================"
Write-Host " $Product v$Version publicado"
Write-Host "============================================"
Write-Host ("  Manifiesto  : site\pcsuite\latest.json")
Write-Host ("  Historial   : $archiveDir\manifest.json")
Write-Host ("  Release     : https://github.com/$ReleaseRepo/releases/tag/$tag")
Write-Host ("  Instalador  : $setupName  (~" + $mb.Invoke($setupSize) + " MB)")
Write-Host ("                 sha256: $setupHash")
Write-Host ("  Portable    : $zipName  (~" + $mb.Invoke($zipSize) + " MB)")
Write-Host ("                 sha256: $zipHash")
Write-Host ""
Write-Host " Siguiente paso (manual): subir site\ al repo del sitio (Vercel)."
Write-Host "============================================"
