namespace PCSuite.Contracts;

/// <summary>
/// Payload de <see cref="ElevatedOperationIds.AdminInfo"/>: solo lectura,
/// sin side effects.
/// </summary>
public sealed class AdminInfoPayload
{
    /// <summary>
    /// Si es true, añade los grupos/SID del token elevado (prueba visible
    /// de que UAC se aceptó). Por defecto no se piden.
    /// </summary>
    public bool IncludeTokenGroups { get; init; }
}
