using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PCSuite.Core.Tools;
using PCSuite.Infrastructure.Tools;

namespace PCSuite.Application.Tests;

/// <summary>
/// Rule 7 — Test security-sensitive code: cada tool se prueba con entradas
/// válidas e inválidas (el error siempre es un mensaje controlado, sin stack).
/// </summary>
public class ToolImplementationsTests
{
    // --- JSON Formatter ---

    [Theory]
    [InlineData("{\"a\":1}", "format")]
    [InlineData("{\"a\":1}", "minify")]
    public async Task JsonFormatter_ValidInput_Succeeds(string text, string mode)
    {
        var result = await ExecuteAsync(new JsonFormatterTool(), new { text, mode });

        Assert.True(result.Success);
        var json = result.Output!.Value.GetProperty("json").GetString()!;
        Assert.Equal(mode == "format", json.Contains('\n'));
        Assert.NotNull(JsonDocument.Parse(json));
    }

    [Fact]
    public async Task JsonFormatter_InvalidJson_FailsWithControlledMessage()
    {
        var result = await ExecuteAsync(new JsonFormatterTool(), new { text = "{no es json" });

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("JSON inválido", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task JsonFormatter_MissingText_Fails()
    {
        var result = await ExecuteAsync(new JsonFormatterTool(), new { mode = "format" });

        Assert.False(result.Success);
        Assert.Contains("'text'", result.ErrorMessage);
    }

    [Fact]
    public async Task JsonFormatter_UnknownMode_Fails()
    {
        var result = await ExecuteAsync(new JsonFormatterTool(), new { text = "{}", mode = "xml" });

        Assert.False(result.Success);
        Assert.Contains("'mode'", result.ErrorMessage);
    }

    // --- UUID Generator ---

    [Fact]
    public async Task UuidGenerator_Default_ReturnsSingleV4Uuid()
    {
        var result = await ExecuteAsync(new UuidGeneratorTool(), new { });

        Assert.True(result.Success);
        var uuids = result.Output!.Value.GetProperty("uuids").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToArray();
        var uuid = Assert.Single(uuids);
        AssertV4(uuid);
    }

    [Fact]
    public async Task UuidGenerator_Count_ReturnsThatManyUniqueV4()
    {
        var result = await ExecuteAsync(new UuidGeneratorTool(), new { count = 25 });

        Assert.True(result.Success);
        var uuids = result.Output!.Value.GetProperty("uuids").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToArray();
        Assert.Equal(25, uuids.Length);
        Assert.Equal(25, uuids.Distinct().Count());
        Assert.All(uuids, AssertV4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task UuidGenerator_OutOfRangeCount_Fails(int count)
    {
        var result = await ExecuteAsync(new UuidGeneratorTool(), new { count });

        Assert.False(result.Success);
        Assert.Contains("entre 1 y 100", result.ErrorMessage);
    }

    [Fact]
    public async Task UuidGenerator_NonNumericCount_Fails()
    {
        var result = await ExecuteAsync(new UuidGeneratorTool(), new { count = "tres" });

        Assert.False(result.Success);
        Assert.Contains("entero", result.ErrorMessage);
    }

    // --- Base64 ---

    [Fact]
    public async Task Base64_EncodeDecode_RoundTrips()
    {
        const string original = "Hola PCSuite — acentos: ñáéíóú";

        var encoded = await ExecuteAsync(new Base64Tool(), new { operation = "encode", text = original });
        Assert.True(encoded.Success);
        var value = encoded.Output!.Value.GetProperty("result").GetString()!;
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes(original)), value);

        var decoded = await ExecuteAsync(new Base64Tool(), new { operation = "decode", text = value });
        Assert.True(decoded.Success);
        Assert.Equal(original, decoded.Output!.Value.GetProperty("result").GetString());
    }

    [Fact]
    public async Task Base64_DecodeInvalid_FailsWithControlledMessage()
    {
        var result = await ExecuteAsync(new Base64Tool(), new { operation = "decode", text = "@@no-base64@@" });

        Assert.False(result.Success);
        Assert.Contains("Base64 válido", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task Base64_DecodeNonUtf8Bytes_Fails()
    {
        // 0xFF 0xFE 0xFF es Base64 válido pero bytes no UTF-8: "/P7/".
        var result = await ExecuteAsync(new Base64Tool(), new { operation = "decode", text = Convert.ToBase64String([0xFF, 0xFE, 0xFF]) });

        Assert.False(result.Success);
        Assert.Contains("UTF-8", result.ErrorMessage);
    }

    [Theory]
    [InlineData("cipherto")]
    [InlineData("")]
    public async Task Base64_UnknownOperation_Fails(string operation)
    {
        var result = await ExecuteAsync(new Base64Tool(), new { operation, text = "hola" });

        Assert.False(result.Success);
        Assert.Contains("'operation'", result.ErrorMessage);
    }

    // --- Hash ---

    [Theory]
    [InlineData("sha256", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("sha1", "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData("md5", "900150983cd24fb0d6963f7d28e17f72")]
    public async Task Hash_KnownVectorOfAbc_Matches(string algorithm, string expected)
    {
        var result = await ExecuteAsync(new HashTool(), new { algorithm, text = "abc" });

        Assert.True(result.Success);
        Assert.Equal(expected, result.Output!.Value.GetProperty("hash").GetString());
    }

    [Fact]
    public async Task Hash_UppercaseOption_ReturnsUpperCase()
    {
        var result = await ExecuteAsync(
            new HashTool(),
            new { algorithm = "sha256", text = "abc", uppercase = true });

        Assert.True(result.Success);
        var hash = result.Output!.Value.GetProperty("hash").GetString()!;
        Assert.Equal(hash.ToUpperInvariant(), hash);
    }

    [Fact]
    public async Task Hash_UnsupportedAlgorithm_Fails()
    {
        var result = await ExecuteAsync(new HashTool(), new { algorithm = "crc32", text = "abc" });

        Assert.False(result.Success);
        Assert.Contains("md5, sha1, sha256 o sha512", result.ErrorMessage);
    }

    [Fact]
    public async Task Hash_MissingText_Fails()
    {
        var result = await ExecuteAsync(new HashTool(), new { algorithm = "sha256" });

        Assert.False(result.Success);
        Assert.Contains("'text'", result.ErrorMessage);
    }

    // --- Password Generator ---

    [Fact]
    public async Task PasswordGenerator_Default_RespectsLengthAndCharacterClasses()
    {
        var result = await ExecuteAsync(new PasswordGeneratorTool(), new { length = 24 });

        Assert.True(result.Success);
        var password = result.Output!.Value.GetProperty("password").GetString()!;
        Assert.Equal(24, password.Length);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, c => !char.IsLetterOrDigit(c));
        Assert.True(result.Output!.Value.GetProperty("entropyBits").GetDouble() > 0);
    }

    [Fact]
    public async Task PasswordGenerator_SelectedClasses_OnlyUseThose()
    {
        var result = await ExecuteAsync(
            new PasswordGeneratorTool(),
            new { length = 32, upper = false, lower = false, digits = true, symbols = false });

        Assert.True(result.Success);
        var password = result.Output!.Value.GetProperty("password").GetString()!;
        Assert.All(password, c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public async Task PasswordGenerator_NoClasses_Fails()
    {
        var result = await ExecuteAsync(
            new PasswordGeneratorTool(),
            new { upper = false, lower = false, digits = false, symbols = false });

        Assert.False(result.Success);
        Assert.Contains("al menos un tipo", result.ErrorMessage);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(129)]
    public async Task PasswordGenerator_OutOfRangeLength_Fails(int length)
    {
        var result = await ExecuteAsync(new PasswordGeneratorTool(), new { length });

        Assert.False(result.Success);
        Assert.Contains("entre 8 y 128", result.ErrorMessage);
    }

    [Fact]
    public async Task PasswordGenerator_NonBooleanFlag_Fails()
    {
        var result = await ExecuteAsync(new PasswordGeneratorTool(), new { upper = "yes" });

        Assert.False(result.Success);
        Assert.Contains("upper, lower, digits y symbols", result.ErrorMessage);
    }

    [Fact]
    public async Task PasswordGenerator_NonObjectPayload_Fails()
    {
        var result = await ExecuteAsync(new PasswordGeneratorTool(), null);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // --- helpers ---

    private static async Task<ToolResult> ExecuteAsync(ITool tool, object? input)
    {
        var context = new ToolContext
        {
            Input = input is null ? null : JsonSerializer.SerializeToElement(input),
        };

        return await tool.ExecuteAsync(context, CancellationToken.None);
    }

    private static void AssertV4(string uuid)
    {
        Assert.True(Guid.TryParse(uuid, out var parsed), $"'{uuid}' no es un GUID válido.");
        Assert.Equal(4, parsed.Version);
    }
}
