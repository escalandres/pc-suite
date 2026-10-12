using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Tools;

namespace PCSuite.Infrastructure.Operations;

/// <summary>Mapea metadatos del Core al DTO expuesto por el puente.</summary>
internal static class ToolMapper
{
    public static ToolCollectionResult ToCollection(IEnumerable<ITool> tools) =>
        new() { Tools = tools.Select(tool => ToInfo(tool.Metadata)).ToList() };

    public static ToolInfoResult ToInfo(ToolMetadata metadata) =>
        new()
        {
            Id = metadata.Id,
            ModuleId = metadata.ModuleId,
            Name = metadata.Name,
            Description = metadata.Description,
            Keywords = metadata.Keywords,
            ExampleInput = metadata.ExampleInput,
            Parameters = metadata.Parameters.Select(ToParameter).ToList(),
        };

    /// <summary>Enum de Core → string del contrato (el bridge no usa convertidores de enum).</summary>
    private static ToolParameterResult ToParameter(ToolParameter parameter) =>
        new()
        {
            Name = parameter.Name,
            Label = parameter.Label,
            Type = parameter.Type switch
            {
                ToolParameterType.Text => "text",
                ToolParameterType.TextArea => "textArea",
                ToolParameterType.Number => "number",
                ToolParameterType.Boolean => "boolean",
                ToolParameterType.Select => "select",
                _ => "text",
            },
            Required = parameter.Required,
            Default = parameter.Default,
            Placeholder = parameter.Placeholder,
            HelpText = parameter.HelpText,
            Options = parameter.Options,
            Min = parameter.Min,
            Max = parameter.Max,
        };

    /// <summary>
    /// Deserializa el payload de una operación de tools. Devuelve false si
    /// no es JSON válido (el caller responde con MALFORMED_MESSAGE).
    /// </summary>
    public static bool TryParsePayload<TPayload>(JsonElement? payload, out TPayload result)
        where TPayload : class
    {
        result = null!;

        if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
        {
            // Payload ausente: se permite para operaciones con campos opcionales.
            result = Activator.CreateInstance<TPayload>();
            return true;
        }

        if (payload.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<TPayload>(payload.Value.GetRawText(), BridgeJson.Options);
            if (parsed is null)
            {
                return false;
            }

            result = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
