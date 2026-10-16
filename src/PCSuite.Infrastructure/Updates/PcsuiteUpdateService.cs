using System.Security.Cryptography;
using System.Text.Json;
using PCSuite.Core.Updates;

namespace PCSuite.Infrastructure.Updates;

/// <summary>
/// Servicio de actualizaciones (Task 17.3/17.4/17.6, propuesta §8).
///
/// Check: GET al manifiesto con timeout corto y Cache-Control: no-cache —
/// nunca bloquea nada (lo llama la UI al arrancar y desde Ajustes). Un fallo
/// de red o timeout es un resultado silencioso Available=false: jamás un
/// error en la cara del usuario por una comprobación de fondo.
///
/// PGP Signature Verification (Task 17.6):
/// - Descarga latest.json + latest.json.sig en paralelo
/// - Verifica la firma contra la clave pública embebida (ManifestSigningKey.asc)
/// - TOFU: primera vez confía y guarda fingerprint; si cambia → aviso
/// - Firma inválida o fingerprint cambiado → Available=false (silencioso)
///
/// Download: UNA petición al manifiesto + streaming de 64 KB con SHA-256 en
/// paralelo (el hash sale gratis, mismo patrón que Local Share). El hash del
/// manifiesto es el del archivo EXACTO que publicó publicar.bat:
/// discrepancia = descarga corrupta o manipulada → se borra y NO se instala.
/// Cualquier fallo intermedio también borra el parcial de %TEMP%.
///
/// Install: solo la ruta verificada en ESTA sesión (recordada internamente),
/// re-hasheada ANTES de lanzar (¿alguien tocó el archivo tras la descarga?).
/// Lanzamiento fire-and-forget: el Setup pide UAC él solo y la app se cierra.
///
/// Versionado SemVer estricto (0.5.10 > 0.5.9; jamás lexicográfico).
/// </summary>
public sealed class PcsuiteUpdateService : IPcsuiteUpdateService
{
    private static readonly HttpClient Http = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Manifiesto por defecto: el contrato público e inmutable.</summary>
    public const string DefaultManifestUrl = "https://aes-studio.dev/pcsuite/latest.json";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);
    private const int ChunkSize = 64 * 1024;

    private readonly string _manifestUrl;
    private readonly Func<string, bool> _launchInstaller;
    private readonly string _currentVersion;
    private readonly ManifestSignatureVerifier _signatureVerifier = new();

    /// <summary>Ruta + artefacto verificados en esta sesión (Download → Install gate).</summary>
    private string? _verifiedPath;
    private UpdateArtifact? _verifiedArtifact;

    public PcsuiteUpdateService(
        string? manifestUrl = null,
        Func<string, bool>? launchInstaller = null,
        string? currentVersion = null)
    {
        _manifestUrl = manifestUrl ?? DefaultManifestUrl;
        _launchInstaller = launchInstaller ?? DefaultLaunch;
        _currentVersion = currentVersion ?? CurrentAssemblyVersion();
    }

    /// <summary>
    /// Constructor interno para tests: verificador de firma con clave propia
    /// (el servidor local firma con una clave de test, no la de producción).
    /// El comportamiento en producción no cambia.
    /// </summary>
    internal PcsuiteUpdateService(
        string? manifestUrl,
        Func<string, bool>? launchInstaller,
        string? currentVersion,
        ManifestSignatureVerifier signatureVerifier)
        : this(manifestUrl, launchInstaller, currentVersion)
    {
        _signatureVerifier = signatureVerifier;
    }

    // ------------------------------------------------------------------
    // Manifiesto (compartido por Check y Download)
    // ------------------------------------------------------------------

    private async Task<(UpdateManifest Manifest, UpdateArtifact Setup, SignatureVerificationResult SigResult)> FetchManifestAsync(
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(CheckTimeout);

        // Descargar manifiesto y firma en paralelo
        var manifestUrl = _manifestUrl;
        var signatureUrl = manifestUrl + ".sig";

        using var manifestRequest = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
        manifestRequest.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true,
        };

        using var sigRequest = new HttpRequestMessage(HttpMethod.Get, signatureUrl);
        sigRequest.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true,
        };

        var manifestTask = Http.SendAsync(manifestRequest, timeoutCts.Token);
        var sigTask = Http.SendAsync(sigRequest, timeoutCts.Token);

        await Task.WhenAll(manifestTask, sigTask).ConfigureAwait(false);

        using var manifestResponse = manifestTask.Result;
        manifestResponse.EnsureSuccessStatusCode();

        var json = await manifestResponse.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOpts)
            ?? throw new InvalidOperationException("Manifiesto vacío.");

        // Verificar firma PGP
        string? signatureArmored = null;
        using var sigResponse = sigTask.Result;
        if (sigResponse.IsSuccessStatusCode)
        {
            signatureArmored = await sigResponse.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        }

        var sigResult = await _signatureVerifier.VerifyAsync(
            System.Text.Encoding.UTF8.GetBytes(json),
            signatureArmored ?? string.Empty,
            ct).ConfigureAwait(false);

        // Si la firma no es válida, NO confiar en el manifiesto
        if (!sigResult.Success)
        {
            throw new InvalidOperationException($"Firma del manifiesto inválida: {sigResult.Error}");
        }

        var setup = manifest.Artifacts.FirstOrDefault(a => a.Id == "setup-win64")
            ?? throw new InvalidOperationException(
                "El manifiesto no declara el artefacto setup-win64.");

        return (manifest, setup, sigResult);
    }

    // ------------------------------------------------------------------
    // Check
    // ------------------------------------------------------------------

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var current = _currentVersion;
        try
        {
            var (manifest, setup, sigResult) = await FetchManifestAsync(ct).ConfigureAwait(false);

            var available = CompareVersions(manifest.Version, current) > 0;

            return new UpdateCheckResult
            {
                Available = available,
                CurrentVersion = current,
                LatestVersion = manifest.Version,
                SizeBytes = setup.Size,
                ReleaseNotesUrl = string.IsNullOrEmpty(manifest.ReleaseNotesUrl)
                    ? null
                    : manifest.ReleaseNotesUrl,
                Error = null,
                KeyRotationWarning = sigResult.FingerprintMatch ? null : 
                    "La clave de firma cambió. Verifica el fingerprint en aes-studio.dev antes de actualizar."
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout del check (no una cancelación del caller): silencioso.
            return new UpdateCheckResult { Available = false, CurrentVersion = current };
        }
        catch (Exception)
        {
            // Sin red / sin manifiesto / firma inválida: la app funciona igual. Sin dramatismo.
            return new UpdateCheckResult { Available = false, CurrentVersion = current };
        }
    }

    // ------------------------------------------------------------------
    // Download (SHA-256 en streaming)
    // ------------------------------------------------------------------

    public async Task<string> DownloadAsync(
        Action<long, long>? onProgress = null,
        CancellationToken ct = default)
    {
        // 1. Manifiesto + validación de versión (UNA petición).
        var (manifest, artifact, sigResult) = await FetchManifestAsync(ct).ConfigureAwait(false);
        
        if (CompareVersions(manifest.Version, _currentVersion) <= 0)
        {
            throw new InvalidOperationException("No hay actualización disponible.");
        }

        // TOFU: si el fingerprint cambió, no permitir la descarga
        if (!sigResult.FingerprintMatch)
        {
            throw new InvalidOperationException(
                "La clave de firma del manifiesto cambió. " +
                "Verifica el fingerprint en aes-studio.dev antes de actualizar.");
        }

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"PCSuite-Setup-{manifest.Version}.exe");

        // 2. Streaming: cada bloque alimenta disco + SHA-256 en paralelo.
        try
        {
            using var response = await Http.GetAsync(
                artifact.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = artifact.Size > 0
                ? artifact.Size
                : response.Content.Headers.ContentLength ?? 0;

            await using var source = await response.Content
                .ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var target = File.Create(tempPath);

            var buffer = new byte[ChunkSize];
            var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                received += read;
                onProgress?.Invoke(received, totalBytes);
            }

            // 3. Tamaño: si el manifiesto declaró tamaño, debe coincidir exacto.
            if (totalBytes > 0 && received != totalBytes)
            {
                throw new InvalidOperationException(
                    $"Descarga incompleta ({received} de {totalBytes} bytes). Vuelve a intentarlo.");
            }

            // 4. SHA-256: LA barrera. No coincide → jamás se instala.
            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!string.Equals(actual, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "SHA-256 no coincide: la descarga se corrompió o fue manipulada. " +
                    "No se instalará. Vuelve a intentarlo.");
            }
        }
        catch
        {
            // Cualquier fallo (red, tamaño, hash): no dejar parciales en %TEMP%.
            TryDelete(tempPath);
            throw;
        }

        // Solo tras verificar TODO se recuerda la ruta: InstallAsync no acepta otra.
        _verifiedPath = tempPath;
        _verifiedArtifact = artifact;
        return tempPath;
    }

    // ------------------------------------------------------------------
    // Install
    // ------------------------------------------------------------------

    public Task InstallAsync(string verifiedPath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(verifiedPath)
            || _verifiedPath is null
            || !string.Equals(verifiedPath, _verifiedPath, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(verifiedPath))
        {
            throw new InvalidOperationException(
                "Ese archivo no fue descargado y verificado en esta sesión. " +
                "Descarga la actualización primero.");
        }

        // Re-hash ANTES de lanzar: el archivo de %TEMP% sigue siendo el que
        // verificamos (¿se tocó desde la descarga? TOCTOU cerrado).
        if (_verifiedArtifact is { } artifact)
        {
            var current = Convert.ToHexStringLower(
                SHA256.HashData(File.ReadAllBytes(verifiedPath)));
            if (!string.Equals(current, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(verifiedPath);
                _verifiedPath = null;
                _verifiedArtifact = null;
                throw new InvalidOperationException(
                    "El archivo cambió tras la verificación. Descarga de nuevo.");
            }
        }

        if (!_launchInstaller(verifiedPath))
        {
            throw new InvalidOperationException(
                "No se pudo iniciar el instalador. Ejecútalo manualmente desde %TEMP%.");
        }

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Utilidades
    // ------------------------------------------------------------------

    /// <summary>
    /// Compara dos versiones SemVer. Devuelve >0 si a > b, <0 si a < b, 0 si iguales.
    /// Normaliza a (major, minor, patch); ignora sufijos ("-beta", "+build").
    /// </summary>
    internal static int CompareVersions(string a, string b) => ParseCore(a).CompareTo(ParseCore(b));

    private static (int Major, int Minor, int Patch) ParseCore(string version)
    {
        // "1.2.4-beta.1+build" → "1.2.4"
        var core = version.Split('+')[0].Split('-')[0];
        var parts = core.Split('.');
        return (
            parts.Length > 0 && int.TryParse(parts[0], out var major) ? major : 0,
            parts.Length > 1 && int.TryParse(parts[1], out var minor) ? minor : 0,
            parts.Length > 2 && int.TryParse(parts[2], out var patch) ? patch : 0);
    }

    private static string CurrentAssemblyVersion()
    {
        var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private static bool DefaultLaunch(string path)
    {
        try
        {
            // Fire-and-forget: el Setup pide UAC él solo si la instalación lo
            // requiere (PrivilegesRequiredOverridesAllowed=dialog). La app se
            // cierra justo después para dejarle el paso.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort */ }
    }
}
