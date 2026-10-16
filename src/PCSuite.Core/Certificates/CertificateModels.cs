namespace PCSuite.Core.Certificates;

/// <summary>Formatos de archivo de certificado/clave que PCSuite reconoce.</summary>
public static class CertificateFormats
{
    public const string Pem = "pem";
    public const string Der = "der";
    public const string Pfx = "pfx";
    public const string OpenSsh = "openssh";
    public const string Asc = "asc";

    public static readonly IReadOnlyList<string> Exportable =
    [
        Pem, Der, Pfx, OpenSsh, Asc,
    ];
}

/// <summary>Algoritmos de clave admitidos para certificados X.509 (Task 19.1).</summary>
public static class X509Algorithms
{
    public const string Rsa2048 = "rsa2048";
    public const string Rsa4096 = "rsa4096";
    public const string EcdsaP256 = "ecdsa-p256";
    public const string EcdsaP384 = "ecdsa-p384";
    public const string Ed25519 = "ed25519";

    public static readonly IReadOnlyList<string> All =
    [
        Rsa2048, Rsa4096, EcdsaP256, EcdsaP384, Ed25519,
    ];
}

/// <summary>Resultado de generar un certificado X.509 autofirmado (Task 19.1).</summary>
public sealed record X509GenerateResult
{
    /// <summary>Certificado en formato PEM (-----BEGIN CERTIFICATE-----).</summary>
    public required string CertificatePem { get; init; }

    /// <summary>Clave privada en formato PEM (PKCS#8). NUNCA viaja por el bridge sin
    /// que el usuario la pida explícitamente: el servicio la escribe a archivo
    /// y devuelve la ruta + fingerprint.</summary>
    public required string PrivateKeyPath { get; init; }

    /// <summary>PKCS#12 (.pfx) con password, si se pidió export.</summary>
    public string? PfxPath { get; init; }

    /// <summary>Fingerprint SHA-256 del certificado (hex minúscula).</summary>
    public required string Fingerprint { get; init; }

    public required string SerialNumber { get; init; }

    public required string Subject { get; init; }

    public required DateTimeOffset ValidFrom { get; init; }

    public required DateTimeOffset ValidTo { get; init; }

    public required string Algorithm { get; init; }
}

/// <summary>Solicitud de generación de certificado X.509 autofirmado.</summary>
public sealed record X509GenerateRequest
{
    public required string Algorithm { get; init; }

    /// <summary>Common Name (ej. "localhost", "*.aes-studio.dev").</summary>
    public required string CommonName { get; init; }

    public string? Organization { get; init; }
    public string? OrganizationalUnit { get; init; }
    public string? Country { get; init; }

    /// <summary>Subject Alternative Names (DNS, IP o email).</summary>
    public IReadOnlyList<string>? SubjectAltNames { get; init; }

    /// <summary>Días de validez (default 365).</summary>
    public int ValidityDays { get; init; } = 365;

    /// <summary>Exportar PFX con password (opcional).</summary>
    public string? PfxPassword { get; init; }
}

/// <summary>Resultado de firmar un archivo con clave PGP (Task 19.2 / 17.6).</summary>
public sealed record PgpSignFileResult
{
    /// <summary>Ruta del archivo de firma (.sig, ASCII-armored detached).</summary>
    public required string SignaturePath { get; init; }

    /// <summary>Fingerprint de la clave firmante.</summary>
    public required string Fingerprint { get; init; }
}

/// <summary>Tipo de contenido reconocido por el inspector universal (Task 19.3).</summary>
public enum CertificateContentKind
{
    Unknown,
    X509Certificate,
    X509Csr,
    PrivateKey,
    PgpPublicKey,
    PgpPrivateKey,
    SshPublicKey,
}

/// <summary>Resultado de inspección universal: un endpoint que reconoce cualquier formato (Task 19.3).</summary>
public sealed record CertificateInspectResult
{
    public required CertificateContentKind Kind { get; init; }

    /// <summary>Descripción legible: "X.509 Certificate (RSA 4096)", "PGP Public Key (Ed25519)", etc.</summary>
    public required string KindDescription { get; init; }

    /// <summary>Fingerprint SHA-256 (hex minúscula) cuando aplica.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>Detalles estructurados según el tipo: Subject, Issuer, Validez, SANs, Algorithm, Identity, KeyId…</summary>
    public required IReadOnlyDictionary<string, string> Details { get; init; }
}

/// <summary>Solicitud de conversión de formato (Task 19.4).</summary>
public sealed record CertificateConvertRequest
{
    public required string InputPath { get; init; }
    public required string InputFormat { get; init; }
    public required string OutputFormat { get; init; }

    /// <summary>Password del archivo de entrada (PFX, clave cifrada).</summary>
    public string? InputPassword { get; init; }

    /// <summary>Password del archivo de salida (PFX export).</summary>
    public string? OutputPassword { get; init; }
}

/// <summary>Resultado de conversión de formato.</summary>
public sealed record CertificateConvertResult
{
    public required string OutputPath { get; init; }
    public required string OutputFormat { get; init; }
}

/// <summary>Solicitud de CSR (Task 19.5).</summary>
public sealed record CsrGenerateRequest
{
    public required string CommonName { get; init; }
    public string? Organization { get; init; }
    public string? Country { get; init; }
    public required string Algorithm { get; init; }
    public IReadOnlyList<string>? SubjectAltNames { get; init; }
}

/// <summary>Resultado de generar CSR.</summary>
public sealed record CsrGenerateResult
{
    /// <summary>CSR en formato PEM (-----BEGIN CERTIFICATE REQUEST-----).</summary>
    public required string CsrPem { get; init; }

    /// <summary>Ruta de la clave privada generada (jamás en el bridge sin pedir).</summary>
    public required string PrivateKeyPath { get; init; }
}

/// <summary>Interfaz del módulo Certificados (Task 019).</summary>
public interface ICertificateService
{
    /// <summary>Genera un certificado X.509 autofirmado (Task 19.1).</summary>
    Task<X509GenerateResult> GenerateX509Async(X509GenerateRequest request, CancellationToken ct = default);

    /// <summary>Firma un archivo con una clave PGP privada del keyring (Task 19.2 / 17.6).</summary>
    Task<PgpSignFileResult> SignFileWithPgpAsync(string fingerprint, string inputPath, string? passphrase, CancellationToken ct = default);

    /// <summary>Inspecciona un archivo de certificado/clave: reconoce el formato automáticamente (Task 19.3).</summary>
    Task<CertificateInspectResult> InspectAsync(string path, string? password, CancellationToken ct = default);

    /// <summary>Convierte entre formatos: PEM↔DER↔PFX↔OpenSSH (Task 19.4).</summary>
    Task<CertificateConvertResult> ConvertAsync(CertificateConvertRequest request, CancellationToken ct = default);

    /// <summary>Genera un CSR (Certificate Signing Request PKCS#10) + clave privada (Task 19.5).</summary>
    Task<CsrGenerateResult> GenerateCsrAsync(CsrGenerateRequest request, CancellationToken ct = default);
}
