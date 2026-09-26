namespace PCSuite.Contracts;

/// <summary>Payload de <see cref="OperationIds.PgpSign"/> (Task 009 / §6).</summary>
public sealed class PgpSignPayload
{
    /// <summary>Texto a firmar (acotado en el servicio).</summary>
    public string? Text { get; init; }

    /// <summary>Fingerprint (o prefijo) de la clave privada que firma.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>Passphrase de la clave privada si está protegida. Jamás se registra.</summary>
    public string? Passphrase { get; init; }
}

/// <summary>Resultado de <see cref="OperationIds.PgpSign"/>.</summary>
public sealed class PgpSignResult
{
    /// <summary>Firma separada armoured (-----BEGIN PGP SIGNATURE-----).</summary>
    public string Signature { get; init; } = string.Empty;
}
