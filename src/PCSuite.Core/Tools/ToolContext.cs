using System.Text.Json;

namespace PCSuite.Core.Tools;

/// <summary>Contexto de ejecución de una tool: su payload de entrada.</summary>
public sealed class ToolContext
{
    public JsonElement? Input { get; init; }
}
