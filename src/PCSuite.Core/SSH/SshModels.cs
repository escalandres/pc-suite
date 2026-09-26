namespace PCSuite.Core.SSH;

/// <summary>Algoritmos SSH admitidos por el generador (§5.1).</summary>
public static class SshAlgorithms
{
    /// <summary>Ed25519 (recomendado; sin opciones de tamaño).</summary>
    public const string Ed25519 = "ed25519";

    /// <summary>RSA (2048 / 3072 / 4096 bits).</summary>
    public const string Rsa = "rsa";

    /// <summary>ECDSA (256 / 384 / 521 bits, "where appropriate").</summary>
    public const string Ecdsa = "ecdsa";
}

/// <summary>
/// Campos parseados de una clave (§5.2: algoritmo, tamaño, fingerprint,
/// comentario) más la línea pública canónica para copiar al portapapeles.
/// </summary>
public sealed record SshParsedKey(
    string Type,
    string Algorithm,
    int Bits,
    string Fingerprint,
    string Comment,
    string PublicKey);

/// <summary>Clave listada/inspeccionada: campos parseados + archivo y estado público/privado.</summary>
public sealed record SshKeyInfo(
    string Type,
    string Algorithm,
    int Bits,
    string Fingerprint,
    string Comment,
    string Format,
    string Path,
    bool HasPrivate,
    string PublicKey);

/// <summary>Detección de ssh-keygen (§5.1 "detect installation").</summary>
public sealed record SshDetectStatus(bool Available, string? Path);

/// <summary>
/// Resultado de la generación. Un fallo controlado (clave ya existente,
/// ssh-keygen ausente o error del CLI) es <see cref="Succeeded"/> = false con
/// <see cref="Detail"/>; solo los errores del propio servicio lanzan
/// <see cref="SshException"/>.
/// </summary>
public sealed record SshGenerateOutcome(
    bool Succeeded,
    string? PrivateKeyPath,
    string? PublicKeyPath,
    SshKeyInfo? Key,
    string? Detail);

/// <summary>Clave pública importada en el directorio indicado.</summary>
public sealed record SshImportOutcome(string Path, SshKeyInfo Key);

/// <summary>Solicitud de generación (validada dentro del servicio antes de construir argv).</summary>
public sealed record SshGenerateRequest(
    string Algorithm,
    int? Bits,
    string Comment,
    string Directory,
    string? Passphrase);

/// <summary>Solicitud de importación de una clave pública (línea authorized_keys).</summary>
public sealed record SshImportRequest(string Key, string FileName, string Directory);
