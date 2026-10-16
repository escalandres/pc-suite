using System.Text;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Utilities;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
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

    [Fact]
    public async Task GenerateX509_WithPfxPassword_WritesPkcs12WithKeyAndCert()
    {
        var result = await _fx.Service.GenerateX509Async(new X509GenerateRequest
        {
            Algorithm = X509Algorithms.Rsa2048,
            CommonName = "pfx.local",
            PfxPassword = "s3creta",
        });

        Assert.NotNull(result.PfxPath);
        Assert.True(File.Exists(result.PfxPath));

        // El PFX reabre con la password y contiene clave + certificado
        // (cert.convert pfx→pem usa exactamente este camino).
        var converted = await _fx.Service.ConvertAsync(new CertificateConvertRequest
        {
            InputPath = result.PfxPath!,
            InputFormat = "pfx",
            OutputFormat = "pem",
            InputPassword = "s3creta",
        });
        var output = await File.ReadAllTextAsync(converted.OutputPath);
        Assert.Contains("PRIVATE KEY", output);
        Assert.Contains("BEGIN CERTIFICATE", output);
    }

    // ------------------------------------------------------------------
    // 19.5 — CSR (PKCS#10)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("rsa2048")]
    [InlineData("ecdsa-p256")]
    [InlineData("ed25519")]
    public async Task GenerateCsr_ByAlgorithm_VerifiesAgainstOwnKey(string algorithm)
    {
        var result = await _fx.Service.GenerateCsrAsync(new CsrGenerateRequest
        {
            Algorithm = algorithm,
            CommonName = "api.aes-studio.dev",
        });

        Assert.Contains("-----BEGIN CERTIFICATE REQUEST-----", result.CsrPem);
        Assert.Contains("-----END CERTIFICATE REQUEST-----", result.CsrPem);
        Assert.True(File.Exists(result.PrivateKeyPath));
        Assert.Contains("PRIVATE KEY", await File.ReadAllTextAsync(result.PrivateKeyPath));

        // El CSR verifica contra su propia clave (PemReader → Pkcs10).
        using var reader = new StringReader(result.CsrPem);
        var pemReader = new PemReader(reader);
        var parsed = pemReader.ReadObject();
        var csr = Assert.IsType<Pkcs10CertificationRequest>(parsed);
        Assert.True(csr.Verify());
    }

    [Fact]
    public async Task GenerateCsr_WithSans_TravelsInExtensionRequest()
    {
        var result = await _fx.Service.GenerateCsrAsync(new CsrGenerateRequest
        {
            Algorithm = X509Algorithms.Rsa2048,
            CommonName = "api.aes-studio.dev",
            Organization = "AES Studio",
            Country = "ES",
            SubjectAltNames = ["api.aes-studio.dev", "192.168.1.10", "admin@aes-studio.dev"],
        });

        using var reader = new StringReader(result.CsrPem);
        var pemReader = new PemReader(reader);
        var csr = Assert.IsType<Pkcs10CertificationRequest>(pemReader.ReadObject());

        Assert.Contains("O=AES Studio", csr.GetCertificationRequestInfo().Subject.ToString());
        Assert.Contains("C=ES", csr.GetCertificationRequestInfo().Subject.ToString());

        // extensionRequest: las CA solo honran los SANs — deben estar presentes.
        var requested = csr.GetRequestedExtensions();
        Assert.NotNull(requested);
        var san = requested.GetExtension(X509Extensions.SubjectAlternativeName);
        Assert.NotNull(san);
        var generalNames = GeneralNames.GetInstance(san.GetParsedValue());
        var labels = generalNames.GetNames()
            .Select(name => SanTestExtensions.DescribeSanForTest(name))
            .ToList();
        Assert.Contains("api.aes-studio.dev", labels);
        Assert.Contains("192.168.1.10", labels);
        Assert.Contains("admin@aes-studio.dev", labels);
    }

    [Fact]
    public async Task GenerateCsr_ThenInspect_ReportsCsrWithSansAndValidSignature()
    {
        var generated = await _fx.Service.GenerateCsrAsync(new CsrGenerateRequest
        {
            Algorithm = X509Algorithms.EcdsaP256,
            CommonName = "inspect.csr.local",
            SubjectAltNames = ["inspect.csr.local"],
        });
        var path = Path.Combine(_fx.OutputDir, "csr-input.pem");
        await File.WriteAllTextAsync(path, generated.CsrPem);

        var inspected = await _fx.Service.InspectAsync(path, null);

        Assert.Equal(CertificateContentKind.X509Csr, inspected.Kind);
        Assert.Contains("inspect.csr.local", inspected.Details["Subject"]);
        Assert.Contains("inspect.csr.local", inspected.Details["SANs"]);
        Assert.Equal("True", inspected.Details["Signature valid"]);
    }

    // ------------------------------------------------------------------
    // 19.4 — Conversiones PEM↔DER / PFX / OpenSSH
    // ------------------------------------------------------------------

    [Fact]
    public async Task Convert_PemBundleToPfx_RoundTripsKeyAndCert()
    {
        var generated = await _fx.Service.GenerateX509Async(new X509GenerateRequest
        {
            Algorithm = X509Algorithms.Rsa2048,
            CommonName = "bundle.local",
        });

        // Bundle estilo fullchain: clave + certificado en un solo PEM.
        var bundlePath = Path.Combine(_fx.OutputDir, "bundle.pem");
        await File.WriteAllTextAsync(
            bundlePath,
            await File.ReadAllTextAsync(generated.PrivateKeyPath) + generated.CertificatePem);

        var toPfx = await _fx.Service.ConvertAsync(new CertificateConvertRequest
        {
            InputPath = bundlePath,
            InputFormat = "pem",
            OutputFormat = "pfx",
            OutputPassword = "cl4ve",
        });
        Assert.True(File.Exists(toPfx.OutputPath));

        // PFX → PEM de vuelta: clave + certificado sobreviven.
        var back = await _fx.Service.ConvertAsync(new CertificateConvertRequest
        {
            InputPath = toPfx.OutputPath,
            InputFormat = "pfx",
            OutputFormat = "pem",
            InputPassword = "cl4ve",
        });
        var output = await File.ReadAllTextAsync(back.OutputPath);
        Assert.Contains("PRIVATE KEY", output);
        Assert.Contains("BEGIN CERTIFICATE", output);
    }

    [Fact]
    public async Task Convert_PemToPfx_WithoutPassword_ThrowsStaticMessage()
    {
        var path = Path.Combine(_fx.OutputDir, "bundle-nopass.pem");
        await File.WriteAllTextAsync(path, "-----BEGIN PRIVATE KEY-----\n");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.ConvertAsync(new CertificateConvertRequest
            {
                InputPath = path,
                InputFormat = "pem",
                OutputFormat = "pfx",
            }));

        Assert.Contains("password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Convert_PemBundleWithoutKey_ThrowsStaticMessage()
    {
        var generated = await _fx.Service.GenerateX509Async(new X509GenerateRequest
        {
            Algorithm = X509Algorithms.Rsa2048,
            CommonName = "solo-cert.local",
        });
        var certOnlyPath = Path.Combine(_fx.OutputDir, "cert-only.pem");
        await File.WriteAllTextAsync(certOnlyPath, generated.CertificatePem);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.ConvertAsync(new CertificateConvertRequest
            {
                InputPath = certOnlyPath,
                InputFormat = "pem",
                OutputFormat = "pfx",
                OutputPassword = "cl4ve",
            }));

        Assert.Contains("AMBOS", exception.Message);
    }

    [Fact]
    public async Task Convert_PfxWithWrongPassword_ThrowsStaticMessage()
    {
        var generated = await _fx.Service.GenerateX509Async(new X509GenerateRequest
        {
            Algorithm = X509Algorithms.Rsa2048,
            CommonName = "wrong-pass.local",
            PfxPassword = "correcta",
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.ConvertAsync(new CertificateConvertRequest
            {
                InputPath = generated.PfxPath!,
                InputFormat = "pfx",
                OutputFormat = "pem",
                InputPassword = "incorrecta",
            }));

        Assert.Contains("password", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", exception.Message);
    }

    [Fact]
    public async Task Convert_OpenSshKeyToPem_ProducesPkcs8PrivateKey()
    {
        // Generamos una clave Ed25519 y la codificamos en formato OpenSSH
        // (openssh-key-v1) con el encoder del propio BouncyCastle — el
        // mismo formato que produce ssh-keygen.
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new Org.BouncyCastle.Security.SecureRandom()));
        var keyPair = generator.GenerateKeyPair();
        var blob = OpenSshPrivateKeyUtilities.EncodePrivateKey(keyPair.Private);

        var base64 = Convert.ToBase64String(blob);
        var sb = new StringBuilder();
        sb.AppendLine("-----BEGIN OPENSSH PRIVATE KEY-----");
        for (var i = 0; i < base64.Length; i += 70)
        {
            sb.AppendLine(base64.Substring(i, Math.Min(70, base64.Length - i)));
        }
        sb.AppendLine("-----END OPENSSH PRIVATE KEY-----");
        var sshPath = Path.Combine(_fx.OutputDir, "id_ed25519");
        await File.WriteAllTextAsync(sshPath, sb.ToString());

        var converted = await _fx.Service.ConvertAsync(new CertificateConvertRequest
        {
            InputPath = sshPath,
            InputFormat = "openssh",
            OutputFormat = "pem",
        });

        // El PEM resultante es un PKCS#8 legible con la MISMA clave.
        var output = await File.ReadAllTextAsync(converted.OutputPath);
        Assert.Contains("-----BEGIN PRIVATE KEY-----", output);
        using var reader = new StringReader(output);
        var pemReader = new PemReader(reader);
        var parsed = pemReader.ReadObject();
        var parsedKey = parsed switch
        {
            AsymmetricCipherKeyPair pair => pair.Private,
            AsymmetricKeyParameter key => key,
            _ => null,
        };
        Assert.NotNull(parsedKey);
        var originalParams = (Ed25519PrivateKeyParameters)keyPair.Private;
        var parsedParams = Assert.IsType<Ed25519PrivateKeyParameters>(parsedKey);
        Assert.True(originalParams.Equals(parsedParams));
    }

    [Fact]
    public async Task Convert_NotOpenSshArmor_ThrowsStaticMessage()
    {
        var path = Path.Combine(_fx.OutputDir, "fake-ssh.txt");
        await File.WriteAllTextAsync(path, "no es una clave openssh\n");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.ConvertAsync(new CertificateConvertRequest
            {
                InputPath = path,
                InputFormat = "openssh",
                OutputFormat = "pem",
            }));

        Assert.Contains("OPENSSH PRIVATE KEY", exception.Message);
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

/// <summary>Etiqueta legible de un GeneralName (misma lógica del servicio).</summary>
internal static class SanTestExtensions
{
    public static string? DescribeSanForTest(GeneralName name)
    {
        return name.TagNo switch
        {
            GeneralName.DnsName or GeneralName.Rfc822Name when name.Name is DerIA5String text =>
                text.GetString(),
            GeneralName.IPAddress when name.Name is Asn1OctetString ip =>
                new System.Net.IPAddress(ip.GetOctets()).ToString(),
            _ => null,
        };
    }
}
