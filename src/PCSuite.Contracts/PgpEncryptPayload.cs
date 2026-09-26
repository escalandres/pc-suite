namespace PCSuite.Contracts;

/// <summary>Payload de <see cref="OperationIds.PgpEncrypt"/> (Task 009 / §6).</summary>
public sealed class PgpEncryptPayload
{
    /// <summary>Texto a cifrar (acotado en el servicio).</summary>
    public string? Text { get; init; }

    /// <summary>Destinatarios por fingerprint (o prefijo) o email exacto.</summary>
    public string[]? Recipients { get; init; }
}

/// <summary>Resultado de <see cref="OperationIds.PgpEncrypt"/>.</summary>
public sealed class PgpEncryptResult
{
    /// <summary>Mensaje cifrado armoured (-----BEGIN PGP MESSAGE-----).</summary>
    public string ArmoredMessage { get; init; } = string.Empty;
}
