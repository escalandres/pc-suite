namespace PCSuite.Contracts;

/// <summary>Payload de tools.search.</summary>
public sealed class ToolSearchPayload
{
    /// <summary>Texto libre; vacío o ausente devuelve todas las tools.</summary>
    public string? Query { get; init; }
}
