using System.Text.Json;

namespace PCSuite.Contracts;

/// <summary>Metadata expuesta al frontend para descubrir una tool.</summary>
public sealed class ToolInfoResult
{
    public string Id { get; init; } = "";

    public string ModuleId { get; init; } = "";

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Ejemplo de payload de entrada para la UI genérica.</summary>
    public JsonElement? ExampleInput { get; init; }
}
