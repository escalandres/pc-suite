namespace PCSuite.Contracts;

/// <summary>Resultado de <see cref="OperationIds.PgpListKeys"/> (Task 009 / §6).</summary>
public sealed class PgpListKeysResult
{
    /// <summary>Claves del keyring (públicas y privadas, más recientes primero).</summary>
    public PgpKeyInfoResult[] Keys { get; init; } = [];
}
