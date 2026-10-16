using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using PCSuite.Core.Certificates;
using PCSuite.Core.PGP;

namespace PCSuite.Infrastructure.Certificates;

/// <summary>
/// Servicio de certificados (Task 019): X.509 autofirmado, inspección
/// universal, conversión de formatos, CSR y firma PGP de archivos.
///
/// TODO BouncyCastle en proceso — sin OpenSSL, makecert ni gpg CLI.
/// La seguridad sigue el diseño de PCSuite: las claves privadas se escriben
/// a archivo (nunca viajan por el bridge en claro), los passphrases solo
/// se usan en memoria y jamás se registran en logs.
/// </summary>
public sealed class CertificateService : ICertificateService
{
    private readonly IPgpService _pgp;
    private readonly ILogger<CertificateService>? _logger;
    private readonly string _outputDir;

    /// <summary>
    /// Carpeta de salida por defecto: Desktop\PCSuite-Certificados. Los tests
    /// inyectan una carpeta temporal para no tocar el escritorio real.
    /// </summary>
    public static string DefaultOutputDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Desktop", "PCSuite-Certificados");

    public CertificateService(
        IPgpService pgp,
        ILogger<CertificateService>? logger = null,
        string? outputDirectory = null)
    {
        _pgp = pgp;
        _logger = logger;
        _outputDir = outputDirectory ?? DefaultOutputDir;
    }

    // ==================================================================
    // 19.1 — X.509 autofirmado
    // ==================================================================

    public Task<X509GenerateResult> GenerateX509Async(X509GenerateRequest request, CancellationToken ct = default)
    {
        var (keyPair, signatureAlgorithm) = GenerateKeyPair(request.Algorithm);
        var now = DateTime.UtcNow;

        var generator = new X509V3CertificateGenerator();
        generator.SetSubjectDN(BuildX509Name(request.CommonName, request.Organization, request.OrganizationalUnit, request.Country));
        generator.SetIssuerDN(BuildX509Name(request.CommonName, request.Organization, request.OrganizationalUnit, request.Country));
        generator.SetSerialNumber(BigInteger.ProbablePrime(120, new SecureRandom()));
        generator.SetNotBefore(now.AddDays(-1));
        generator.SetNotAfter(now.AddDays(request.ValidityDays));
        generator.SetPublicKey(keyPair.Public);

        if (request.SubjectAltNames is { Count: > 0 } sans)
        {
            generator.AddExtension(X509Extensions.SubjectAlternativeName, false, BuildSans(sans));
        }

        var certificate = generator.Generate(
            new Asn1SignatureFactory(signatureAlgorithm, keyPair.Private));

        Directory.CreateDirectory(_outputDir);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        var certPem = PemEncode(certificate);
        var certPath = Path.Combine(_outputDir, $"certificate-{timestamp}.pem");
        File.WriteAllText(certPath, certPem);

        var keyPem = PemEncode(keyPair.Private);
        var keyPath = Path.Combine(_outputDir, $"private-key-{timestamp}.pem");
        File.WriteAllText(keyPath, keyPem);

        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(certificate.GetEncoded()));

        var result = new X509GenerateResult
        {
            CertificatePem = certPem,
            PrivateKeyPath = keyPath,
            PfxPath = null,
            Fingerprint = fingerprint,
            SerialNumber = certificate.SerialNumber.ToString(16),
            Subject = certificate.SubjectDN.ToString(),
            ValidFrom = new DateTimeOffset(certificate.NotBefore, TimeSpan.Zero),
            ValidTo = new DateTimeOffset(certificate.NotAfter, TimeSpan.Zero),
            Algorithm = request.Algorithm,
        };

        _logger?.LogInformation(
            "Certificado X.509 generado: {Subject} ({Algorithm}), válido hasta {ValidTo}",
            result.Subject, result.Algorithm, result.ValidTo);
        return Task.FromResult(result);
    }

    // ==================================================================
    // 19.2 — Firma PGP de archivos (cierra 17.6)
    //
    // Contrato: firma el contenido como TEXTO UTF-8 (latest.json, notas .md,
    // scripts…). gpg --verify contra el archivo original coincide para
    // archivos UTF-8 sin BOM. Firmar binarios requiere una variante por
    // bytes en PgpService: próxima iteración.
    // ==================================================================

    public async Task<PgpSignFileResult> SignFileWithPgpAsync(
        string fingerprint, string inputPath, string? passphrase, CancellationToken ct = default)
    {
        if (!File.Exists(inputPath))
        {
            throw new InvalidOperationException($"No se encontró el archivo: {inputPath}");
        }

        var content = await File.ReadAllTextAsync(inputPath, ct).ConfigureAwait(false);
        var signature = await _pgp.SignAsync(
            new PgpSignRequest(content, fingerprint, passphrase),
            ct).ConfigureAwait(false);

        Directory.CreateDirectory(_outputDir);
        var signaturePath = Path.ChangeExtension(inputPath, ".sig");
        await File.WriteAllTextAsync(signaturePath, signature, ct).ConfigureAwait(false);

        _logger?.LogInformation(
            "Archivo firmado con PGP: {Input} → {Signature}",
            inputPath, signaturePath);

        return new PgpSignFileResult
        {
            SignaturePath = signaturePath,
            Fingerprint = fingerprint,
        };
    }

    // ==================================================================
    // 19.3 — Inspección universal
    // ==================================================================

    public Task<CertificateInspectResult> InspectAsync(
        string path, string? password, CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"No se encontró el archivo: {path}");
        }

        var content = File.ReadAllText(path);

        if (content.Contains("-----BEGIN CERTIFICATE-----"))
        {
            return Task.FromResult(InspectX509Certificate(content));
        }
        if (content.Contains("-----BEGIN CERTIFICATE REQUEST-----"))
        {
            return Task.FromResult(InspectCsr(content));
        }
        if (content.Contains("-----BEGIN PRIVATE KEY-----") ||
            content.Contains("-----BEGIN RSA PRIVATE KEY-----") ||
            content.Contains("-----BEGIN EC PRIVATE KEY-----"))
        {
            return Task.FromResult(InspectPrivateKey(content));
        }
        if (content.Contains("-----BEGIN PGP PUBLIC KEY BLOCK-----"))
        {
            return Task.FromResult(new CertificateInspectResult
            {
                Kind = CertificateContentKind.PgpPublicKey,
                KindDescription = "PGP Public Key",
                Details = new Dictionary<string, string>(),
            });
        }
        if (content.Contains("-----BEGIN PGP PRIVATE KEY BLOCK-----"))
        {
            return Task.FromResult(new CertificateInspectResult
            {
                Kind = CertificateContentKind.PgpPrivateKey,
                KindDescription = "PGP Private Key (cifrada con passphrase)",
                Details = new Dictionary<string, string>(),
            });
        }
        if (content.Contains("ssh-rsa ") || content.Contains("ssh-ed25519 ") ||
            content.Contains("ecdsa-sha2-"))
        {
            return Task.FromResult(InspectSshPublicKey(content));
        }

        return Task.FromResult(new CertificateInspectResult
        {
            Kind = CertificateContentKind.Unknown,
            KindDescription = "Formato no reconocido",
            Details = new Dictionary<string, string>(),
        });
    }

    // ==================================================================
    // 19.4 — Conversión de formatos (PEM↔DER)
    // ==================================================================

    public Task<CertificateConvertResult> ConvertAsync(
        CertificateConvertRequest request, CancellationToken ct = default)
    {
        if (!File.Exists(request.InputPath))
        {
            throw new InvalidOperationException($"No se encontró: {request.InputPath}");
        }

        Directory.CreateDirectory(_outputDir);
        var baseName = Path.GetFileNameWithoutExtension(request.InputPath);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        string outputPath;

        switch (request.InputFormat, request.OutputFormat)
        {
            case ("pem", "der"):
            {
                var cert = ParsePemCertificate(File.ReadAllText(request.InputPath));
                outputPath = Path.Combine(_outputDir, $"{baseName}-{timestamp}.der");
                File.WriteAllBytes(outputPath, cert.GetEncoded());
                break;
            }
            case ("der", "pem"):
            {
                var cert = new X509CertificateParser()
                    .ReadCertificate(File.ReadAllBytes(request.InputPath));
                if (cert is null)
                {
                    throw new InvalidOperationException("El archivo DER no contiene un certificado X.509.");
                }
                outputPath = Path.Combine(_outputDir, $"{baseName}-{timestamp}.pem");
                File.WriteAllText(outputPath, PemEncode(cert));
                break;
            }
            default:
                throw new InvalidOperationException(
                    $"Conversión {request.InputFormat}→{request.OutputFormat} no soportada aún.");
        }

        return Task.FromResult(new CertificateConvertResult
        {
            OutputPath = outputPath,
            OutputFormat = request.OutputFormat,
        });
    }

    // ==================================================================
    // 19.5 — CSR (PKCS#10) — TODO: BouncyCastle 2.x cambió la API de
    // Pkcs10; implementar en la próxima iteración cuando se explore la
    // superficie disponible. El resto del servicio está listo.
    // ==================================================================

    public Task<CsrGenerateResult> GenerateCsrAsync(
        CsrGenerateRequest request, CancellationToken ct = default)
    {
        throw new InvalidOperationException(
            "Generación de CSR en desarrollo — disponible en la próxima iteración. " +
            "Usa 'openssl req -new -key key.pem -out csr.pem' mientras tanto.");
    }

    // ==================================================================
    // Utilidades privadas
    // ==================================================================

    private static (AsymmetricCipherKeyPair KeyPair, string SignatureAlgorithm) GenerateKeyPair(
        string algorithm) => algorithm switch
    {
        X509Algorithms.Rsa2048 => (GenerateRsa(2048), "SHA256withRSA"),
        X509Algorithms.Rsa4096 => (GenerateRsa(4096), "SHA256withRSAandMGF1"),
        X509Algorithms.EcdsaP256 => (GenerateEcdsa("P-256"), "SHA256withECDSA"),
        X509Algorithms.EcdsaP384 => (GenerateEcdsa("P-384"), "SHA384withECDSA"),
        X509Algorithms.Ed25519 => (GenerateEd25519(), "Ed25519"),
        _ => throw new InvalidOperationException($"Algoritmo no soportado: {algorithm}"),
    };

    private static AsymmetricCipherKeyPair GenerateRsa(int bits)
    {
        var generator = new RsaKeyPairGenerator();
        generator.Init(new RsaKeyGenerationParameters(
            BigInteger.ValueOf(0x10001), new SecureRandom(), bits, 80));
        return generator.GenerateKeyPair();
    }

    private static AsymmetricCipherKeyPair GenerateEcdsa(string curve)
    {
        var generator = new ECKeyPairGenerator("ECDSA");
        var ecP = Org.BouncyCastle.Asn1.Sec.SecNamedCurves.GetByName(
            curve == "P-256" ? "secp256r1" : "secp384r1");
        var ecSpec = new ECDomainParameters(ecP.Curve, ecP.G, ecP.N, ecP.H);
        generator.Init(new ECKeyGenerationParameters(ecSpec, new SecureRandom()));
        return generator.GenerateKeyPair();
    }

    private static AsymmetricCipherKeyPair GenerateEd25519()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        return generator.GenerateKeyPair();
    }

    private static X509Name BuildX509Name(
        string commonName, string? organization, string? organizationalUnit, string? country)
    {
        var oids = new List<DerObjectIdentifier> { X509Name.CN };
        var values = new List<string> { commonName };
        if (!string.IsNullOrEmpty(organization))
        {
            oids.Add(X509Name.O);
            values.Add(organization);
        }
        if (!string.IsNullOrEmpty(organizationalUnit))
        {
            oids.Add(X509Name.OU);
            values.Add(organizationalUnit);
        }
        if (!string.IsNullOrEmpty(country))
        {
            oids.Add(X509Name.C);
            values.Add(country);
        }
        return new X509Name(oids, values);
    }

    private static GeneralNames BuildSans(IReadOnlyList<string> sans)
    {
        var names = new List<GeneralName>();
        foreach (var san in sans)
        {
            if (System.Net.IPAddress.TryParse(san, out var ip))
            {
                var ipBytes = ip.GetAddressBytes();
                var octets = new byte[ipBytes.Length];
                ipBytes.CopyTo(octets, 0);
                names.Add(new GeneralName(GeneralName.IPAddress, new DerOctetString(octets)));
            }
            else if (san.Contains('@'))
            {
                names.Add(new GeneralName(GeneralName.Rfc822Name, san));
            }
            else
            {
                names.Add(new GeneralName(GeneralName.DnsName, san));
            }
        }
        return new GeneralNames([.. names]);
    }

    private static string BuildX500DistinguishedName(
        string commonName, string? organization, string? country)
    {
        var parts = new List<string> { $"CN={commonName}" };
        if (!string.IsNullOrEmpty(organization)) parts.Add($"O={organization}");
        if (!string.IsNullOrEmpty(country)) parts.Add($"C={country}");
        return string.Join(", ", parts);
    }

    private static string PemEncode(object value)
    {
        using var writer = new StringWriter();
        var pemWriter = new PemWriter(writer);
        pemWriter.WriteObject(value);
        return writer.ToString();
    }

    private static string PemEncodeCsr(byte[] derCsr)
    {
        var base64 = Convert.ToBase64String(derCsr);
        var sb = new StringBuilder();
        sb.AppendLine("-----BEGIN CERTIFICATE REQUEST-----");
        for (var i = 0; i < base64.Length; i += 64)
        {
            sb.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
        }
        sb.AppendLine("-----END CERTIFICATE REQUEST-----");
        return sb.ToString();
    }

    private static Org.BouncyCastle.X509.X509Certificate ParsePemCertificate(string pem)
    {
        using var reader = new StringReader(pem);
        var pemReader = new PemReader(reader);
        var obj = pemReader.ReadObject();
        if (obj is Org.BouncyCastle.X509.X509Certificate cert) return cert;
        throw new InvalidOperationException("El PEM no contiene un certificado X.509.");
    }

    // ------------------------------------------------------------------
    // Inspectores
    // ------------------------------------------------------------------

    private static CertificateInspectResult InspectX509Certificate(string pem)
    {
        var cert = ParsePemCertificate(pem);
        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(cert.GetEncoded()));

        var algorithm = cert.GetPublicKey() switch
        {
            RsaKeyParameters rsa => $"RSA {rsa.Modulus.BitLength}",
            _ => cert.SigAlgName,
        };

        var details = new Dictionary<string, string>
        {
            ["Subject"] = cert.SubjectDN.ToString(),
            ["Issuer"] = cert.IssuerDN.ToString(),
            ["Serial"] = cert.SerialNumber.ToString(16),
            ["Valid from"] = cert.NotBefore.ToString("yyyy-MM-dd HH:mm:ss UTC"),
            ["Valid to"] = cert.NotAfter.ToString("yyyy-MM-dd HH:mm:ss UTC"),
            ["Signature algorithm"] = cert.SigAlgName,
        };

        // SANs: extensión opcional; ilegible → se omite el campo (no rompe).
        try
        {
            var octets = cert.GetExtensionValue(X509Extensions.SubjectAlternativeName);
            if (octets is not null)
            {
                var generalNames = GeneralNames.GetInstance(
                    Asn1Object.FromByteArray(octets.GetOctets()));
                var labels = generalNames.GetNames()
                    .Select(DescribeSan)
                    .Where(label => !string.IsNullOrEmpty(label))
                    .ToList();
                if (labels.Count > 0)
                {
                    details["SANs"] = string.Join(", ", labels);
                }
            }
        }
        catch
        {
            // sin SANs o extensión no parseable
        }

        return new CertificateInspectResult
        {
            Kind = CertificateContentKind.X509Certificate,
            KindDescription = $"X.509 Certificate ({algorithm})",
            Fingerprint = fingerprint,
            Details = details,
        };
    }

    /// <summary>Etiqueta legible de un GeneralName (DNS, email o IP).</summary>
    private static string? DescribeSan(GeneralName name)
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

    private static CertificateInspectResult InspectCsr(string pem)
    {
        using var reader = new StringReader(pem);
        var pemReader = new PemReader(reader);
        var obj = pemReader.ReadObject();
        var subject = "desconocido";
        if (obj is Org.BouncyCastle.Asn1.Pkcs.CertificationRequest csr)
        {
            subject = csr.GetCertificationRequestInfo().Subject.ToString();
        }
        return new CertificateInspectResult
        {
            Kind = CertificateContentKind.X509Csr,
            KindDescription = "Certificate Signing Request (PKCS#10)",
            Details = new Dictionary<string, string>
            {
                ["Subject"] = subject,
            },
        };
    }

    private static CertificateInspectResult InspectPrivateKey(string pem)
    {
        try
        {
            using var reader = new StringReader(pem);
            var pemReader = new PemReader(reader);
            var obj = pemReader.ReadObject();
            var algorithm = obj switch
            {
                AsymmetricCipherKeyPair pair when pair.Private is RsaKeyParameters rsa =>
                    $"RSA Private Key ({rsa.Modulus.BitLength} bits)",
                AsymmetricCipherKeyPair pair when pair.Private is ECKeyParameters ec =>
                    $"EC Private Key ({ec.AlgorithmName})",
                AsymmetricCipherKeyPair pair when pair.Private is Ed25519PrivateKeyParameters =>
                    "Ed25519 Private Key",
                _ => "Private Key",
            };
            return new CertificateInspectResult
            {
                Kind = CertificateContentKind.PrivateKey,
                KindDescription = algorithm,
                Details = new Dictionary<string, string>(),
            };
        }
        catch (Exception ex)
        {
            return new CertificateInspectResult
            {
                Kind = CertificateContentKind.PrivateKey,
                KindDescription = "Private Key (posiblemente cifrada)",
                Details = new Dictionary<string, string> { ["Error"] = ex.Message },
            };
        }
    }

    private static CertificateInspectResult InspectSshPublicKey(string content)
    {
        var parts = content.Trim().Split(' ');
        var type = parts.Length > 0 ? parts[0] : "desconocido";
        var comment = parts.Length > 2 ? parts[2] : "";

        return new CertificateInspectResult
        {
            Kind = CertificateContentKind.SshPublicKey,
            KindDescription = $"SSH Public Key ({type})",
            Details = new Dictionary<string, string>
            {
                ["Type"] = type,
                ["Comment"] = comment,
            },
        };
    }
}
