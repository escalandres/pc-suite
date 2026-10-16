using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.PGP;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Application.Tests;

/// <summary>
/// Las 4 operaciones cert.* por el puente (Task 019): contrato camelCase,
/// payloads estructurados (no-objeto → error controlado, jamás
/// INTERNAL_ERROR), validación de campos obligatorios, round-trip completo
/// generar→inspeccionar→convertir→firmar, el passphrase nunca se refleja en
/// los errores y ninguna operación declara elevación. Todo en carpetas
/// temporales: los tests no tocan el escritorio real.
/// </summary>
public sealed class CertificateOperationTests
{
    /// <summary>Marcador que jamás debe aparecer en un mensaje de error.</summary>
    private const string MarkerPassphrase = "MARCADOR-SECRETO-QUE-NUNCA-APARECE";

    // ------------------------------------------------------------------
    // Contrato camelCase: cert.x509.generate
    // ------------------------------------------------------------------

    [Fact]
    public async Task X509Generate_ReturnsDto_CamelCase()
    {
        using var fx = CertificateFixture.Create();

        var response = await Send(
            fx.Router,
            "g1",
            OperationIds.CertX509Generate,
            JsonSerializer.Serialize(new { algorithm = "rsa2048", commonName = "bridge.local" }));

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.Matches("^[0-9a-f]{64}$", data.GetProperty("fingerprint").GetString());
        Assert.Equal("rsa2048", data.GetProperty("algorithm").GetString());
        Assert.Contains("CN=bridge.local", data.GetProperty("subject").GetString());
        Assert.Contains("-----BEGIN CERTIFICATE-----", data.GetProperty("certificatePem").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("privateKeyPath").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("serialNumber").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("validFrom").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("validTo").GetString()));
    }

    [Fact]
    public async Task X509Generate_MissingCommonName_FailsWithError()
    {
        using var fx = CertificateFixture.Create();

        var response = await Send(
            fx.Router,
            "g2",
            OperationIds.CertX509Generate,
            JsonSerializer.Serialize(new { algorithm = "rsa2048" }));

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.DoesNotContain("   at ", response.Error.Message);
    }

    // ------------------------------------------------------------------
    // Payload estructurado: no-objeto → error controlado
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(OperationIds.CertX509Generate, "\"certificado\"")]
    [InlineData(OperationIds.CertInspect, "42")]
    [InlineData(OperationIds.CertConvert, "\"pem\"")]
    [InlineData(OperationIds.CertPgpSignFile, "true")]
    public async Task NonObjectPayload_FailsWithoutInternalError(string operation, string payloadJson)
    {
        using var fx = CertificateFixture.Create();

        var response = await Send(fx.Router, "x1", operation, payloadJson);

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        // Nunca INTERNAL_ERROR: es un payload malformado, no un fallo del host.
        Assert.NotEqual(BridgeErrorCodes.InternalError, response.Error.Code);
        Assert.DoesNotContain("   at ", response.Error.Message);
    }

    // ------------------------------------------------------------------
    // cert.inspect + cert.convert: round-trip por el puente
    // ------------------------------------------------------------------

    [Fact]
    public async Task Generate_Inspect_AndConvert_RoundTripThroughBridge()
    {
        using var fx = CertificateFixture.Create();

        var generated = await Send(
            fx.Router,
            "c1",
            OperationIds.CertX509Generate,
            JsonSerializer.Serialize(new { algorithm = "rsa2048", commonName = "rt.local" }));
        Assert.True(generated.Success);
        var pemPath = await WriteTempAsync(
            fx.OutputDir,
            generated.Result!.Value.GetProperty("certificatePem").GetString()!);
        var fingerprint = generated.Result.Value.GetProperty("fingerprint").GetString()!;

        // Inspección por el puente: kind camelCase + fingerprint estable.
        var inspected = await Send(
            fx.Router,
            "c2",
            OperationIds.CertInspect,
            JsonSerializer.Serialize(new { path = pemPath }));
        Assert.True(inspected.Success);
        var inspectData = inspected.Result!.Value;
        Assert.Equal("x509certificate", inspectData.GetProperty("kind").GetString());
        Assert.Equal(fingerprint, inspectData.GetProperty("fingerprint").GetString());
        Assert.Contains(
            "CN=rt.local",
            inspectData.GetProperty("details").GetProperty("Subject").GetString());
        Assert.Contains(
            "CN=rt.local",
            inspectData.GetProperty("details").GetProperty("Issuer").GetString());

        // Conversión PEM → DER por el puente.
        var converted = await Send(
            fx.Router,
            "c3",
            OperationIds.CertConvert,
            JsonSerializer.Serialize(new
            {
                inputPath = pemPath,
                inputFormat = "pem",
                outputFormat = "der",
            }));
        Assert.True(converted.Success);
        var outputPath = converted.Result!.Value.GetProperty("outputPath").GetString();
        Assert.True(File.Exists(outputPath));

        // DER → PEM de vuelta: el fingerprint sobrevive al round-trip.
        var back = await Send(
            fx.Router,
            "c4",
            OperationIds.CertConvert,
            JsonSerializer.Serialize(new
            {
                inputPath = outputPath,
                inputFormat = "der",
                outputFormat = "pem",
            }));
        Assert.True(back.Success);

        var reInspected = await Send(
            fx.Router,
            "c5",
            OperationIds.CertInspect,
            JsonSerializer.Serialize(new { path = back.Result!.Value.GetProperty("outputPath").GetString() }));
        Assert.True(reInspected.Success);
        Assert.Equal(
            fingerprint,
            reInspected.Result!.Value.GetProperty("fingerprint").GetString());
    }

    // ------------------------------------------------------------------
    // cert.pgp.signFile: firma verificable + passphrase jamás en errores
    // ------------------------------------------------------------------

    [Fact]
    public async Task SignFileWithPgp_ThroughBridge_ProducesVerifiableSignature()
    {
        using var fx = CertificateFixture.Create();
        var key = await fx.Pgp.GenerateKeyAsync(new PgpGenerateKeyRequest(
            "Ana Test", "ana@test.es", PgpAlgorithms.Ed25519, null, null, null));

        var content = "{ \"version\": \"0.5.2\" }";
        var inputPath = await WriteTempAsync(fx.OutputDir, content);

        var signed = await Send(
            fx.Router,
            "s1",
            OperationIds.CertPgpSignFile,
            JsonSerializer.Serialize(new { fingerprint = key.Fingerprint, inputPath }));
        Assert.True(signed.Success);
        var signaturePath = signed.Result!.Value.GetProperty("signaturePath").GetString();
        Assert.True(File.Exists(signaturePath));
        Assert.Equal(
            key.Fingerprint,
            signed.Result!.Value.GetProperty("fingerprint").GetString());

        // La firma es detached armoured y verifica contra el contenido.
        var signature = await File.ReadAllTextAsync(signaturePath!);
        Assert.Contains("-----BEGIN PGP SIGNATURE-----", signature);
        var verified = await fx.Pgp.VerifyAsync(new PgpVerifyRequest(content, signature));
        Assert.True(verified.Valid);
    }

    [Fact]
    public async Task SignFileWithPgp_UnknownFingerprint_SurfacesControlledError_NoSecrets()
    {
        using var fx = CertificateFixture.Create();
        var inputPath = await WriteTempAsync(fx.OutputDir, "contenido");

        var response = await Send(
            fx.Router,
            "s2",
            OperationIds.CertPgpSignFile,
            JsonSerializer.Serialize(new
            {
                fingerprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                inputPath,
                passphrase = MarkerPassphrase,
            }));

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.NotEqual(BridgeErrorCodes.InternalError, response.Error.Code);
        Assert.DoesNotContain("   at ", response.Error.Message);
        // El passphrase jamás se refleja en el error.
        Assert.DoesNotContain(MarkerPassphrase, response.Error.Message);
    }

    // ------------------------------------------------------------------
    // Ninguna operación cert.* declara elevación
    // ------------------------------------------------------------------

    [Fact]
    public void CertOperations_NeverRequireElevation()
    {
        using var fx = CertificateFixture.Create();

        Assert.False(new CertX509GenerateOperation(fx.Service).RequiresElevation);
        Assert.False(new CertInspectOperation(fx.Service).RequiresElevation);
        Assert.False(new CertConvertOperation(fx.Service).RequiresElevation);
        Assert.False(new CertPgpSignFileOperation(fx.Service).RequiresElevation);
    }

    // ------------------------------------------------------------------
    // Auxiliares
    // ------------------------------------------------------------------

    private static async Task<string> WriteTempAsync(string directory, string content)
    {
        var path = Path.Combine(directory, "input-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, content);
        return path;
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
