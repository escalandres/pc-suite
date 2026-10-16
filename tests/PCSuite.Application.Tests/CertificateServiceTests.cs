using System.Text;
using PCSuite.Core.Certificates;
using PCSuite.Core.PGP;
using PCSuite.Infrastructure.PGP;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests de <see cref="PCSuite.Infrastructure.Certificates.CertificateService"/>
/// (Task 019): X.509 autofirmado por algoritmo (archivos PEM en la carpeta de
/// salida, fingerprint SHA-256, subject, validez), inspección universal de los
/// archivos que el propio servicio genera + claves SSH/PGP, round-trip de
/// conversión PEM→DER→PEM y firma PGP de archivos verificable. Todo en
/// carpetas temporales: los tests no tocan el escritorio real.
/// </summary>
public sealed class CertificateServiceTests : IDisposable
{
    private readonly CertificateFixture _fx = CertificateFixture.Create();

    public void Dispose() => _fx.Dispose();

    // ------------------------------------------------------------------
    // 19.1 — X.509 autofirmado
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("rsa2048", "RSA")]
    [InlineData("rsa4096", "RSA")]
    [InlineData("ecdsa-p256", "EC")]
    [InlineData("ecdsa-p384", "EC")]
    [InlineData("ed25519", "Ed25519")]
    public async Task GenerateX509_ByAlgorithm_WritesPemFilesAndFingerprint(string algorithm, string expectedKeyFamily)
    {
        var result = await _fx.Service.GenerateX509Async(new X509GenerateRequest
        {
            Algorithm = algorithm,
            CommonName = "localhost",
        });

        // Fingerprint SHA-256 = 64 hex minúscula.
        Assert.Matches("^[0-9a-f]{64}$", result.Fingerprint);
        Assert.Equal(algorithm, result.Algorithm);
        Assert.Contains("CN=localhost", result.Subject);
        Assert.True(result.ValidTo > result.ValidFrom);
        Assert.Contains("-----BEGIN CERTIFICATE-----", result.CertificatePem);
        Assert.Contains("-----END CERTIFICATE-----", result.CertificatePem);

        // La clave privada se escribe a archivo — nunca en el resultado.
        Assert.True(File.Exists(result.PrivateKeyPath));
        Assert.DoesNotContain("PRIVATE KEY", result.CertificatePem);
        var keyPem = await File.ReadAllTextAsync(result.PrivateKeyPath);
        Assert.Contains("PRIVATE KEY", keyPem);

        // El certificado se inspecciona como X.509 de la familia esperada;
        // autofirmado → Issuer == Subject.
        var inspected = await _fx.Service.InspectAsync(result.CertificatePemToFile(), null);
        Assert.Equal(CertificateContentKind.X509Certificate, inspected.Kind);
        Assert.Contains(expectedKeyFamily, inspected.KindDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(result.Fingerprint, inspected.Fingerprint);
        Assert.Contains("CN=localhost", inspected.Details["Issuer"]);
    }

    [Fact]
    public async Task GenerateX509_WithSans_AndDates_WritesSubjectAltNames()
    {
        var result = await _fx.Service.GenerateX509Async(new X509GenerateRequest
        {
            Algorithm = X509Algorithms.EcdsaP256,
            CommonName = "api.aes-studio.dev",
            Organization = "AES Studio",
            Country = "ES",
            SubjectAltNames = ["api.aes-studio.dev", "192.168.1.10", "admin@aes-studio.dev"],
            ValidityDays = 30,
        });

        Assert.Contains("O=AES Studio", result.Subject);
        Assert.Contains("C=ES", result.Subject);
        var expectedDays = (result.ValidTo - result.ValidFrom).TotalDays;
        Assert.InRange(expectedDays, 29, 31);

        var inspected = await _fx.Service.InspectAsync(result.CertificatePemToFile(), null);
        Assert.Equal(CertificateContentKind.X509Certificate, inspected.Kind);
        Assert.Contains("api.aes-studio.dev", inspected.Details["SANs"]);
        Assert.Contains("192.168.1.10", inspected.Details["SANs"]);
        Assert.Contains("admin@aes-studio.dev", inspected.Details["SANs"]);
    }

    [Fact]
    public async Task GenerateX509_UnknownAlgorithm_ThrowsStaticMessage()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.GenerateX509Async(new X509GenerateRequest
            {
                Algorithm = "dsa",
                CommonName = "localhost",
            }));

        Assert.Contains("no soportado", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // 19.3 — Inspección universal
    // ------------------------------------------------------------------

    [Fact]
    public async Task Inspect_PrivateKeyPem_ReportsPrivateKey()
    {
        var generated = await _fx.Service.GenerateX509Async(new X509GenerateRequest
        {
            Algorithm = X509Algorithms.Rsa2048,
            CommonName = "localhost",
        });

        var inspected = await _fx.Service.InspectAsync(generated.PrivateKeyPath, null);

        Assert.Equal(CertificateContentKind.PrivateKey, inspected.Kind);
        Assert.Contains("RSA", inspected.KindDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2048", inspected.KindDescription);
    }

    [Fact]
    public async Task Inspect_SshPublicKey_ReportsTypeAndComment()
    {
        var path = Path.Combine(_fx.OutputDir, "id_ed25519.pub");
        await File.WriteAllTextAsync(
            path, "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIFakeKey ana@laptop\n");

        var inspected = await _fx.Service.InspectAsync(path, null);

        Assert.Equal(CertificateContentKind.SshPublicKey, inspected.Kind);
        Assert.Equal("ssh-ed25519", inspected.Details["Type"]);
        Assert.Equal("ana@laptop", inspected.Details["Comment"]);
    }

    [Fact]
    public async Task Inspect_PgpPublicKeyArmor_ReportsPgpKind()
    {
        var key = await _fx.Pgp.GenerateKeyAsync(new PgpGenerateKeyRequest(
            "Ana Test", "ana@test.es", PgpAlgorithms.Ed25519, null, null, null));
        var exported = await _fx.Pgp.ExportKeyAsync(new PgpExportKeyRequest(key.Fingerprint, false));
        var path = Path.Combine(_fx.OutputDir, "public.asc");
        await File.WriteAllTextAsync(path, exported);

        var inspected = await _fx.Service.InspectAsync(path, null);

        Assert.Equal(CertificateContentKind.PgpPublicKey, inspected.Kind);
    }

    [Fact]
    public async Task Inspect_UnknownFile_ReportsUnknown()
    {
        var path = Path.Combine(_fx.OutputDir, "random.txt");
        await File.WriteAllTextAsync(path, "ni certificado ni clave: texto plano\n");

        var inspected = await _fx.Service.InspectAsync(path, null);

        Assert.Equal(CertificateContentKind.Unknown, inspected.Kind);
    }

    [Fact]
    public async Task Inspect_MissingFile_ThrowsStaticMessage()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.InspectAsync(
                Path.Combine(_fx.OutputDir, "no-existe.pem"), null));

        Assert.Contains("No se encontró", exception.Message);
    }

    // ------------------------------------------------------------------
    // 19.4 — Conversión PEM↔DER
    // ------------------------------------------------------------------

    [Fact]
    public async Task Convert_PemToDerToPem_RoundTripsFingerprint()
    {
        var generated = await _fx.Service.GenerateX509Async(new X509GenerateRequest
        {
            Algorithm = X509Algorithms.Rsa2048,
            CommonName = "roundtrip.local",
        });

        // PEM → DER
        var toDer = await _fx.Service.ConvertAsync(new CertificateConvertRequest
        {
            InputPath = generated.CertificatePemToFile(),
            InputFormat = "pem",
            OutputFormat = "der",
        });
        Assert.True(File.Exists(toDer.OutputPath));
        var derBytes = await File.ReadAllBytesAsync(toDer.OutputPath);
        Assert.True(derBytes.Length > 0);
        Assert.Equal(0x30, derBytes[0]); // DER SEQUENCE

        // DER → PEM: el fingerprint debe coincidir con el original.
        var toPem = await _fx.Service.ConvertAsync(new CertificateConvertRequest
        {
            InputPath = toDer.OutputPath,
            InputFormat = "der",
            OutputFormat = "pem",
        });
        var inspected = await _fx.Service.InspectAsync(toPem.OutputPath, null);
        Assert.Equal(CertificateContentKind.X509Certificate, inspected.Kind);
        Assert.Equal(generated.Fingerprint, inspected.Fingerprint);
    }

    [Fact]
    public async Task Convert_UnsupportedDirection_ThrowsStaticMessage()
    {
        var path = Path.Combine(_fx.OutputDir, "cert.pem");
        await File.WriteAllTextAsync(path, "-----BEGIN CERTIFICATE-----\n");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.ConvertAsync(new CertificateConvertRequest
            {
                InputPath = path,
                InputFormat = "pfx",
                OutputFormat = "openssh",
            }));

        Assert.Contains("no soportada", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Convert_MissingFile_ThrowsStaticMessage()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.ConvertAsync(new CertificateConvertRequest
            {
                InputPath = Path.Combine(_fx.OutputDir, "no-existe.pem"),
                InputFormat = "pem",
                OutputFormat = "der",
            }));

        Assert.Contains("No se encontró", exception.Message);
    }

    // ------------------------------------------------------------------
    // 19.2 / 17.6 — Firma PGP de archivos
    // ------------------------------------------------------------------

    [Fact]
    public async Task SignFileWithPgp_WritesDetachedArmoredSignature_Verifiable()
    {
        var key = await _fx.Pgp.GenerateKeyAsync(new PgpGenerateKeyRequest(
            "Ana Test", "ana@test.es", PgpAlgorithms.Ed25519, null, null, null));

        var content = "{ \"version\": \"0.5.1\", \"sha256\": \"abc\" }";
        var inputPath = Path.Combine(_fx.OutputDir, "latest.json");
        await File.WriteAllTextAsync(inputPath, content, Encoding.UTF8);

        var signed = await _fx.Service.SignFileWithPgpAsync(
            key.Fingerprint, inputPath, null);

        Assert.Equal(key.Fingerprint, signed.Fingerprint);
        Assert.True(File.Exists(signed.SignaturePath));
        var signature = await File.ReadAllTextAsync(signed.SignaturePath);
        Assert.Contains("-----BEGIN PGP SIGNATURE-----", signature);
        Assert.Contains("-----END PGP SIGNATURE-----", signature);

        // La firma es detached: el original queda intacto y verfica.
        Assert.Equal(content, await File.ReadAllTextAsync(inputPath));
        var verified = await _fx.Pgp.VerifyAsync(new PgpVerifyRequest(content, signature));
        Assert.True(verified.Valid);
        Assert.Equal(key.Fingerprint, verified.Signer?.Fingerprint);
    }

    [Fact]
    public async Task SignFileWithPgp_MissingFile_ThrowsStaticMessage()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.SignFileWithPgpAsync(
                "ABCD",
                Path.Combine(_fx.OutputDir, "no-existe.json"),
                null));

        Assert.Contains("No se encontró", exception.Message);
    }
}

/// <summary>Auxiliar de tests: vuelca el PEM del certificado a un archivo.</summary>
internal static class X509TestExtensions
{
    public static string CertificatePemToFile(this X509GenerateResult result)
    {
        var path = Path.Combine(Path.GetTempPath(), "pcsuite-test-cert-" + Guid.NewGuid().ToString("N") + ".pem");
        File.WriteAllText(path, result.CertificatePem);
        return path;
    }
}
