namespace PCSuite.Core.PGP;

/// <summary>
/// Fachada PGP (Task 009 / §6): OpenPGP maduro en proceso con BouncyCastle
/// (<c>Org.BouncyCastle.Bcpg.OpenPgp</c>) — nunca gpg (ausente en Windows)
/// ni criptografía casera. El keyring propio vive en %APPDATA%\PCSuite\pgp
/// con escritura atómica; el material privado y los passphrases jamás se
/// registran en logs ni llegan al frontend en claro (solo bloques armoured
/// solicitados explícitamente). Nada requiere elevación.
/// </summary>
public interface IPgpService
{
    /// <summary>
    /// Generación de par (§6): nombre/email, algoritmo, caducidad y
    /// passphrase opcional. Devuelve la clave creada (fingerprint incluido).
    /// </summary>
    Task<PgpKeyInfo> GenerateKeyAsync(PgpGenerateKeyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Claves del keyring (§6 inspect/list); keyring vacío → lista vacía.</summary>
    Task<IReadOnlyList<PgpKeyInfo>> ListKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Importación de una clave armoured (pública o privada): se indexa en el
    /// keyring y su material se guarda en archivo. Nunca sobrescribe otra
    /// clave distinta (upsert por fingerprint).
    /// </summary>
    Task<PgpKeyInfo> ImportKeyAsync(PgpImportKeyRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exportación armoured por fingerprint (§6 "export key"). La privada
    /// solo si se pide explícitamente y existe en el keyring.
    /// </summary>
    Task<string> ExportKeyAsync(PgpExportKeyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Cifrado para destinatarios (fingerprint o email exacto).</summary>
    Task<string> EncryptAsync(PgpEncryptRequest request, CancellationToken cancellationToken = default);

    /// <summary>Descifrado con la clave privada del keyring (passphrase si procede).</summary>
    Task<string> DecryptAsync(PgpDecryptRequest request, CancellationToken cancellationToken = default);

    /// <summary>Firma separada (armoured) con la clave privada indicada.</summary>
    Task<string> SignAsync(PgpSignRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verificación de la firma contra el texto: firma no válida = resultado
    /// con Valid = false (no excepción).
    /// </summary>
    Task<PgpVerifyOutcome> VerifyAsync(PgpVerifyRequest request, CancellationToken cancellationToken = default);
}
