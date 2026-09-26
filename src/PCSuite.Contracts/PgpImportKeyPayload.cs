namespace PCSuite.Contracts;

/// <summary>
/// Payload de <see cref="OperationIds.PgpImportKey"/> (Task 009 / §6).
/// El resultado es la <see cref="PgpKeyInfoResult"/> de la clave importada
/// (HasPrivate indica si era una clave privada).
/// </summary>
public sealed class PgpImportKeyPayload
{
    /// <summary>Clave armoured (BLOCK PUBLIC/PRIVATE KEY BLOCK); acotada en el servicio.</summary>
    public string? ArmoredKey { get; init; }
}
