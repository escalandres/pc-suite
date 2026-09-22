using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Tools;

namespace PCSuite.Infrastructure.Tools;

/// <summary>Formatea (pretty print) o minifica texto JSON.</summary>
public sealed class JsonFormatterTool : ITool
{
    public ToolMetadata Metadata { get; } = new()
    {
        Id = "json.format",
        ModuleId = ToolsModule.IdValue,
        Name = "JSON Formatter",
        Description = "Formatea un JSON con sangría legible o lo minifica. Valida la sintaxis.",
        Keywords = ["json", "format", "pretty", "print", "minify", "compact", "validar"],
        ExampleInput = BridgeJson.ToJson(new
        {
            text = "{\"ok\":true,\"items\":[1,2,3]}",
            mode = "format",
        }),
    };

    public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        if (context.Input is not { ValueKind: JsonValueKind.Object } input)
        {
            return Task.FromResult(ToolResult.Fail("Se esperaba un objeto con el campo 'text'."));
        }

        if (!input.TryGetProperty("text", out var textProperty)
            || textProperty.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(ToolResult.Fail("El campo 'text' debe ser una cadena con el JSON a procesar."));
        }

        var mode = "format";
        if (input.TryGetProperty("mode", out var modeProperty))
        {
            if (modeProperty.ValueKind != JsonValueKind.String)
            {
                return Task.FromResult(ToolResult.Fail("El campo 'mode' debe ser \"format\" o \"minify\"."));
            }

            mode = modeProperty.GetString() ?? "";
            if (mode is not ("format" or "minify"))
            {
                return Task.FromResult(ToolResult.Fail("El campo 'mode' debe ser \"format\" o \"minify\"."));
            }
        }

        try
        {
            using var document = JsonDocument.Parse(
                textProperty.GetString() ?? "",
                new JsonDocumentOptions { MaxDepth = 64 });

            var formatted = JsonSerializer.Serialize(
                document.RootElement,
                new JsonSerializerOptions { WriteIndented = mode == "format" });

            return Task.FromResult(ToolResult.Ok(BridgeJson.ToJson(new { json = formatted })));
        }
        catch (JsonException exception)
        {
            // JsonException.Message es un mensaje controlado (posición/error), sin stack.
            return Task.FromResult(ToolResult.Fail($"JSON inválido: {exception.Message}"));
        }
    }
}
