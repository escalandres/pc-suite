# ============================================================================
# publicar.ps1 (Task 17.1/17.5/17.6) — genera el manifiesto del updater,
# las notas de versión HTML, la firma PGP y publica los artefactos como
# GitHub Release.
#
# Flujo (propuesta Task 17, decisión 2026-10-14):
#   1. Artefactos ya compilados (empaquetar.bat + ISCC) de la versión actual.
#   2. SHA-256 del archivo EXACTO que se sube (fuente de verdad del updater).
#   3. Manifiestos con URLs de GitHub Releases (los binarios viven ahí,
#      no en el sitio: el deploy de Vercel queda en KBs).
#   4. Notas de versión: markdown → HTML (site\notes\<vers>.html).
#   5. Firma PGP: latest.json → latest.json.sig + latest.json.pub.
#   6. Si gh CLI está autenticado: crea el release con ambos assets.
#      Si no: imprime el comando exacto para copiar.
#
# Inmutabilidad: si la versión ya está en site\archive, aborta. El historial
# jamás se reescribe — las apps instaladas pinnean la URL del manifiesto.
# ============================================================================
param(
    [string]$Product = "pcsuite",
    [string]$Version = "",
    [string]$ReleaseRepo = "escalandres/pc-suite",
    [string]$TagPrefix = "pcsuite-v",
    [string]$GpgKey = "releases@aes-studio.dev",  # Email/fingerprint de la clave de firma
    [switch]$SkipSign,  # Omitir firma PGP (para testing)
    [switch]$UseGpg  # Legacy: firmar con gpg CLI en vez de SignRelease (Task 19.6)
)

$ErrorActionPreference = "Stop"
Set-Location -Path (Join-Path $PSScriptRoot "..")

# -----------------------------------------------------------------------------
# Helpers
# -----------------------------------------------------------------------------

function Convert-MarkdownToHtml {
    param([string]$MarkdownPath, [string]$Version, [string]$RelDate)

    if (-not (Test-Path $MarkdownPath)) {
        Write-Host "  ⚠ No existe $MarkdownPath — se omite la generación de notas HTML."
        return
    }

    $md = Get-Content -Raw -Path $MarkdownPath -Encoding UTF8

    # Conversión simple de Markdown a HTML (suficiente para nuestras notas)
    # Nota: para producción real se recomienda pandoc o marked.js via Node
    $html = $md `
        -replace '(?m)^### (.+)$', '<h3>$1</h3>' `
        -replace '(?m)^## (.+)$', '<h2>$1</h2>' `
        -replace '(?m)^# (.+)$', '<h1>$1</h1>' `
        -replace '(?m)^> (.+)$', '<blockquote><p>$1</p></blockquote>' `
        -replace '(?m)^\*\*(.+?)\*\*', '<strong>$1</strong>' `
        -replace '(?m)`([^`]+)`', '<code>$1</code>' `
        -replace '(?m)^\|(.+)\|$', '<tr><td>$1</td></tr>' `
        -replace '(?m)^---$', '<hr>' `
        -replace '\*\*(.+?)\*\*', '<strong>$1</strong>' `
        -replace '`([^`]+)`', '<code>$1</code>' `
        -replace '(?m)^- (.+)$', '<li>$1</li>' `
        -replace '(?m)^(\d+)\. (.+)$', '<li>$2</li>'

    # Wrap list items in <ul>
    $html = $html -replace '(<li>.*</li>\n)+', '<ul>$&</ul>'

    # Paragraphs: split by double newline, wrap non-tag lines in <p>
    $paragraphs = $html -split '\n\n'
    $result = ""
    foreach ($para in $paragraphs) {
        $trimmed = $para.Trim()
        if (-not $trimmed) { continue }
        if ($trimmed -match '^<(h[1-6]|ul|ol|li|blockquote|table|tr|th|td|hr|pre)') {
            $result += "$trimmed\n\n"
        } else {
            $result += "<p>$trimmed</p>\n\n"
        }
    }
    $html = $result.Trim()

    # Tabla de descargas: convertir formato markdown simple a tabla HTML
    $html = $html -replace '<p>\|(.+)\|</p>', '<tr><td>$1</td></tr>'
    $html = $html -replace '<tr><td>Artefacto.+?</td></tr>', '<thead><tr><th>Artefacto</th><th>Tamaño</th><th>SHA-256</th></tr></thead>'
    $html = $html -replace '(<tr><td>.+?</td></tr>\n)+', '<tbody>$&</tbody>'
    $html = $html -replace '(<thead>.+?</tbody>)', '<table>$1</table>'

    # Code blocks (PowerShell)
    $html = $html -replace '```powershell\n(.+?)\n```', '<pre><code class="language-powershell">$1</code></pre>' -replace '```\w*\n(.+?)\n```', '<pre><code>$1</code></pre>'

    # Template HTML completo
    $fullHtml = @"
<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>PCSuite $Version — Notas de versión</title>
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
    <h1>PCSuite $Version</h1>
    <p class="section-sub">Publicado el $([DateTime]::ParseExact($RelDate, "yyyy-MM-ddTHH:mm:ssZ", $null).ToString("d 'de' MMMM 'de' yyyy", (New-Object System.Globalization.CultureInfo "es-ES")))</p>

    <article class="notes-content">
$html
    </article>
  </main>
  <footer>
    <p>Requiere Windows 10/11 x64 · WebView2 Runtime (preinstalado con Edge)</p>
    <p>Sin telemetría: PCSuite no envía nada a ningún servidor salvo la consulta de actualizaciones (y con tu consentimiento para instalar).</p>
    <p class="copyright">© $((Get-Date).Year) AES Studio · sha256 por versión publicado en el manifiesto</p>
  </footer>
</body>
</html>
"@

    $notesDir = "site\notes"
    New-Item -ItemType Directory -Force -Path $notesDir | Out-Null
    $outFile = Join-Path $notesDir "$Version.html"
    [IO.File]::WriteAllText($outFile, $fullHtml, [Text.UTF8Encoding]::new($false))
    Write-Host "  ✓ Notas HTML generadas: $outFile"
}

function Get-SigningFingerprint {
    param([string]$GpgKey, [switch]$UseGpg)

    # Fingerprint de la clave de firma SIN firmar nada (Task 19.6): se
    # inyecta en latest.json antes de que la firma cubra el contenido final.
    if (-not $UseGpg) {
    # Ruta del tool: el DLL directo (dotnet exec) — NO el apphost exe ni
    # 'dotnet run'. El exe recien compilado dispara Access Denied
    # intermitente (Defender sobre binarios sin firmar que tocan
    # criptografia); el DLL via 'dotnet <dll>' es estable y ademas evita
    # el restore+build de cada llamada.
    $signCsproj = Join-Path $PSScriptRoot "..\src\Tools\SignRelease\SignRelease.csproj"
    $signDll = Join-Path $PSScriptRoot "..\src\Tools\SignRelease\bin\Debug\net10.0\SignRelease.dll"
    if (-not (Test-Path $signDll))
    {
        & dotnet build $signCsproj --nologo -v quiet | Out-Null
    }
    $result = & dotnet $signDll --fingerprint-only --email $GpgKey 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ⚠ SignRelease no pudo leer el fingerprint: $result"
            return $null
        }
        # Última línea del output = fingerprint formateado (AB12 CD34 ...).
        return @($result | Where-Object { $_ -match '^([0-9A-F]{4} )+[0-9A-F]{4}$' })[-1]
    }

    $fprResult = & gpg --batch --yes --fingerprint --with-colons $GpgKey 2>&1
    if ($LASTEXITCODE -eq 0) {
        $fprMatch = [regex]::Match($fprResult, 'fpr::::::::([0-9A-F]{40}):')
        if ($fprMatch.Success) {
            return (($fprMatch.Groups[1].Value) -split '(....)' | Where-Object { $_ }) -join ' '
        }
    }
    return $null
}

function Sign-Manifest {
    param([string]$ManifestPath, [string]$GpgKey, [switch]$Skip, [switch]$UseGpg)

    if ($Skip) {
        Write-Host "  ⚠ Firma PGP omitida (flag -SkipSign)."
        return
    }

    $sigPath = "$ManifestPath.sig"
    $pubPath = "$ManifestPath.pub"

    # Ruta por defecto (Task 19.6): SignRelease — el MISMO stack que la app
    # (PgpService/BouncyCastle en proceso), sin gpg CLI. La clave de
    # releases@aes-studio.dev vive en el keyring local (%APPDATA%\PCSuite\pgp);
    # su mitad pública es la embebida en el updater (ManifestSigningKey.asc),
    # así que la firma generada aquí es la que el updater acepta.
    if (-not $UseGpg) {
        Write-Host "  Firmando manifiesto con SignRelease (PgpService, sin gpg)..."
        $signCsproj = Join-Path $PSScriptRoot "..\src\Tools\SignRelease\SignRelease.csproj"
        $signDll = Join-Path $PSScriptRoot "..\src\Tools\SignRelease\bin\Debug\net10.0\SignRelease.dll"
        if (-not (Test-Path $signDll))
        {
            & dotnet build $signCsproj --nologo -v quiet | Out-Null
        }
        $signResult = & dotnet $signDll $ManifestPath --email $GpgKey 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Error firmando con SignRelease: $signResult (gpg legacy: -UseGpg)"
        }
        if (Test-Path $sigPath) { Write-Host "  ✓ Firma generada: $sigPath" }
        if (Test-Path $pubPath) { Write-Host "  ✓ Clave pública exportada: $pubPath" }

        # Sanity check ANTES de publicar: la firma debe verificar contra la
        # clave EMBEBIDA del updater — si falla, el release se detiene aquí
        # en vez de producir un latest.json que ningún updater aceptaría.
        $verifyResult = & dotnet $signDll $ManifestPath --verify 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "La firma NO verifica contra la clave embebida: $verifyResult"
        }
        Write-Host "  ✓ Firma verificada contra la clave embebida del updater"

        $fprFormatted = @($signResult |
            Select-String 'signingKeyFingerprint \(JSON\): (.+)$').
            Matches.Groups[1].Value
        if ($fprFormatted) { Write-Host "  ✓ Fingerprint: $fprFormatted" }
        return $fprFormatted
    }

    # Legacy: gpg CLI (requiere la clave privada importada en el keyring de gpg).
    Write-Host "  Firmando manifiesto con GPG (clave: $GpgKey)..."

    # Firmar detached (armored)
    $signResult = & gpg --batch --yes --armor --detach-sign --local-user $GpgKey --output $sigPath $ManifestPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Error firmando con GPG: $signResult"
    }
    Write-Host "  ✓ Firma generada: $sigPath"

    # Exportar clave pública
    $exportResult = & gpg --batch --yes --armor --export $GpgKey 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Error exportando clave pública: $exportResult"
    }
    [IO.File]::WriteAllText($pubPath, $exportResult, [Text.UTF8Encoding]::new($false))
    Write-Host "  ✓ Clave pública exportada: $pubPath"

    # Extraer fingerprint SHA-256 de la clave pública (para el JSON)
    $fprResult = & gpg --batch --yes --fingerprint --with-colons $GpgKey 2>&1
    if ($LASTEXITCODE -eq 0) {
        # Formato: fpr::::::::<40 hex chars>:
        $fprMatch = [regex]::Match($fprResult, 'fpr::::::::([0-9A-F]{40}):')
        if ($fprMatch.Success) {
            $fpr = $fprMatch.Groups[1].Value
            $fprFormatted = ($fpr -split '(....)' | Where-Object { $_ }) -join ' '
            Write-Host "  ✓ Fingerprint: $fprFormatted"
            return $fprFormatted
        }
    }
    return $null
}

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
Write-Host "[1/6] Computando SHA-256 de los artefactos..."

$setupHash = (Get-FileHash -Algorithm SHA256 $setupRel).Hash.ToLowerInvariant()
$zipHash   = (Get-FileHash -Algorithm SHA256 $zipRel).Hash.ToLowerInvariant()
$setupSize = (Get-Item $setupRel).Length
$zipSize   = (Get-Item $zipRel).Length
$relDate   = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")

Write-Host "        Instalador: $setupHash"
Write-Host "        Portable  : $zipHash"

# ---------- 4. Manifiestos con URLs de GitHub Releases ----------
Write-Host ""
Write-Host "[2/6] Generando manifiestos (URLs de GitHub Releases)..."

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
  "releaseNotesUrl": "https://aes-studio.dev/notes/$Version.html",
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

# ---------- 3. Generar HTML de notas de versión (Task 17.5) ----------
Write-Host ""
Write-Host "[3/6] Generando notas de versión HTML..."
$notesMd = "docs\landing\notas\$Version.md"
Convert-MarkdownToHtml -MarkdownPath $notesMd -Version $Version -RelDate $relDate

# ---------- 4. Firmar manifiesto con PGP (Task 17.6) ----------
Write-Host ""
Write-Host "[4/6] Inyectando signingKeyFingerprint en el manifiesto..."
$manifestPath = Join-Path (Get-Location) "site\pcsuite\latest.json"

# CRÍTICO: el fingerprint se inyecta ANTES de firmar. La firma PGP cubre el
# contenido EXACTO del latest.json desplegado — si el JSON mutara después de
# firmarse, todos los updaters instalados la rechazarían ("manifiesto
# alterado") y las actualizaciones no aparecerían nunca.
$fingerprint = Get-SigningFingerprint -GpgKey $GpgKey -UseGpg:$UseGpg

if ($fingerprint) {
    # Bug 2026-10-19: la concatenación con '+' a la derecha de -replace hace
    # que PowerShell la parsee como ARRAY de argumentos ("solo dos elementos,
    # no 4"). El reemplazo se construye PRIMERO en su propia variable; además
    # es -ireplace literal: el fingerprint no es regex y el JSON es texto.
    $replacement = '"signingKeyFingerprint": "' + $fingerprint + '"' + ",`n" + '  "artifacts":'
    $jsonWithFpr = $json.Replace('"artifacts":', $replacement)
    [IO.File]::WriteAllText(
        (Join-Path (Get-Location) "site\pcsuite\latest.json"),
        $jsonWithFpr,
        [Text.UTF8Encoding]::new($false))
    # Actualizar también el archivo en archive
    [IO.File]::WriteAllText(
        (Join-Path (Get-Location) "$archiveDir\manifest.json"),
        $jsonWithFpr,
        [Text.UTF8Encoding]::new($false))
    Write-Host "  ✓ latest.json actualizado con signingKeyFingerprint"
} else {
    Write-Host "  ⚠ Sin fingerprint: el manifiesto llevará el campo de la clave embebida vacío."
}

Write-Host "[5/6] Firmando manifiesto PGP (sobre el contenido FINAL)..."
$null = Sign-Manifest -ManifestPath $manifestPath -GpgKey $GpgKey -SkipSign:$SkipSign -UseGpg:$UseGpg

# Copiar .sig y .pub al directorio archive también
$sigSrc = "$manifestPath.sig"
$pubSrc = "$manifestPath.pub"
if (Test-Path $sigSrc) {
    Copy-Item $sigSrc (Join-Path $archiveDir "latest.json.sig") -Force
    Write-Host "  ✓ Firma copiada a archive"
}
if (Test-Path $pubSrc) {
    Copy-Item $pubSrc (Join-Path $archiveDir "latest.json.pub") -Force
    Write-Host "  ✓ Clave pública copiada a archive"
}

# ---------- 5. Release en GitHub (gh) ----------
Write-Host ""
Write-Host "[6/6] Publicando el release en GitHub..."

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
Write-Host ("  Manifiesto      : site\pcsuite\latest.json")
Write-Host ("  Historial       : $archiveDir\manifest.json")
Write-Host ("  Release         : https://github.com/$ReleaseRepo/releases/tag/$tag")
Write-Host ("  Notas HTML      : site\notes\$Version.html")
Write-Host ("  Firma PGP       : site\pcsuite\latest.json.sig")
Write-Host ("  Clave pública   : site\pcsuite\latest.json.pub")
Write-Host ("  Instalador      : $setupName  (~" + $mb.Invoke($setupSize) + " MB)")
Write-Host ("                    sha256: $setupHash")
Write-Host ("  Portable        : $zipName  (~" + $mb.Invoke($zipSize) + " MB)")
Write-Host ("                    sha256: $zipHash")
Write-Host ""
Write-Host " Siguiente paso (manual): subir site\ al repo del sitio (Vercel)."
Write-Host "============================================"
