using System.Text;
using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Tools;

namespace PCSuite.Infrastructure.Tools;

/// <summary>Codifica y descodifica Base64 (UTF-8 estricto).</summary>
public sealed class Base64Tool : ITool
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public ToolMetadata Metadata { get; } = new()
    {
        Id = "base64",
        ModuleId = ToolsModule.IdValue,
        Name = "Base64",
        Description = "Codifica texto a Base64 o lo descodifica de vuelta a texto UTF-8.",
        Keywords = ["base64", "encode", "decode", "codificar", "decodificar", "texto"],
        ExampleInput = BridgeJson.ToJson(new { operation = "encode", text = "Hola PCSuite" }),
        Parameters =
        [
            new ToolParameter
            {
                Name = "operation",
                Label = "Operación",
                Type = ToolParameterType.Select,
                Default = BridgeJson.ToJson("encode"),
                Options = ["encode", "decode"],
            },
            new ToolParameter
            {
                Name = "text",
                Label = "Texto",
                Type = ToolParameterType.TextArea,
                Required = true,
                Placeholder = "Hola PCSuite",
                HelpText = "Texto a codificar, o Base64 a descodificar.",
            },
        ],
    };

    public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        if (context.Input is not { ValueKind: JsonValueKind.Object } input)
        {
            return Task.FromResult(ToolResult.Fail("Se esperaba un objeto con 'operation' y 'text'."));
        }

        if (!input.TryGetProperty("operation", out var operationProperty)
            || operationProperty.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(ToolResult.Fail("El campo 'operation' debe ser \"encode\" o \"decode\"."));
        }

        if (!input.TryGetProperty("text", out var textProperty)
            || textProperty.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(ToolResult.Fail("El campo 'text' debe ser una cadena."));
        }

        var operation = operationProperty.GetString();
        var text = textProperty.GetString() ?? "";

        try
        {
            switch (operation)
            {
                case "encode":
                {
                    var encoded = Convert.ToBase64String(StrictUtf8.GetBytes(text));
                    return Task.FromResult(ToolResult.Ok(BridgeJson.ToJson(encoded)));
                }
                case "decode":
                {
                    var bytes = Convert.FromBase64String(text);
                    var decoded = StrictUtf8.GetString(bytes);
                    return Task.FromResult(ToolResult.Ok(BridgeJson.ToJson(decoded)));
                }
                default:
                    return Task.FromResult(ToolResult.Fail("El campo 'operation' debe ser \"encode\" o \"decode\"."));
            }
        }
        catch (FormatException)
        {
            return Task.FromResult(ToolResult.Fail("El texto no es Base64 válido."));
        }
        catch (DecoderFallbackException)
        {
            return Task.FromResult(ToolResult.Fail("Los bytes decodificados no son UTF-8 válido."));
        }
    }
}
