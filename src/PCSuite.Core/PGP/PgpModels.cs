namespace PCSuite.Core.PGP;

/// <summary>Algoritmos PGP admitidos por el generador (§6 "Algorithm").</summary>
public static class PgpAlgorithms
{
    /// <summary>
    /// Ed25519 (primaria de firma) + ECDH Curve25519 (subclave de cifrado);
    /// sin opción de tamaño, es la recomendada.
    /// </summary>
    public const string Ed25519 = "ed25519";

    /// <summary>RSA (primaria de firma + subclave RSA de cifrado), 2048/3072/4096 bits.</summary>
    public const string Rsa = "rsa";

    public static readonly IReadOnlyList<string> All = [Ed25519, Rsa];

    public static readonly IReadOnlyList<int> RsaBits = [2048, 3072, 4096];
}

/// <summary>
/// Clave listada/inspeccionada del keyring (§6): fingerprint, User ID,
/// algoritmo, caducidad y estado público/privado. Es también la entrada del
/// índice <c>keyring.json</c> (serializada camelCase con BridgeJson).
/// </summary>
public sealed record PgpKeyInfo(
    string Fingerprint,
    string KeyId,
    string UserId,
    string Name,
    string Email,
    string Algorithm,
    int? Bits,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    bool HasPrivate,
    bool CanEncrypt);

/// <summary>Solicitud de generación de par (validada dentro del servicio).</summary>
public sealed record PgpGenerateKeyRequest(
    string Name,
    string Email,
    string Algorithm,
    int? Bits,
    int? ExpirationDays,
    string? Passphrase);

/// <summary>Solicitud de importación de una clave armoured (pública o privada).</summary>
public sealed record PgpImportKeyRequest(string ArmoredKey);

/// <summary>Solicitud de exportación por fingerprint (opción de clave privada).</summary>
public sealed record PgpExportKeyRequest(string Fingerprint, bool IncludePrivate);

/// <summary>Solicitud de cifrado: texto + destinatarios (fingerprint o email).</summary>
public sealed record PgpEncryptRequest(string Text, IReadOnlyList<string> Recipients);

/// <summary>Solicitud de descifrado; el passphrase solo se usa en memoria.</summary>
public sealed record PgpDecryptRequest(string ArmoredMessage, string? Passphrase);

/// <summary>Solicitud de firma separada con la clave privada indicada.</summary>
public sealed record PgpSignRequest(string Text, string Fingerprint, string? Passphrase);

/// <summary>Solicitud de verificación de una firma separada contra el texto.</summary>
public sealed record PgpVerifyRequest(string Text, string Signature);

/// <summary>
/// Resultado de la verificación (§6): una firma no válida o ilegible es un
/// resultado legítimo con <see cref="Valid"/> = false y motivo controlado en
/// <see cref="Detail"/> — no un error del puente. <see cref="Signer"/> se
/// rellena cuando la clave firmante está en el keyring.
/// </summary>
public sealed record PgpVerifyOutcome(bool Valid, string? Detail, PgpKeyInfo? Signer);
