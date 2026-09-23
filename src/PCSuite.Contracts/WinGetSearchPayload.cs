namespace PCSuite.Contracts;

/// <summary>Payload de <see cref="OperationIds.WingetSearch"/>.</summary>
public sealed class WinGetSearchPayload
{
    /// <summary>
    /// Texto de búsqueda. Validado en .NET (no vacío, sin controles, sin
    /// guion inicial): el servicio construye después los argumentos fijos.
    /// </summary>
    public string Query { get; init; } = "";
}
