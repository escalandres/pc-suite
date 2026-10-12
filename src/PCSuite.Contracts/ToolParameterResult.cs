using System.Text.Json;

namespace PCSuite.Contracts;

/// <summary>Parámetro de una tool expuesto al frontend para renderizar el formulario.</summary>
public sealed class ToolParameterResult
{
    public string Name { get; init; } = "";

    public string Label { get; init; } = "";

    /// <summary>text | textArea | number | boolean | select.</summary>
    public string Type { get; init; } = "";

    public bool Required { get; init; }

    public JsonElement? Default { get; init; }

    public string? Placeholder { get; init; }

    public string? HelpText { get; init; }

    public IReadOnlyList<string> Options { get; init; } = [];

    public int? Min { get; init; }

    public int? Max { get; init; }
}
