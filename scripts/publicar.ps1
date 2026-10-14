# ============================================================================
# publicar.ps1 (Task 17.1) — genera el sitio de AES Studio para una versión
# de PCSuite: manifiesto del updater (latest.json), historial inmutable por
# versión y copia de artefactos con su SHA-256.
#
# Lo invoca publicar.bat; se puede llamar directo:
#   powershell -File scripts\publicar.ps1 -Version 1.2.4
#
# Inmutabilidad: si la versión ya está en site\archive, aborta. El historial
# jamás se reescribe — las apps instaladas pinnean la URL del manifiesto.
# ============================================================================
param(
    [string]$Product = "pcsuite",
    [string]$BaseUrl = "https://aes-studio.dev",
    [string]$Version = ""
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

Write-Host "============================================"
Write-Host " Publicar $Product v$Version en site\"
Write-Host " (dominio: $BaseUrl)"
Write-Host "============================================"

# ---------- 1. Artefactos de esta version ----------
$setupRel = "dist\instalador\PCSuite-Setup-$Version-win64.exe"
$zipRel   = "dist\PCSuite-$Version-win-x64.zip"

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

# ---------- 3. SHA-256 y tamanos del archivo EXACTO que se publica ----------
Write-Host ""
Write-Host "[1/3] Computando SHA-256 de los artefactos..."

$setupHash = (Get-FileHash -Algorithm SHA256 $setupRel).Hash.ToLowerInvariant()
$zipHash   = (Get-FileHash -Algorithm SHA256 $zipRel).Hash.ToLowerInvariant()
$setupSize = (Get-Item $setupRel).Length
$zipSize   = (Get-Item $zipRel).Length
$relDate   = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")

Write-Host "        Instalador: $setupHash"
Write-Host "        Portable  : $zipHash"

# ---------- 4. Manifiestos + copia de artefactos ----------
Write-Host ""
Write-Host "[2/3] Generando manifiestos y copiando artefactos..."

New-Item -ItemType Directory -Force -Path "site\pcsuite\download" | Out-Null
New-Item -ItemType Directory -Force -Path $archiveDir | Out-Null

# Plantilla determinista: el orden y formato del JSON son parte del
# contrato con el updater (los campos que lee son fijos).
$json = @"
{
  "product": "$Product",
  "channel": "stable",
  "version": "$Version",
  "releasedAt": "$relDate",
  "releaseNotesUrl": "$BaseUrl/notes/$Version.html",
  "artifacts": [
    {
      "id": "setup-win64",
      "kind": "installer",
      "label": "Instalador (recomendado)",
      "url": "$BaseUrl/$Product/download/PCSuite-Setup-$Version-win64.exe",
      "size": $setupSize,
      "sha256": "$setupHash"
    },
    {
      "id": "portable-win64",
      "kind": "zip",
      "label": "Portable (.zip)",
      "url": "$BaseUrl/$Product/download/PCSuite-$Version-win-x64.zip",
      "size": $zipSize,
      "sha256": "$zipHash"
    }
  ]
}
"@

# Validar que el JSON generado es parseable ANTES de escribirlo a disco.
$null = $json | ConvertFrom-Json

[IO.File]::WriteAllText(
    (Join-Path (Get-Location) "site\pcsuite\latest.json"),
    $json,
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    (Join-Path (Get-Location) "$archiveDir\manifest.json"),
    $json,
    [Text.UTF8Encoding]::new($false))

Copy-Item $setupRel "site\pcsuite\download\PCSuite-Setup-$Version-win64.exe" -Force
Copy-Item $zipRel   "site\pcsuite\download\PCSuite-$Version-win-x64.zip" -Force

# ---------- 5. Verificacion + resumen ----------
Write-Host ""
Write-Host "[3/3] Verificando lo publicado..."

foreach ($f in @("site\pcsuite\latest.json", "$archiveDir\manifest.json",
                 "site\pcsuite\download\PCSuite-Setup-$Version-win64.exe",
                 "site\pcsuite\download\PCSuite-$Version-win-x64.zip")) {
    if (-not (Test-Path $f)) { throw "No se genero $f" }
}

# Re-hash de lo copiado: lo publicado es EXACTAMENTE lo del manifiesto.
$pubSetupHash = (Get-FileHash -Algorithm SHA256 "site\pcsuite\download\PCSuite-Setup-$Version-win64.exe").Hash.ToLowerInvariant()
$pubZipHash   = (Get-FileHash -Algorithm SHA256 "site\pcsuite\download\PCSuite-$Version-win-x64.zip").Hash.ToLowerInvariant()
if ($pubSetupHash -ne $setupHash -or $pubZipHash -ne $zipHash) {
    throw "El SHA-256 de lo copiado no coincide con el manifiesto. NO publicar."
}

$mb = { param($x) [Math]::Round($x / 1MB) }

Write-Host ""
Write-Host "============================================"
Write-Host " $Product v$Version listo para publicar"
Write-Host "============================================"
Write-Host ("  Manifiesto  : site\pcsuite\latest.json")
Write-Host ("  Historial   : $archiveDir\manifest.json")
Write-Host ("  Instalador  : PCSuite-Setup-$Version-win64.exe  (~" + $mb.Invoke($setupSize) + " MB)")
Write-Host ("                 sha256: $setupHash")
Write-Host ("  Portable    : PCSuite-$Version-win-x64.zip      (~" + $mb.Invoke($zipSize) + " MB)")
Write-Host ("                 sha256: $zipHash")
Write-Host ""
Write-Host " Siguiente paso (manual): subir site\ al hosting."
Write-Host " La landing y el updater leen latest.json al cargar."
Write-Host "============================================"
