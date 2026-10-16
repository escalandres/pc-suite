using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PCSuite.Infrastructure.LocalShare;

/// <summary>
/// Gestiona el certificado TLS autofirmado de Local Share.
/// Genera uno por instalación al primer arranque, persiste la clave privada
/// protegida con DPAPI (nunca en texto plano), y expone la huella SHA-256
/// para el emparejamiento TOFU (Trust On First Use).
/// </summary>
public sealed class CertificateStore
{
    private const string CertificateFileName = "localshare-cert.pfx";

    private readonly string _storageDirectory;
    private X509Certificate2? _certificate;

    public CertificateStore(string storageDirectory)
    {
        _storageDirectory = storageDirectory;
        Directory.CreateDirectory(storageDirectory);
    }

    /// <summary>Certificado actual (genera al primer uso si no existe).</summary>
    public X509Certificate2 Certificate => _certificate ??= LoadOrCreate();

    /// <summary>Huella SHA-256 del certificado (hex minúscula, para emparejamiento).</summary>
    public string Thumbprint
    {
        get
        {
            var hash = SHA256.HashData(Certificate.Export(X509ContentType.Cert));
            return Convert.ToHexStringLower(hash);
        }
    }

    private X509Certificate2 LoadOrCreate()
    {
        var path = Path.Combine(_storageDirectory, CertificateFileName);
        if (File.Exists(path))
        {
            try
            {
                return X509CertificateLoader.LoadPkcs12FromFile(
                    path,
                    null,
                    // Clave persistida en el almacén del usuario: SChannel
                    // (TLS a nivel OS) no puede usar claves efímeras.
                    KeyStorageFlags);
            }
            catch
            {
                // Certificado corrupto o de otra instalación: regenerar.
            }
        }

        var cert = Generate();
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx));
        return cert;
    }

    /// <summary>
    /// Flags de carga del PFX. UserKeySet persiste la clave privada en el
    /// almacén de claves del usuario (protegida por DPAPI del perfil).
    /// EphemeralKeySet NO sirve aquí: SChannel no soporta claves efímeras
    /// para el handshake TLS y falla con "platform does not support
    /// ephemeral keys".
    /// </summary>
    private const X509KeyStorageFlags KeyStorageFlags =
        X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet;

    private static X509Certificate2 Generate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=PCSuite Local Share",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(5);
        using var cert = request.CreateSelfSigned(notBefore, notAfter);

        // Exportar a PFX y recargar con la clave PERSISTIDA (UserKeySet).
        // El cert que devuelve CreateSelfSigned lleva la clave efímera del
        // RSA.Create: SChannel la rechaza. El PFX en disco se protege con
        // la ACL del directorio de datos del usuario (%LOCALAPPDATA%).
        // X509CertificateLoader (SYSLIB0057): el ctor de X509Certificate2
        // está obsoleto desde .NET 9.
        return X509CertificateLoader.LoadPkcs12(
            cert.Export(X509ContentType.Pfx),
            null,
            KeyStorageFlags);
    }
}
