using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PCSuite.Core.PGP;
using PCSuite.Infrastructure.PGP;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Bcpg;
using System.Reflection;
using System.Text;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<PgpKeyringStore>(_ => new PgpKeyringStore(Path.Combine(AppContext.BaseDirectory, "release-keyring")));
builder.Services.AddSingleton<IPgpService, PgpService>();

var host = builder.Build();

// Check for verify-embedded flag
if (args.Contains("--verify-embedded"))
{
    await VerifyEmbeddedKey();
    return;
}

var service = host.Services.GetRequiredService<IPgpService>();

Console.WriteLine("Generando clave PGP para firma de manifiestos de PCSuite...");
Console.WriteLine();

// Parámetros según especificación Task 17.6
var request = new PgpGenerateKeyRequest(
    Name: "AES Studio",
    Email: "releases@aes-studio.dev",
    Algorithm: PgpAlgorithms.Ed25519,
    Bits: null,
    ExpirationDays: null, // Sin caducidad
    Passphrase: null // Sin passphrase para uso automatizado en CI
);

try
{
    var info = await service.GenerateKeyAsync(request, CancellationToken.None);

    Console.WriteLine("✅ Clave generada exitosamente");
    Console.WriteLine();
    Console.WriteLine($"   Fingerprint: {info.Fingerprint}");
    Console.WriteLine($"   Key ID:      {info.KeyId}");
    Console.WriteLine($"   User ID:     {info.UserId}");
    Console.WriteLine($"   Algorithm:   {info.Algorithm}");
    Console.WriteLine($"   Has Private: {info.HasPrivate}");
    Console.WriteLine($"   Can Encrypt: {info.CanEncrypt}");
    Console.WriteLine();

    // Exportar clave pública
    var publicKey = await service.ExportKeyAsync(new PgpExportKeyRequest(info.Fingerprint, IncludePrivate: false), CancellationToken.None);
    Console.WriteLine("-----BEGIN PGP PUBLIC KEY BLOCK-----");
    Console.WriteLine(publicKey.Trim());
    Console.WriteLine("-----END PGP PUBLIC KEY BLOCK-----");
    Console.WriteLine();

    // Exportar clave privada
    var privateKey = await service.ExportKeyAsync(new PgpExportKeyRequest(info.Fingerprint, IncludePrivate: true), CancellationToken.None);
    Console.WriteLine("-----BEGIN PGP PRIVATE KEY BLOCK-----");
    Console.WriteLine(privateKey.Trim());
    Console.WriteLine("-----END PGP PRIVATE KEY BLOCK-----");
    Console.WriteLine();

    // Guardar en archivos para referencia
    var outputDir = Path.Combine(AppContext.BaseDirectory, "output");
    Directory.CreateDirectory(outputDir);

    var fingerprintFile = Path.Combine(outputDir, "fingerprint.txt");
    await File.WriteAllTextAsync(fingerprintFile, info.Fingerprint);
    Console.WriteLine($"Fingerprint guardado en: {fingerprintFile}");

    var publicKeyFile = Path.Combine(outputDir, "releases@aes-studio.dev.pub.asc");
    await File.WriteAllTextAsync(publicKeyFile, publicKey);
    Console.WriteLine($"Clave pública guardada en: {publicKeyFile}");

    var privateKeyFile = Path.Combine(outputDir, "releases@aes-studio.dev.sec.asc");
    await File.WriteAllTextAsync(privateKeyFile, privateKey);
    Console.WriteLine($"Clave privada guardada en: {privateKeyFile}");
    Console.WriteLine();

    // Formato del fingerprint para latest.json (SHA-256 de la clave pública, hex con espacios cada 4 chars)
    var fingerprintFormatted = FormatFingerprintForJson(info.Fingerprint);
    Console.WriteLine($"signingKeyFingerprint (para latest.json): {fingerprintFormatted}");
    Console.WriteLine();

    Console.WriteLine("⚠️  IMPORTANTE:");
    Console.WriteLine("   - La clave PRIVADA (.sec.asc) debe guardarse FUERA del repo (secreto de CI/CD)");
    Console.WriteLine("   - La clave PÚBLICA (.pub.asc) se embeberá en la app como recurso");
    Console.WriteLine("   - El fingerprint debe añadirse a latest.json como 'signingKeyFingerprint'");
    Console.WriteLine("   - El keyring generado está en: " + Path.Combine(AppContext.BaseDirectory, "release-keyring"));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"❌ Error: {ex.Message}");
    Environment.Exit(1);
}

static async Task VerifyEmbeddedKey()
{
    Console.WriteLine("Verificando clave pública embebida en PCSuite.Infrastructure...");
    Console.WriteLine();

    var assembly = Assembly.Load("PCSuite.Infrastructure");
    var resourceName = "PCSuite.Infrastructure.Keys.ManifestSigningKey.asc";

    using var stream = assembly.GetManifestResourceStream(resourceName);
    if (stream is null)
    {
        Console.WriteLine($"❌ Recurso no encontrado: {resourceName}");
        Console.WriteLine("Recursos disponibles:");
        foreach (var name in assembly.GetManifestResourceNames())
        {
            Console.WriteLine($"  - {name}");
        }
        Environment.Exit(1);
    }

    using var reader = new StreamReader(stream);
    var content = reader.ReadToEnd();

    Console.WriteLine("✅ Recurso embebido leído correctamente");
    Console.WriteLine();

    // Parsear con BouncyCastle para obtener fingerprint
    var fingerprint = ComputeFingerprintFromArmored(content);
    Console.WriteLine($"Fingerprint calculado del recurso: {fingerprint}");
    
    // Leer el fingerprint original guardado
    var originalFingerprintPath = Path.Combine(AppContext.BaseDirectory, "output", "fingerprint.txt");
    if (File.Exists(originalFingerprintPath))
    {
        var originalFingerprint = await File.ReadAllTextAsync(originalFingerprintPath);
        Console.WriteLine($"Fingerprint original:              {originalFingerprint.Trim()}");
        
        if (fingerprint.Equals(originalFingerprint.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("✅ COINCIDEN - La clave embebida es correcta");
        }
        else
        {
            Console.WriteLine("❌ NO COINCIDEN - La clave embebida es diferente");
            Environment.Exit(1);
        }
    }
}

static string ComputeFingerprintFromArmored(string armored)
{
    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(armored));
    using var armoredStream = new Org.BouncyCastle.Bcpg.ArmoredInputStream(ms);
    var factory = new PgpObjectFactory(armoredStream);
    var ring = factory.NextPgpObject() as PgpPublicKeyRing;
    if (ring is null) return "ERROR";
    
    var masterKey = ring.GetPublicKey();
    var fp = masterKey.GetFingerprint();
    return Convert.ToHexString(fp);
}

static string FormatFingerprintForJson(string fingerprint)
{
    // fingerprint ya viene en hex uppercase de 40 chars (SHA-1 de la clave maestra)
    // El doc dice "fingerprint SHA-256 de la clave pública" pero el servicio usa SHA-1
    // Formato con espacios cada 4 caracteres: AB12 CD34 EF56 ...
    return string.Join(" ", Enumerable.Range(0, fingerprint.Length / 4)
        .Select(i => fingerprint.Substring(i * 4, 4)));
}