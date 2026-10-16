using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Utilities;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
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

        // Export PFX opcional (PKCS#12): para importar en el almacén de
        // certificados de Windows. La password solo se usa en memoria.
        string? pfxPath = null;
        if (!string.IsNullOrEmpty(request.PfxPassword))
        {
            pfxPath = Path.Combine(_outputDir, $"certificate-{timestamp}.pfx");
            WritePfx(pfxPath, request.PfxPassword, keyPair.Private, certificate);
        }

        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(certificate.GetEncoded()));

        var result = new X509GenerateResult
        {
            CertificatePem = certPem,
            PrivateKeyPath = keyPath,
            PfxPath = pfxPath,
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
    // 19.2 — Verificación de firma PGP detached (cierre del ciclo:
    // crear clave → exportar pública → firmar → IMPORTAR → verificar)
    //
    // Mismo contrato de texto UTF-8 que SignFileWithPgpAsync: simétrico con
    // la firma. La clave pública del firmante debe estar en el keyring;
    // una firma inválida o sin clave es un RESULTADO (Valid=false), no
    // una excepción — igual que pgp.verify (§6).
    // ==================================================================

    public async Task<PCSuite.Core.PGP.PgpVerifyOutcome> VerifyFileWithPgpAsync(
        string inputPath, string signaturePath, CancellationToken ct = default)
    {
        if (!File.Exists(inputPath))
        {
            throw new InvalidOperationException($"No se encontró el archivo original: {inputPath}");
        }
        if (!File.Exists(signaturePath))
        {
            throw new InvalidOperationException($"No se encontró la firma: {signaturePath}");
        }

        var content = await File.ReadAllTextAsync(inputPath, ct).ConfigureAwait(false);
        var signature = await File.ReadAllTextAsync(signaturePath, ct).ConfigureAwait(false);

        var outcome = await _pgp.VerifyAsync(
            new PgpVerifyRequest(content, signature), ct).ConfigureAwait(false);

        _logger?.LogInformation(
            "Firma PGP verificada: {Input} vs {Signature} → {Valid}",
            inputPath, signaturePath, outcome.Valid);

        return outcome;
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
        if (content.Contains("-----BEGIN PGP SIGNATURE-----"))
        {
            return Task.FromResult(InspectPgpSignature(content));
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
    // 19.4 — Conversión de formatos
    //
    // PEM↔DER (certificado), PEM→PFX y PFX→PEM (bundle con clave+cert),
    // OpenSSH→PEM (clave privada openssh-key-v1 → PKCS#8).
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
            case ("pem", "pfx"):
            {
                if (string.IsNullOrEmpty(request.OutputPassword))
                {
                    throw new InvalidOperationException(
                        "PFX requiere password de exportación (OutputPassword).");
                }
                // El PEM de entrada debe ser un bundle con clave + certificado
                // (formato fullchain típico de nginx/openssl).
                var (cert, key) = ParsePemBundle(File.ReadAllText(request.InputPath));
                outputPath = Path.Combine(_outputDir, $"{baseName}-{timestamp}.pfx");
                WritePfx(outputPath, request.OutputPassword, key, cert);
                break;
            }
            case ("pfx", "pem"):
            {
                var (cert, key) = ReadPfx(
                    request.InputPath, request.InputPassword ?? string.Empty);
                // Un solo archivo: clave privada (PKCS#8) + certificado.
                outputPath = Path.Combine(_outputDir, $"{baseName}-{timestamp}.pem");
                File.WriteAllText(outputPath, PemEncode(key) + PemEncode(cert));
                break;
            }
            case ("openssh", "pem"):
            {
                // Clave privada openssh-key-v1 (sin cifrar) → PKCS#8 PEM,
                // usable con OpenSSL/ssh-keygen -p y en cualquier servidor.
                var key = ParseOpenSshPrivateKey(File.ReadAllText(request.InputPath));
                outputPath = Path.Combine(_outputDir, $"{baseName}-{timestamp}.pem");
                File.WriteAllText(outputPath, PemEncode(key));
                break;
            }
            default:
                throw new InvalidOperationException(
                    $"Conversión {request.InputFormat}→{request.OutputFormat} no soportada. " +
                    "Soportadas: pem↔der, pem→pfx, pfx→pem, openssh→pem.");
        }

        _logger?.LogInformation(
            "Convertido {Input} ({From}) → {Output} ({To})",
            request.InputPath, request.InputFormat, outputPath, request.OutputFormat);

        return Task.FromResult(new CertificateConvertResult
        {
            OutputPath = outputPath,
            OutputFormat = request.OutputFormat,
        });
    }

    // ==================================================================
    // 19.5 — CSR (PKCS#10): el usuario genera clave + CSR, la CA firma y
    // vuelve a importar. Todo BouncyCastle en proceso. Los SANs viajan en
    // el atributo estándar extensionRequest (1.2.840.113549.1.9.14) —
    // hoy las CA (Let's Encrypt incluida) SOLO honran los SANs, nunca el CN.
    // ==================================================================

    public Task<CsrGenerateResult> GenerateCsrAsync(
        CsrGenerateRequest request, CancellationToken ct = default)
    {
        var (keyPair, signatureAlgorithm) = GenerateKeyPair(request.Algorithm);
        var subject = BuildX509Name(
            request.CommonName, request.Organization, organizationalUnit: null, request.Country);

        // SANs opcionales como extensionRequest (SET OF Attribute estándar).
        Asn1Set? attributes = null;
        if (request.SubjectAltNames is { Count: > 0 } sans)
        {
            var extensions = new X509Extensions(
                oids: new List<DerObjectIdentifier> { X509Extensions.SubjectAlternativeName },
                values: new List<X509Extension>
                {
                    new(false, new DerOctetString(BuildSans(sans).GetEncoded())),
                });
            attributes = new DerSet(
                new DerSequence(PkcsObjectIdentifiers.Pkcs9AtExtensionRequest, new DerSet(extensions)));
        }

        var csr = new Pkcs10CertificationRequest(
            signatureAlgorithm, subject, keyPair.Public, attributes, keyPair.Private);

        if (!csr.Verify())
        {
            throw new InvalidOperationException(
                "El CSR generado no verifica contra su propia clave: abortando.");
        }

        Directory.CreateDirectory(_outputDir);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var csrPem = PemEncode(csr);
        var csrPath = Path.Combine(_outputDir, $"csr-{timestamp}.pem");
        File.WriteAllText(csrPath, csrPem);

        var keyPem = PemEncode(keyPair.Private);
        var keyPath = Path.Combine(_outputDir, $"csr-private-key-{timestamp}.pem");
        File.WriteAllText(keyPath, keyPem);

        _logger?.LogInformation("CSR generado: {CsrPath} ({Algorithm})", csrPath, request.Algorithm);

        return Task.FromResult(new CsrGenerateResult
        {
            CsrPem = csrPem,
            PrivateKeyPath = keyPath,
        });
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

    /// <summary>
    /// Lee un bundle PEM (clave + certificado en el mismo archivo, estilo
    /// fullchain de nginx) y devuelve el primer certificado y la primera
    /// clave privada que encuentre.
    /// </summary>
    private static (Org.BouncyCastle.X509.X509Certificate Cert, AsymmetricKeyParameter Key)
        ParsePemBundle(string pem)
    {
        Org.BouncyCastle.X509.X509Certificate? cert = null;
        AsymmetricKeyParameter? key = null;

        using var reader = new StringReader(pem);
        var pemReader = new PemReader(reader);
        object? obj;
        while ((obj = pemReader.ReadObject()) is not null)
        {
            switch (obj)
            {
                case Org.BouncyCastle.X509.X509Certificate c when cert is null:
                    cert = c;
                    break;
                case AsymmetricCipherKeyPair pair when key is null:
                    key = pair.Private;
                    break;
                case AsymmetricKeyParameter k when k.IsPrivate && key is null:
                    key = k;
                    break;
            }
        }

        if (cert is null || key is null)
        {
            throw new InvalidOperationException(
                "El PEM debe contener AMBOS: certificado X.509 y clave privada " +
                "(bundle estilo fullchain).");
        }
        return (cert, key);
    }

    /// <summary>Escribe un PKCS#12 con clave + certificado (BC 2.x: builder).</summary>
    private static void WritePfx(
        string path, string password, AsymmetricKeyParameter key, Org.BouncyCastle.X509.X509Certificate cert)
    {
        var store = new Pkcs12StoreBuilder().Build();
        store.SetKeyEntry(
            "pcsuite",
            new AsymmetricKeyEntry(key),
            [new X509CertificateEntry(cert)]);
        using var stream = File.Create(path);
        store.Save(stream, password.ToCharArray(), new SecureRandom());
    }

    /// <summary>Lee un PKCS#12 y devuelve la clave privada + certificado.</summary>
    private static (Org.BouncyCastle.X509.X509Certificate Cert, AsymmetricKeyParameter Key)
        ReadPfx(string path, string password)
    {
        var store = new Pkcs12StoreBuilder().Build();
        using (var stream = File.OpenRead(path))
        {
            try
            {
                store.Load(stream, password.ToCharArray());
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "No se pudo abrir el PFX: password incorrecta o archivo inválido.", ex);
            }
        }

        foreach (var alias in store.Aliases)
        {
            if (!store.IsKeyEntry(alias))
            {
                continue;
            }
            var key = store.GetKey(alias).Key;
            Org.BouncyCastle.X509.X509Certificate? cert = null;
            var chain = store.GetCertificateChain(alias);
            if (chain is { Length: > 0 })
            {
                cert = chain[0].Certificate;
            }
            else if (store.GetCertificate(alias) is { } entry)
            {
                cert = entry.Certificate;
            }
            if (cert is not null)
            {
                return (cert, key);
            }
        }

        throw new InvalidOperationException("El PFX no contiene ninguna entrada con clave + certificado.");
    }

    /// <summary>
    /// Parsea una clave privada OpenSSH (openssh-key-v1, SIN cifrar) desde
    /// su armor PEM. Las claves cifradas con passphrase se rechazan con
    /// mensaje controlado — descifrado bcrypt en próxima iteración.
    /// </summary>
    private static AsymmetricKeyParameter ParseOpenSshPrivateKey(string content)
    {
        var base64 = ExtractArmorBody(
            content, "-----BEGIN OPENSSH PRIVATE KEY-----", "-----END OPENSSH PRIVATE KEY-----");
        if (base64.Length == 0)
        {
            throw new InvalidOperationException(
                "El archivo no contiene un bloque OPENSSH PRIVATE KEY.");
        }

        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("El cuerpo OpenSSH no es base64 válido.");
        }

        try
        {
            var key = OpenSshPrivateKeyUtilities.ParsePrivateKeyBlob(blob);
            if (!key.IsPrivate)
            {
                throw new InvalidOperationException("El blob OpenSSH no contiene una clave privada.");
            }
            return key;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Clave OpenSSH ilegible o CIFRADA con passphrase (aún no soportada).", ex);
        }
    }

    /// <summary>Extrae y concatena el cuerpo base64 entre dos marcadores armor.</summary>
    private static string ExtractArmorBody(string content, string begin, string end)
    {
        var sb = new StringBuilder();
        var inBody = false;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            if (line.StartsWith(begin, StringComparison.Ordinal))
            {
                inBody = true;
                continue;
            }
            if (line.StartsWith(end, StringComparison.Ordinal))
            {
                break;
            }
            if (inBody && !line.StartsWith(':'))
            {
                sb.Append(line);
            }
        }
        return sb.ToString();
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
        if (obj is not Pkcs10CertificationRequest csr)
        {
            throw new InvalidOperationException("El PEM no contiene un CSR.");
        }

        var details = new Dictionary<string, string>
        {
            ["Subject"] = csr.GetCertificationRequestInfo().Subject.ToString(),
        };

        // SANs pedidos (extensionRequest) — lo único que las CA honran hoy.
        try
        {
            var requested = csr.GetRequestedExtensions();
            var san = requested?.GetExtension(X509Extensions.SubjectAlternativeName);
            if (san is not null)
            {
                var generalNames = GeneralNames.GetInstance(san.GetParsedValue());
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
            // sin extensionRequest o ilegible: la inspección sigue
        }

        details["Signature valid"] = csr.Verify().ToString();

        return new CertificateInspectResult
        {
            Kind = CertificateContentKind.X509Csr,
            KindDescription = "Certificate Signing Request (PKCS#10)",
            Details = details,
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

    /// <summary>
    /// Inspector de firmas PGP detached (.sig): extrae el KeyId del armor.
    /// Aquí solo se LEE — para validar contra el original usa el tab
    /// Firmar con PGP → Verificar (necesita la clave pública del firmante).
    /// </summary>
    private static CertificateInspectResult InspectPgpSignature(string armor)
    {
        var details = new Dictionary<string, string>
        {
            ["Cómo validarla"] = "Firmar con PGP → Verificar (requiere la clave pública del firmante)",
        };

        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(armor));
            using var armored = new Org.BouncyCastle.Bcpg.ArmoredInputStream(stream);
            var factory = new Org.BouncyCastle.Bcpg.OpenPgp.PgpObjectFactory(armored);
            if (factory.NextPgpObject() is Org.BouncyCastle.Bcpg.OpenPgp.PgpSignatureList list
                && list.Count > 0)
            {
                var keyId = list[0].KeyId.ToString("X16");
                details["Key ID"] = keyId;
                details["Algoritmo hash"] = list[0].HashAlgorithm.ToString();
            }
        }
        catch
        {
            // armor corrupto: la inspección devuelve el tipo sin detalles
        }

        return new CertificateInspectResult
        {
            Kind = CertificateContentKind.PgpSignature,
            KindDescription = "PGP Signature (detached, armoured)",
            Details = details,
        };
    }
}
