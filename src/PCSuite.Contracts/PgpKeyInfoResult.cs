namespace PCSuite.Contracts;

/// <summary>Clave PGP listada/inspeccionada/exportada (Task 009 / §6).</summary>
public sealed class PgpKeyInfoResult
{
    /// <summary>Fingerprint hexadecimal completo (40 dígitos, mayúsculas).</summary>
    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>Key ID (últimos 8 bytes del fingerprint, 16 dígitos).</summary>
    public string KeyId { get; init; } = string.Empty;

    /// <summary>User ID completo, p. ej. "Ana &lt;ana@example.com&gt;".</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>Nombre del titular (parte del User ID).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Email del titular (parte del User ID).</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Algoritmo de la clave primaria (rsa | ed25519).</summary>
    public string Algorithm { get; init; } = string.Empty;

    /// <summary>Tamaño en bits para RSA; null en Ed25519.</summary>
    public int? Bits { get; init; }

    /// <summary>Fecha de creación de la clave.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Caducidad (§6 "key expiration"); null = sin expiración.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>True si el keyring guarda también la clave privada.</summary>
    public bool HasPrivate { get; init; }

    /// <summary>
    /// True si la clave admite cifrado (subclave de cifrado presente): puede
    /// ser destinataria; solo clave firmante → false.
    /// </summary>
    public bool CanEncrypt { get; init; }
}
