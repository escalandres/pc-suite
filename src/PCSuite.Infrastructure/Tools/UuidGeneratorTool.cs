using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Tools;

namespace PCSuite.Infrastructure.Tools;

/// <summary>Genera UUID v4 (aleatorio) o v7 (ordenable por tiempo).</summary>
public sealed class UuidGeneratorTool : ITool
{
    private const int MaxCount = 100;

    public ToolMetadata Metadata { get; } = new()
    {
        Id = "uuid.generate",
        ModuleId = ToolsModule.IdValue,
        Name = "UUID Generator",
        Description = "Genera identificadores UUID v4 o v7 (ordenable por tiempo).",
        Keywords = ["uuid", "guid", "generar", "random", "v4", "v7", "id"],
        ExampleInput = BridgeJson.ToJson(new { count = 3 }),
        Parameters =
        [
            new ToolParameter
            {
                Name = "count",
                Label = "Cantidad",
                Type = ToolParameterType.Number,
                Default = BridgeJson.ToJson(1),
                Min = 1,
                Max = MaxCount,
                HelpText = "UUIDs a generar (1-100).",
            },
            new ToolParameter
            {
                Name = "version",
                Label = "Versión",
                Type = ToolParameterType.Select,
                Default = BridgeJson.ToJson("v4"),
                Options = ["v4", "v7"],
                HelpText = "v4: aleatorio clásico · v7: ordenable (marca de tiempo Unix).",
            },
        ],
    };

    public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var count = 1;
        var version = "v4";
        if (context.Input is { ValueKind: JsonValueKind.Object } input)
        {
            if (input.TryGetProperty("count", out var countProperty))
            {
                if (countProperty.ValueKind != JsonValueKind.Number || !countProperty.TryGetInt32(out count))
                {
                    return Task.FromResult(ToolResult.Fail("El campo 'count' debe ser un número entero."));
                }

                if (count < 1 || count > MaxCount)
                {
                    return Task.FromResult(ToolResult.Fail($"El campo 'count' debe estar entre 1 y {MaxCount}."));
                }
            }

            if (input.TryGetProperty("version", out var versionProperty))
            {
                if (versionProperty.ValueKind != JsonValueKind.String)
                {
                    return Task.FromResult(ToolResult.Fail("El campo 'version' debe ser \"v4\" o \"v7\"."));
                }

                version = versionProperty.GetString() ?? "";
                if (version is not ("v4" or "v7"))
                {
                    return Task.FromResult(ToolResult.Fail("El campo 'version' debe ser \"v4\" o \"v7\"."));
                }
            }
        }

        var uuids = new string[count];
        for (var i = 0; i < count; i++)
        {
            // v7 (.NET 9+): Unix epoch ms + aleatorio — ordenable en el
            // tiempo, ideal para claves de BD. v4: aleatorio clásico.
            uuids[i] = (version == "v7" ? Guid.CreateVersion7() : Guid.NewGuid()).ToString("D");
        }

        return Task.FromResult(ToolResult.Ok(BridgeJson.ToJson(uuids)));
    }
}
