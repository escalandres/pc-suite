namespace PCSuite.Contracts;

/// <summary>
/// Payload de <see cref="OperationIds.PgpGenerateKey"/> (Task 009 / §6).
/// El resultado es directamente <see cref="PgpKeyInfoResult"/> de la clave
/// creada; los fallos llegan como error controlado PGP_FAILED (el material
/// privado jamás sale en claro: se guarda armoured en el keyring).
/// </summary>
public sealed class PgpGenerateKeyPayload
{
    /// <summary>Nombre del titular (requerido).</summary>
    public string? Name { get; init; }

    /// <summary>Email del titular (requerido, formato email).</summary>
    public string? Email { get; init; }

    /// <summary>ed25519 | rsa (validado contra allow-list en el servicio).</summary>
    public string? Algorithm { get; init; }

    /// <summary>Bits para RSA (2048/3072/4096); se omite en Ed25519.</summary>
    public int? Bits { get; init; }

    /// <summary>Caducidad en días (1–3650); null = sin expiración.</summary>
    public int? ExpirationDays { get; init; }

    /// <summary>Passphrase opcional que protege la clave privada; jamás se registra.</summary>
    public string? Passphrase { get; init; }
}
