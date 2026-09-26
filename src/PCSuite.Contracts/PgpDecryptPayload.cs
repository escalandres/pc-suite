namespace PCSuite.Contracts;

/// <summary>Payload de <see cref="OperationIds.PgpDecrypt"/> (Task 009 / §6).</summary>
public sealed class PgpDecryptPayload
{
    /// <summary>Mensaje cifrado armoured (acotado en el servicio).</summary>
    public string? ArmoredMessage { get; init; }

    /// <summary>Passphrase de la clave privada; solo si está protegida. Jamás se registra.</summary>
    public string? Passphrase { get; init; }
}

/// <summary>Resultado de <see cref="OperationIds.PgpDecrypt"/>.</summary>
public sealed class PgpDecryptResult
{
    /// <summary>Texto descifrado.</summary>
    public string Text { get; init; } = string.Empty;
}
