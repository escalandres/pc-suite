using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Tools;

namespace PCSuite.Infrastructure.Tools;

/// <summary>Genera UUIDs v4 (RFC 4122) con el RNG del sistema.</summary>
public sealed class UuidGeneratorTool : ITool
{
    private const int MaxCount = 100;

    public ToolMetadata Metadata { get; } = new()
    {
        Id = "uuid.generate",
        ModuleId = ToolsModule.IdValue,
        Name = "UUID Generator",
        Description = "Genera identificadores UUID v4 aleatorios.",
        Keywords = ["uuid", "guid", "generar", "random", "v4", "id"],
        ExampleInput = BridgeJson.ToJson(new { count = 3 }),
    };

    public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var count = 1;
        if (context.Input is { ValueKind: JsonValueKind.Object } input
            && input.TryGetProperty("count", out var countProperty))
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

        var uuids = new string[count];
        for (var i = 0; i < count; i++)
        {
            uuids[i] = Guid.NewGuid().ToString("D");
        }

        return Task.FromResult(ToolResult.Ok(BridgeJson.ToJson(new { uuids })));
    }
}
