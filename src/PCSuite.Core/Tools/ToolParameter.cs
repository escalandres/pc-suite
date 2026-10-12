using System.Text.Json;

namespace PCSuite.Core.Tools;

/// <summary>Tipo de control que la UI renderiza para un parámetro de tool.</summary>
public enum ToolParameterType
{
    /// <summary>Texto de una línea.</summary>
    Text,

    /// <summary>Texto multilínea (JSON, claves, contenido largo).</summary>
    TextArea,

    /// <summary>Número entero.</summary>
    Number,

    /// <summary>Checkbox true/false.</summary>
    Boolean,

    /// <summary>Lista fija de valores (Options).</summary>
    Select,
}

/// <summary>
/// Parámetro declarado por una tool: la UI genera el formulario de
/// configuración a partir de esta lista, sin hardcodear ninguna tool.
/// </summary>
public sealed class ToolParameter
{
    /// <summary>Clave del payload JSON que rellena este parámetro.</summary>
    public required string Name { get; init; }

    /// <summary>Etiqueta visible del campo.</summary>
    public required string Label { get; init; }

    public required ToolParameterType Type { get; init; }

    /// <summary>El campo debe venir relleno para poder ejecutar.</summary>
    public bool Required { get; init; }

    /// <summary>Valor inicial del campo (misma forma que el payload).</summary>
    public JsonElement? Default { get; init; }

    /// <summary>Placeholder del input (Text/TextArea).</summary>
    public string? Placeholder { get; init; }

    /// <summary>Ayuda corta bajo el campo.</summary>
    public string? HelpText { get; init; }

    /// <summary>Valores admitidos (solo Select).</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>Límite inferior (Number).</summary>
    public int? Min { get; init; }

    /// <summary>Límite superior (Number).</summary>
    public int? Max { get; init; }
}
