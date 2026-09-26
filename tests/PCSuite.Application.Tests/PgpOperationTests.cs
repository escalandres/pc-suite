using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.PGP;

namespace PCSuite.Application.Tests;

/// <summary>
/// Las 8 operaciones pgp.* por el puente (Task 009): contrato camelCase,
/// payloads estructurados (no-objeto → MALFORMED_MESSAGE; pgp.listKeys ignora
/// su payload por contrato §6), fallos controlados (PGP_FAILED, nunca
/// INTERNAL_ERROR ni stack traces), la contraseña jamás se refleja en los
/// errores y ninguna operación declara elevación. Todo en carpetas temporales:
/// los tests no tocan %APPDATA% real.
/// </summary>
public sealed class PgpOperationTests
{
    /// <summary>Marcador que jamás debe aparecer en un mensaje de error.</summary>
    private const string MarkerPassphrase = "MARCADOR-SECRETO-QUE-NUNCA-APARECE";

    // ------------------------------------------------------------------
    // Contrato camelCase
    // ------------------------------------------------------------------

    [Fact]
    public async Task GenerateKey_ReturnsKeyInfo_CamelCase()
    {
        using var fx = PgpFixture.Create();
        var payloadJson = JsonSerializer.Serialize(new { name = "Ana Test", email = "ana@test.es" });

        var response = await Send(fx.Router, "g1", OperationIds.PgpGenerateKey, payloadJson);

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.Matches("^[0-9A-F]{40}$", data.GetProperty("fingerprint").GetString());
        Assert.Equal("Ana Test", data.GetProperty("name").GetString());
        Assert.Equal("ana@test.es", data.GetProperty("email").GetString());
        Assert.True(data.GetProperty("hasPrivate").GetBoolean());
        Assert.True(data.GetProperty("canEncrypt").GetBoolean());
    }

    [Fact]
    public async Task ListKeys_EmptyKeyring_ReturnsEmptyKeysArray()
    {
        using var fx = PgpFixture.Create();

        var response = await Send(fx.Router, "l1", OperationIds.PgpListKeys);

        Assert.True(response.Success);
        Assert.Empty(response.Result!.Value.GetProperty("keys").EnumerateArray());
    }

    [Fact]
    public async Task EncryptDecrypt_SignVerify_RoundTripThroughBridge()
    {
        using var fx = PgpFixture.Create();
        var generated = await Send(
            fx.Router,
            "c1",
            OperationIds.PgpGenerateKey,
            JsonSerializer.Serialize(new { name = "Ana Test", email = "ana@test.es", passphrase = "pase" }));
        Assert.True(generated.Success);
        var fingerprint = generated.Result!.Value.GetProperty("fingerprint").GetString();

        var encrypted = await Send(
            fx.Router,
            "c2",
            OperationIds.PgpEncrypt,
            JsonSerializer.Serialize(new { text = "hola mundo", recipients = new[] { fingerprint![..12] } }));
        Assert.True(encrypted.Success);
        var armored = encrypted.Result!.Value.GetProperty("armoredMessage").GetString();

        var decrypted = await Send(
            fx.Router,
            "c3",
            OperationIds.PgpDecrypt,
            JsonSerializer.Serialize(new { armoredMessage = armored, passphrase = "pase" }));
        Assert.True(decrypted.Success);
        Assert.Equal("hola mundo", decrypted.Result!.Value.GetProperty("text").GetString());

        var signed = await Send(
            fx.Router,
            "c4",
            OperationIds.PgpSign,
            JsonSerializer.Serialize(new { text = "hola mundo", fingerprint, passphrase = "pase" }));
        Assert.True(signed.Success);
        var signature = signed.Result!.Value.GetProperty("signature").GetString();

        var verified = await Send(
            fx.Router,
            "c5",
            OperationIds.PgpVerify,
            JsonSerializer.Serialize(new { text = "hola mundo", signature }));
        Assert.True(verified.Success);
        Assert.True(verified.Result!.Value.GetProperty("valid").GetBoolean());
        Assert.Equal(
            fingerprint,
            verified.Result!.Value.GetProperty("signer").GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task Verify_TamperedText_ReturnsSuccessWithValidFalse()
    {
        using var fx = PgpFixture.Create();
        var generated = await Send(
            fx.Router,
            "v1",
            OperationIds.PgpGenerateKey,
            JsonSerializer.Serialize(new { name = "Ana Test", email = "ana@test.es" }));
        var fingerprint = generated.Result!.Value.GetProperty("fingerprint").GetString();

        var signed = await Send(
            fx.Router,
            "v2",
            OperationIds.PgpSign,
            JsonSerializer.Serialize(new { text = "original", fingerprint }));
        var signature = signed.Result!.Value.GetProperty("signature").GetString();

        var response = await Send(
            fx.Router,
            "v3",
            OperationIds.PgpVerify,
            JsonSerializer.Serialize(new { text = "alterado", signature }));

        // Firma inválida = resultado legítimo con success:true (§6), no un error.
        Assert.True(response.Success);
        Assert.False(response.Result!.Value.GetProperty("valid").GetBoolean());
        var detail = response.Result!.Value.GetProperty("detail").GetString();
        Assert.False(string.IsNullOrWhiteSpace(detail));
        Assert.DoesNotContain("   at ", detail);
    }

    // ------------------------------------------------------------------
    // Payload estructurado: no-objeto → MALFORMED_MESSAGE
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(OperationIds.PgpGenerateKey, "\"Ana\"")]
    [InlineData(OperationIds.PgpImportKey, "123")]
    [InlineData(OperationIds.PgpExportKey, "[\"ABC\"]")]
    [InlineData(OperationIds.PgpEncrypt, "\"hola\"")]
    [InlineData(OperationIds.PgpDecrypt, "true")]
    [InlineData(OperationIds.PgpSign, "\"texto\"")]
    [InlineData(OperationIds.PgpVerify, "42")]
    public async Task NonObjectPayload_SurfacesMalformedMessage(string operation, string payloadJson)
    {
        using var fx = PgpFixture.Create();

        var response = await Send(fx.Router, "x1", operation, payloadJson);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, response.Error!.Code);
        Assert.Equal("El payload debe ser un objeto JSON.", response.Error.Message);
    }

    [Fact]
    public async Task ListKeys_WithAnyPayload_StillReturnsKeys()
    {
        using var fx = PgpFixture.Create();
        await Send(
            fx.Router,
            "l0",
            OperationIds.PgpGenerateKey,
            JsonSerializer.Serialize(new { name = "Ana Test", email = "ana@test.es" }));

        // Payload cero (§6): pgp.listKeys ignora lo que venga en él.
        var response = await Send(fx.Router, "l1", OperationIds.PgpListKeys, "\"--all\"");

        Assert.True(response.Success);
        Assert.Single(response.Result!.Value.GetProperty("keys").EnumerateArray());
    }

    // ------------------------------------------------------------------
    // Errores controlados (nunca INTERNAL_ERROR para fallos de dominio)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Encrypt_UnknownRecipient_SurfacesPgpFailed_NoStackTrace()
    {
        using var fx = PgpFixture.Create();
        await Send(
            fx.Router,
            "e0",
            OperationIds.PgpGenerateKey,
            JsonSerializer.Serialize(new { name = "Ana Test", email = "ana@test.es" }));

        var response = await Send(
            fx.Router,
            "e1",
            OperationIds.PgpEncrypt,
            JsonSerializer.Serialize(new { text = "x", recipients = new[] { "nadie@test.es" } }));

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.PgpFailed, response.Error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(response.Error.Message));
        Assert.DoesNotContain("   at ", response.Error.Message);
        Assert.DoesNotContain("nadie@test.es", response.Error.Message); // no refleja la entrada
    }

    [Fact]
    public async Task Decrypt_WrongPassphrase_SurfacesPgpFailed_WithoutEchoingPassphrase()
    {
        using var fx = PgpFixture.Create();
        var generated = await Send(
            fx.Router,
            "d1",
            OperationIds.PgpGenerateKey,
            JsonSerializer.Serialize(new { name = "Ana Test", email = "ana@test.es", passphrase = MarkerPassphrase }));
        var fingerprint = generated.Result!.Value.GetProperty("fingerprint").GetString();
        var encrypted = await Send(
            fx.Router,
            "d2",
            OperationIds.PgpEncrypt,
            JsonSerializer.Serialize(new { text = "hola", recipients = new[] { fingerprint } }));
        var armored = encrypted.Result!.Value.GetProperty("armoredMessage").GetString();

        var response = await Send(
            fx.Router,
            "d3",
            OperationIds.PgpDecrypt,
            JsonSerializer.Serialize(new { armoredMessage = armored, passphrase = "otra-contraseña" }));

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.PgpFailed, response.Error!.Code);
        Assert.DoesNotContain("otra-contraseña", response.Error.Message);
        Assert.DoesNotContain(MarkerPassphrase, response.Error.Message);
        Assert.DoesNotContain("   at ", response.Error.Message);
    }

    [Fact]
    public async Task ExportKey_IncludePrivate_ReportsIsPrivateTrue()
    {
        using var fx = PgpFixture.Create();
        var generated = await Send(
            fx.Router,
            "p1",
            OperationIds.PgpGenerateKey,
            JsonSerializer.Serialize(new { name = "Ana Test", email = "ana@test.es" }));
        var fingerprint = generated.Result!.Value.GetProperty("fingerprint").GetString();

        var response = await Send(
            fx.Router,
            "p2",
            OperationIds.PgpExportKey,
            JsonSerializer.Serialize(new { fingerprint, includePrivate = true }));

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.Contains("PRIVATE KEY BLOCK", data.GetProperty("armoredKey").GetString(), StringComparison.Ordinal);
        Assert.True(data.GetProperty("isPrivate").GetBoolean());
    }

    [Fact]
    public async Task ImportKey_ReturnsImportedKeyInfo()
    {
        using var source = PgpFixture.Create();
        var generated = await Send(
            source.Router,
            "i1",
            OperationIds.PgpGenerateKey,
            JsonSerializer.Serialize(new { name = "Ana Test", email = "ana@test.es" }));
        var fingerprint = generated.Result!.Value.GetProperty("fingerprint").GetString();
        var exported = await Send(
            source.Router,
            "i2",
            OperationIds.PgpExportKey,
            JsonSerializer.Serialize(new { fingerprint, includePrivate = false }));
        var armored = exported.Result!.Value.GetProperty("armoredKey").GetString();

        using var target = PgpFixture.Create();
        var response = await Send(
            target.Router,
            "i3",
            OperationIds.PgpImportKey,
            JsonSerializer.Serialize(new { armoredKey = armored }));

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.Equal(fingerprint, data.GetProperty("fingerprint").GetString());
        Assert.False(data.GetProperty("hasPrivate").GetBoolean()); // solo la pública viajó
    }

    [Fact]
    public async Task GenerateKey_HostileExtraFields_AreIgnored()
    {
        using var fx = PgpFixture.Create();
        var payloadJson =
            """{"name":"Ana Test","email":"ana@test.es","command":"calc.exe","args":["--bell"]}""";

        var response = await Send(fx.Router, "h1", OperationIds.PgpGenerateKey, payloadJson);

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.Matches("^[0-9A-F]{40}$", data.GetProperty("fingerprint").GetString());
        Assert.False(data.TryGetProperty("command", out _));
        Assert.False(data.TryGetProperty("args", out _));
    }

    // ------------------------------------------------------------------
    // Política de elevación (Task 009 jamás eleva)
    // ------------------------------------------------------------------

    [Fact]
    public void AllPgpOperations_NeverRequireElevation()
    {
        using var fx = PgpFixture.Create();
        IOperation[] operations =
        [
            new PgpGenerateKeyOperation(fx.Service),
            new PgpListKeysOperation(fx.Service),
            new PgpImportKeyOperation(fx.Service),
            new PgpExportKeyOperation(fx.Service),
            new PgpEncryptOperation(fx.Service),
            new PgpDecryptOperation(fx.Service),
            new PgpSignOperation(fx.Service),
            new PgpVerifyOperation(fx.Service),
        ];

        Assert.Equal(8, operations.Select(op => op.Id).Distinct().Count());
        Assert.All(operations, op => Assert.False(op.RequiresElevation));
    }

    private static Task<BridgeResponse> Send(
        CommandRouter router,
        string id,
        string operation,
        string? payloadJson = null)
    {
        var payload = payloadJson is null ? string.Empty : $",\"payload\":{payloadJson}";
        return router.RouteAsync(
            $"{{\"id\":\"{id}\",\"operation\":\"{operation}\"{payload}}}");
    }
}

/// <summary>
/// PgpService + router para los tests de las operaciones pgp.*: un keyring en
/// carpeta temporal (jamás el %APPDATA% real del equipo) y las mismas 8 ops
/// que registra MainForm.
/// </summary>
internal sealed class PgpFixture : IDisposable
{
    private readonly TempExecutablePath _data = new();

    private PgpFixture(TempExecutablePath data, PgpService service, CommandRouter router)
    {
        _data = data;
        Service = service;
        Router = router;
    }

    public PgpService Service { get; }

    public CommandRouter Router { get; }

    public static PgpFixture Create()
    {
        var data = new TempExecutablePath();
        Directory.CreateDirectory(data.DirectoryPath);
        var store = new PgpKeyringStore(data.DirectoryPath);
        var service = new PgpService(store);

        var router = new CommandRouter(
            new IOperation[]
            {
                new PgpGenerateKeyOperation(service),
                new PgpListKeysOperation(service),
                new PgpImportKeyOperation(service),
                new PgpExportKeyOperation(service),
                new PgpEncryptOperation(service),
                new PgpDecryptOperation(service),
                new PgpSignOperation(service),
                new PgpVerifyOperation(service),
            },
            NullLogger<CommandRouter>.Instance);

        return new PgpFixture(data, service, router);
    }

    public void Dispose() => _data.Dispose();
}
