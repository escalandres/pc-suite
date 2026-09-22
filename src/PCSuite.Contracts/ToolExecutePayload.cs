using System.Text.Json;

namespace PCSuite.Contracts;

/// <summary>Payload de tool.execute.</summary>
public sealed class ToolExecutePayload
{
    /// <summary>Id de la tool del registry; jamás es un comando arbitrario.</summary>
    public string? ToolId { get; init; }

    /// <summary>Entrada específica de cada tool, validada por la propia tool.</summary>
    public JsonElement? Input { get; init; }
}
