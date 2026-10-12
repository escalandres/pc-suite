using System.Text.Json;

namespace PCSuite.Core.Tools;

/// <summary>
/// Metadatos con los que el frontend descubre y busca tools sin tener
/// ninguna tool hardcodeada en la UI.
/// </summary>
public sealed class ToolMetadata
{
    /// <summary>Id estable y único, p. ej. "json.format".</summary>
    public required string Id { get; init; }

    public required string ModuleId { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Ejemplo de payload de entrada para la UI genérica.</summary>
    public JsonElement? ExampleInput { get; init; }

    /// <summary>
    /// Parámetros de configuración de la tool: la UI genera el formulario
    /// de ejecución a partir de esta lista (app-like). Vacía = la UI cae al
    /// editor JSON genérico.
    /// </summary>
    public IReadOnlyList<ToolParameter> Parameters { get; init; } = [];
}
