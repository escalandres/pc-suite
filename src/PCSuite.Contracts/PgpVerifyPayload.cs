namespace PCSuite.Contracts;

/// <summary>Payload de <see cref="OperationIds.PgpVerify"/> (Task 009 / §6).</summary>
public sealed class PgpVerifyPayload
{
    /// <summary>Texto original que se firmó (acotado en el servicio).</summary>
    public string? Text { get; init; }

    /// <summary>Firma armoured (acotada en el servicio).</summary>
    public string? Signature { get; init; }
}

/// <summary>
/// Resultado de <see cref="OperationIds.PgpVerify"/>: una firma no válida es
/// un resultado legítimo (Valid = false con Detail), no un error del puente.
/// </summary>
public sealed class PgpVerifyResult
{
    /// <summary>True si la firma corresponde al texto y es legible.</summary>
    public bool Valid { get; init; }

    /// <summary>Motivo cuando Valid es false; se omite con éxito.</summary>
    public string? Detail { get; init; }

    /// <summary>Clave firmante cuando pudo identificarse.</summary>
    public PgpKeyInfoResult? Signer { get; init; }
}
