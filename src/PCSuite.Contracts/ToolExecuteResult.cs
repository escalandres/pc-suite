using System.Text.Json;

namespace PCSuite.Contracts;

/// <summary>Resultado exitoso de tool.execute.</summary>
public sealed class ToolExecuteResult
{
    public string ToolId { get; init; } = "";

    public JsonElement? Output { get; init; }
}
