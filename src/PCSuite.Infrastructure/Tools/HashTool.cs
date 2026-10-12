using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Tools;

namespace PCSuite.Infrastructure.Tools;

/// <summary>Calcula hashes de texto (MD5/SHA-1/SHA-256/SHA-512).</summary>
public sealed class HashTool : ITool
{
    public ToolMetadata Metadata { get; } = new()
    {
        Id = "hash",
        ModuleId = ToolsModule.IdValue,
        Name = "Hash",
        Description = "Calcula el hash MD5, SHA-1, SHA-256 o SHA-512 de un texto (UTF-8).",
        Keywords = ["hash", "sha256", "sha1", "sha512", "md5", "checksum", "digest"],
        ExampleInput = BridgeJson.ToJson(new { algorithm = "sha256", text = "Hola PCSuite" }),
        Parameters =
        [
            new ToolParameter
            {
                Name = "algorithm",
                Label = "Algoritmo",
                Type = ToolParameterType.Select,
                Default = BridgeJson.ToJson("sha256"),
                Options = ["md5", "sha1", "sha256", "sha512"],
            },
            new ToolParameter
            {
                Name = "text",
                Label = "Texto",
                Type = ToolParameterType.TextArea,
                Required = true,
                Placeholder = "Hola PCSuite",
                HelpText = "Texto (UTF-8) cuyo hash se calcula.",
            },
            new ToolParameter
            {
                Name = "uppercase",
                Label = "Mayúsculas",
                Type = ToolParameterType.Boolean,
                Default = BridgeJson.ToJson(false),
                HelpText = "Hash en hex mayúsculas.",
            },
        ],
    };

    public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        if (context.Input is not { ValueKind: JsonValueKind.Object } input)
        {
            return Task.FromResult(ToolResult.Fail("Se esperaba un objeto con 'algorithm' y 'text'."));
        }

        if (!input.TryGetProperty("algorithm", out var algorithmProperty)
            || algorithmProperty.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(ToolResult.Fail("El campo 'algorithm' debe ser md5, sha1, sha256 o sha512."));
        }

        if (!input.TryGetProperty("text", out var textProperty)
            || textProperty.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(ToolResult.Fail("El campo 'text' debe ser una cadena."));
        }

        var algorithmName = algorithmProperty.GetString() ?? "";
        var algorithm = algorithmName.ToLowerInvariant() switch
        {
            "md5" => HashAlgorithmName.MD5,
            "sha1" => HashAlgorithmName.SHA1,
            "sha256" => HashAlgorithmName.SHA256,
            "sha512" => HashAlgorithmName.SHA512,
            _ => (HashAlgorithmName?)null,
        };

        if (algorithm is null)
        {
            return Task.FromResult(ToolResult.Fail("Algoritmo no soportado. Usa md5, sha1, sha256 o sha512."));
        }

        var uppercase = false;
        if (input.TryGetProperty("uppercase", out var uppercaseProperty))
        {
            if (uppercaseProperty.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return Task.FromResult(ToolResult.Fail("El campo 'uppercase' debe ser true o false."));
            }

            uppercase = uppercaseProperty.GetBoolean();
        }

        var bytes = Encoding.UTF8.GetBytes(textProperty.GetString() ?? "");
        using var incremental = IncrementalHash.CreateHash(algorithm.Value);
        incremental.AppendData(bytes);
        var hash = Convert.ToHexStringLower(incremental.GetHashAndReset());
        if (uppercase)
        {
            hash = hash.ToUpperInvariant();
        }

        return Task.FromResult(ToolResult.Ok(BridgeJson.ToJson(hash)));
    }
}
