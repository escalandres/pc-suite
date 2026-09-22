using System.Text.Json;
using System.Text;
using PCSuite.Contracts;

namespace PCSuite.Application.Tests;

/// <summary>Forma del protocolo IPC de elevación: nonce, parseo validado y framing con tope de 1 MiB.</summary>
public sealed class ElevatedProtocolTests
{
    [Fact]
    public void NewNonce_EsBase64UrlDe32Bytes_YNoSeRepite()
    {
        var first = ElevatedProtocol.NewNonce();
        var second = ElevatedProtocol.NewNonce();

        Assert.Equal(43, first.Length);
        Assert.DoesNotContain("=", first);
        Assert.DoesNotContain("+", first);
        Assert.DoesNotContain("/", first);
        Assert.NotEqual(first, second);
        Assert.All(first, c => Assert.True(char.IsLetterOrDigit(c) || c is '-' or '_'));

        // Decodifica de vuelta a los 32 bytes aleatorios originales.
        var base64 = first.Replace('-', '+').Replace('_', '/') + "=";
        Assert.Equal(32, Convert.FromBase64String(base64).Length);
    }

    [Fact]
    public void SerializeRequest_Y_TryParseRequest_RoundTrip_ConPayload()
    {
        using var payload = JsonDocument.Parse("""{"includeTokenGroups":true}""");
        var request = new ElevatedRequest
        {
            Nonce = "abc-123",
            Operation = ElevatedOperationIds.AdminInfo,
            Payload = payload.RootElement,
        };

        var json = ElevatedProtocol.SerializeRequest(request);

        Assert.True(ElevatedProtocol.TryParseRequest(json, out var parsed, out var error));
        Assert.Null(error);
        Assert.Equal("abc-123", parsed!.Nonce);
        Assert.Equal(ElevatedOperationIds.AdminInfo, parsed.Operation);
        Assert.True(parsed.Payload!.Value.GetProperty("includeTokenGroups").GetBoolean());
    }

    [Fact]
    public void SerializeRequest_SinPayload_OmiteLaPropiedad()
    {
        var json = ElevatedProtocol.SerializeRequest(
            new ElevatedRequest { Nonce = "n", Operation = ElevatedOperationIds.AdminInfo });

        Assert.DoesNotContain("payload", json);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("42")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void TryParseRequest_MensajesSinForma_ReturnFalse_Malformed(string message)
    {
        Assert.False(ElevatedProtocol.TryParseRequest(message, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, error!.Code);
    }

    [Fact]
    public void TryParseRequest_SinNonce_ReturnFalse_Unauthorized()
    {
        Assert.False(
            ElevatedProtocol.TryParseRequest("""{"operation":"elevated.adminInfo"}""", out _, out var error));
        Assert.Equal(BridgeErrorCodes.Unauthorized, error!.Code);
    }

    [Fact]
    public void TryParseRequest_SinOperacion_ReturnFalse_Malformed()
    {
        Assert.False(ElevatedProtocol.TryParseRequest("""{"nonce":"n"}""", out _, out var error));
        Assert.Equal(BridgeErrorCodes.MalformedMessage, error!.Code);
    }

    [Fact]
    public void TryParseResponse_Exitosa_Y_DeError_RoundTrip()
    {
        var ok = ElevatedProtocol.SerializeResponse(ElevatedResponse.Ok());
        Assert.True(ElevatedProtocol.TryParseResponse(ok, out var okParsed, out _));
        Assert.True(okParsed!.Success);

        var failure = ElevatedProtocol.SerializeResponse(
            ElevatedResponse.Fail(BridgeErrorCodes.Unauthorized, "Token de sesión no válido."));
        Assert.True(ElevatedProtocol.TryParseResponse(failure, out var failParsed, out _));
        Assert.False(failParsed!.Success);
        Assert.Equal(BridgeErrorCodes.Unauthorized, failParsed.Error!.Code);
        Assert.Equal("Token de sesión no válido.", failParsed.Error!.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("basura")]
    [InlineData("{}")]
    [InlineData("""{"success":false}""")]
    [InlineData("""{"success":false,"error":{}}""")]
    [InlineData("""{"success":false,"error":{"code":"X"}}""")]
    public void TryParseResponse_SinSuccessNiErrorValido_ReturnFalse_Malformed(string message)
    {
        Assert.False(ElevatedProtocol.TryParseResponse(message, out _, out var error));
        Assert.Equal(BridgeErrorCodes.MalformedMessage, error!.Code);
    }

    [Fact]
    public async Task WriteYReadMessage_RoundTrip()
    {
        using var stream = new MemoryStream();
        await ElevatedProtocol.WriteMessageAsync(stream, """{"hola":"mundo"}""", CancellationToken.None);

        stream.Position = 0;
        Assert.Equal(
            """{"hola":"mundo"}""",
            await ElevatedProtocol.ReadMessageAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadMessageAsync_EofSinDatos_ReturnNull()
    {
        using var stream = new MemoryStream();
        Assert.Null(await ElevatedProtocol.ReadMessageAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadMessageAsync_EofSinNewline_ReturnParcial()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sin-newline"));
        Assert.Equal("sin-newline", await ElevatedProtocol.ReadMessageAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadMessageAsync_MayorQueElTope_LanzaInvalidData_SinParsear()
    {
        var oversized = new byte[ElevatedProtocol.MaxMessageBytes + 1];
        Array.Fill(oversized, (byte)'a');

        using var stream = new MemoryStream(oversized);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => ElevatedProtocol.ReadMessageAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task WriteMessageAsync_MayorQueElTope_LanzaInvalidData()
    {
        using var stream = new MemoryStream();
        var oversized = new string('a', ElevatedProtocol.MaxMessageBytes + 1);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ElevatedProtocol.WriteMessageAsync(stream, oversized, CancellationToken.None));
    }
}
