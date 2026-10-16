using System.Reflection;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace PCSuite.Infrastructure.Updates;

/// <summary>
/// Verificación de firma PGP del manifiesto (Task 17.6).
///
/// La clave pública de firma se embeebe como recurso en el ensamblado
/// (ManifestSigningKey.asc). El updater NO la descarga: si un atacante
/// compromete el hosting, no puede sustituir la clave de validación.
///
/// TOFU (Trust On First Use):
/// - Primera ejecución: confía en la firma y archiva el fingerprint
///   en %LOCALAPPDATA%\PCSuite\Data\update-key-fingerprint.txt
/// - Ejecuciones posteriores: el fingerprint debe coincidir; si no,
///   avisa explícitamente al usuario.
/// </summary>
public sealed class ManifestSignatureVerifier
{
    private const string EmbeddedResourceName = "PCSuite.Infrastructure.Keys.ManifestSigningKey.asc";
    private const string FingerprintFileName = "update-key-fingerprint.txt";

    private readonly string _fingerprintPath;

    /// <summary>
    /// Clave pública inyectada por los tests (jamás se usa en producción):
    /// les permite firmar el manifiesto de su servidor local con una clave
    /// propia y verificar el circuito completo sin el material privado real.
    /// </summary>
    private readonly string? _publicKeyArmored;

    public ManifestSignatureVerifier()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataDir = Path.Combine(localAppData, "PCSuite", "Data");
        Directory.CreateDirectory(dataDir);
        _fingerprintPath = Path.Combine(dataDir, FingerprintFileName);
    }

    /// <summary>
    /// Constructor interno para tests y tools: clave pública armoured
    /// (null = la embebida de producción) y ruta del fingerprint TOFU
    /// explícitas — jamás se toca el %LOCALAPPDATA% real ni la clave de
    /// producción cuando no se quiere.
    /// </summary>
    internal ManifestSignatureVerifier(string? publicKeyArmored, string fingerprintFilePath)
    {
        _publicKeyArmored = publicKeyArmored;
        _fingerprintPath = fingerprintFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(fingerprintFilePath)!);
    }

    /// <summary>
    /// Verifica la firma PGP detached del manifiesto.
    /// </summary>
    /// <param name="manifestJson">Contenido exacto del manifiesto (bytes UTF-8).</param>
    /// <param name="signatureArmored">Firma PGP en formato armoured (latest.json.sig).</param>
    /// <returns>Resultado de la verificación.</returns>
    public async Task<SignatureVerificationResult> VerifyAsync(
        byte[] manifestJson,
        string signatureArmored,
        CancellationToken ct = default)
    {
        // 1. Cargar clave pública embebida
        var publicKey = LoadEmbeddedPublicKey();
        if (publicKey is null)
        {
            return new SignatureVerificationResult(
                Success: false,
                Error: "Clave pública de firma no disponible (recurso embebido no encontrado).",
                FingerprintMatch: false);
        }

        // 2. Parsear la firma
        PgpSignature signature;
        try
        {
            signature = ParseSignature(signatureArmored);
        }
        catch (Exception)
        {
            return new SignatureVerificationResult(
                Success: false,
                Error: "Formato de firma inválido.",
                FingerprintMatch: false);
        }

        // 3. Verificar que la firma corresponde a la clave embebida
        var fingerprint = ComputeFingerprint(publicKey);
        var expectedFingerprint = signature.KeyId.ToString("X16");
        var embeddedKeyId = publicKey.KeyId.ToString("X16");

        if (!expectedFingerprint.Equals(embeddedKeyId, StringComparison.OrdinalIgnoreCase))
        {
            return new SignatureVerificationResult(
                Success: false,
                Error: "La firma no corresponde a la clave de firma embebida.",
                FingerprintMatch: false);
        }

        // 4. Verificar la firma contra el contenido del manifiesto
        try
        {
            signature.InitVerify(publicKey);
            signature.Update(manifestJson);
            var valid = signature.Verify();

            if (!valid)
            {
                return new SignatureVerificationResult(
                    Success: false,
                    Error: "Firma PGP inválida: el manifiesto ha sido alterado.",
                    FingerprintMatch: true);
            }
        }
        catch (Exception)
        {
            return new SignatureVerificationResult(
                Success: false,
                Error: "Error al verificar la firma (algoritmo no soportado).",
                FingerprintMatch: true);
        }

        // 5. TOFU: comparar/guardar fingerprint
        var fingerprintMatch = await CheckAndStoreFingerprintAsync(fingerprint, ct);
        
        return new SignatureVerificationResult(
            Success: true,
            Error: null,
            FingerprintMatch: fingerprintMatch,
            Fingerprint: FormatFingerprint(fingerprint));
    }

    /// <summary>
    /// Carga la clave pública: la inyectada por los tests o, en producción,
    /// el recurso embebido.
    /// </summary>
    private PgpPublicKey? LoadEmbeddedPublicKey()
    {
        if (_publicKeyArmored is not null)
        {
            return LoadArmoredPublicKey(_publicKeyArmored);
        }

        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return LoadArmoredPublicKey(reader.ReadToEnd());
    }

    /// <summary>Parsea un bloque armoured con el anillo público.</summary>
    private static PgpPublicKey? LoadArmoredPublicKey(string armored)
    {
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(armored));
        using var armoredStream = new ArmoredInputStream(ms);
        var factory = new PgpObjectFactory(armoredStream);

        if (factory.NextPgpObject() is not PgpPublicKeyRing ring)
        {
            return null;
        }

        return ring.GetPublicKey();
    }

    /// <summary>
    /// Parsea una firma PGP en formato armoured.
    /// </summary>
    private static PgpSignature ParseSignature(string armored)
    {
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(armored));
        using var armoredStream = new ArmoredInputStream(ms);
        var factory = new PgpObjectFactory(armoredStream);
        
        if (factory.NextPgpObject() is PgpSignatureList list && list.Count > 0)
        {
            return list[0];
        }

        throw new InvalidOperationException("No se encontró firma en el bloque armoured.");
    }

    /// <summary>
    /// Calcula el fingerprint (SHA-1 de la clave maestra) en hex uppercase.
    /// </summary>
    private static string ComputeFingerprint(PgpPublicKey key)
    {
        var fp = key.GetFingerprint();
        return Convert.ToHexString(fp);
    }

    /// <summary>
    /// Formatea fingerprint con espacios cada 4 caracteres para mostrar.
    /// </summary>
    private static string FormatFingerprint(string fingerprint)
    {
        return string.Join(" ", Enumerable.Range(0, fingerprint.Length / 4)
            .Select(i => fingerprint.Substring(i * 4, 4)));
    }

    /// <summary>
    /// TOFU: verifica y guarda el fingerprint.
    /// Primera vez: guarda y devuelve true.
    /// Veces siguientes: devuelve true si coincide, false si cambió.
    /// </summary>
    private async Task<bool> CheckAndStoreFingerprintAsync(string fingerprint, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(_fingerprintPath))
        {
            // Primera vez: confiar y guardar
            await File.WriteAllTextAsync(_fingerprintPath, fingerprint, ct);
            return true;
        }

        var stored = await File.ReadAllTextAsync(_fingerprintPath, ct);
        return stored.Trim().Equals(fingerprint, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Resultado de la verificación de firma PGP del manifiesto.
/// </summary>
public sealed record SignatureVerificationResult(
    bool Success,
    string? Error,
    bool FingerprintMatch,
    string? Fingerprint = null);