using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Tools;

namespace PCSuite.Infrastructure.Tools;

/// <summary>
/// Genera contraseñas aleatorias criptográficamente seguras.
/// NUNCA registrar la salida (Rule 5 — Never log passwords).
/// </summary>
public sealed class PasswordGeneratorTool : ITool
{
    private const int MinLength = 8;
    private const int MaxLength = 128;

    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{}:;.?/";

    public ToolMetadata Metadata { get; } = new()
    {
        Id = "password.generate",
        ModuleId = ToolsModule.IdValue,
        Name = "Password Generator",
        Description = "Genera una contraseña aleatoria segura eligiendo longitud y tipos de caracteres.",
        Keywords = ["password", "contraseña", "generar", "clave", "random", "segura"],
        ExampleInput = BridgeJson.ToJson(new
        {
            length = 16,
            upper = true,
            lower = true,
            digits = true,
            symbols = true,
        }),
        Parameters =
        [
            new ToolParameter
            {
                Name = "length",
                Label = "Longitud",
                Type = ToolParameterType.Number,
                Default = BridgeJson.ToJson(16),
                Min = MinLength,
                Max = MaxLength,
                HelpText = $"Caracteres de la contraseña ({MinLength}-{MaxLength}).",
            },
            new ToolParameter
            {
                Name = "upper",
                Label = "Mayúsculas (A-Z)",
                Type = ToolParameterType.Boolean,
                Default = BridgeJson.ToJson(true),
            },
            new ToolParameter
            {
                Name = "lower",
                Label = "Minúsculas (a-z)",
                Type = ToolParameterType.Boolean,
                Default = BridgeJson.ToJson(true),
            },
            new ToolParameter
            {
                Name = "digits",
                Label = "Números (0-9)",
                Type = ToolParameterType.Boolean,
                Default = BridgeJson.ToJson(true),
            },
            new ToolParameter
            {
                Name = "symbols",
                Label = "Símbolos (!@#$…)",
                Type = ToolParameterType.Boolean,
                Default = BridgeJson.ToJson(true),
            },
        ],
    };

    public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        if (context.Input is not { ValueKind: JsonValueKind.Object } input)
        {
            return Task.FromResult(ToolResult.Fail("Se esperaba un objeto con las opciones de la contraseña."));
        }

        var length = 16;
        if (input.TryGetProperty("length", out var lengthProperty))
        {
            if (lengthProperty.ValueKind != JsonValueKind.Number || !lengthProperty.TryGetInt32(out length))
            {
                return Task.FromResult(ToolResult.Fail("El campo 'length' debe ser un número entero."));
            }

            if (length < MinLength || length > MaxLength)
            {
                return Task.FromResult(
                    ToolResult.Fail($"El campo 'length' debe estar entre {MinLength} y {MaxLength}."));
            }
        }

        var useUpper = ReadFlag(input, "upper", true);
        var useLower = ReadFlag(input, "lower", true);
        var useDigits = ReadFlag(input, "digits", true);
        var useSymbols = ReadFlag(input, "symbols", true);
        if (useUpper is null || useLower is null || useDigits is null || useSymbols is null)
        {
            return Task.FromResult(ToolResult.Fail("Los campos upper, lower, digits y symbols deben ser true o false."));
        }

        var groups = new List<string>(4);
        if (useUpper.Value)
        {
            groups.Add(Upper);
        }

        if (useLower.Value)
        {
            groups.Add(Lower);
        }

        if (useDigits.Value)
        {
            groups.Add(Digits);
        }

        if (useSymbols.Value)
        {
            groups.Add(Symbols);
        }

        if (groups.Count == 0)
        {
            return Task.FromResult(ToolResult.Fail("Selecciona al menos un tipo de caracteres."));
        }

        var pool = string.Concat(groups);
        var characters = Generate(length, groups, pool);

        // La salida es la contraseña a pelo (nunca se registra en logs).
        return Task.FromResult(ToolResult.Ok(BridgeJson.ToJson(new string(characters))));
    }

    private static bool? ReadFlag(JsonElement input, string name, bool defaultValue)
    {
        if (!input.TryGetProperty(name, out var property))
        {
            return defaultValue;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>
    /// Garantiza al menos un carácter de cada grupo seleccionado:
    /// uno por grupo, el resto del pool global, y barajado Fisher-Yates.
    /// </summary>
    private static char[] Generate(int length, List<string> groups, string pool)
    {
        var characters = new char[length];

        for (var i = 0; i < groups.Count; i++)
        {
            characters[i] = groups[i][RandomNumberGenerator.GetInt32(groups[i].Length)];
        }

        for (var i = groups.Count; i < length; i++)
        {
            characters[i] = pool[RandomNumberGenerator.GetInt32(pool.Length)];
        }

        for (var i = characters.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }

        return characters;
    }
}
