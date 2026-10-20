using PCSuite.Core.PGP;
using PCSuite.Infrastructure.PGP;
using PCSuite.Infrastructure.Updates;

// ============================================================================
// SignRelease (Task 19.6): firma latest.json con la clave de releases usando
// el MISMO stack que la app (PgpService + BouncyCastle en proceso) — sin gpg
// CLI ni dependencias externas. El updater verifica contra la clave pública
// embebida (ManifestSigningKey.asc), así que la firma de aquí ES la firma que
// el updater acepta (misma clave => mismo KeyId).
//
// Modos:
//   (default)  SignRelease <latest.json> [--keyring <dir>] [--email <id>]
//              [--fingerprint <hex>] [--passphrase <pw>]
//              → escribe <latest.json>.sig + <latest.json>.pub e imprime
//                el fingerprint para el JSON.
//   --fingerprint-only    Solo imprime el fingerprint formateado (publicar.ps1
//                         lo inyecta en el JSON ANTES de firmar: la firma cubre
//                         el contenido final exacto).
//   --import <armored.asc> Importa una clave armoured (pública o privada) al
//                         keyring y sale. Uso único: traer la clave de
//                         releases desde GenerateReleaseKey/output al keyring
//                         de la app (%APPDATA%\PCSuite\pgp).
//   --verify              Verifica <latest.json> + <latest.json>.sig contra
//                         la clave pública EMBEBIDA (la que validarán los
//                         updaters instalados). TOFU en temporal: no toca
//                         el estado de la app. Úsalo como sanity check del
//                         pipeline antes de publicar.
// ============================================================================

var fingerprintOnly = args.Contains("--fingerprint-only");
var verify = args.Contains("--verify");
var importIndex = Array.IndexOf(args, "--import");

var keyringDir = GetOption("keyring")
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PCSuite", "pgp");
var email = GetOption("email") ?? "releases@aes-studio.dev";
var fingerprint = GetOption("fingerprint");
var passphrase = GetOption("passphrase");

// El modo --import CREA el keyring de destino; el resto lo exige existente.
if (importIndex < 0 && !Directory.Exists(keyringDir))
{
    Console.Error.WriteLine($"Keyring no encontrado: {keyringDir}");
    Console.Error.WriteLine("Genera la clave con GenerateReleaseKey, impórtala con --import, " +
        "o pasa --keyring <dir>.");
    return 1;
}
Directory.CreateDirectory(keyringDir);

var pgp = new PgpService(new PgpKeyringStore(keyringDir));

// ---------------- Modo import: traer la clave de releases al keyring ----------------
// El keyring de destino se CREA si no existe (primera importación).
if (importIndex >= 0)
{
    var importPath = importIndex + 1 < args.Length ? args[importIndex + 1] : null;
    if (string.IsNullOrWhiteSpace(importPath) || !File.Exists(importPath))
    {
        Console.Error.WriteLine("--import requiere la ruta del bloque armoured (.asc).");
        return 1;
    }
    Directory.CreateDirectory(keyringDir);
    var armored = await File.ReadAllTextAsync(importPath);
    var imported = await pgp.ImportKeyAsync(new PgpImportKeyRequest(armored));
    Console.WriteLine($"✅ Clave importada al keyring: {keyringDir}");
    Console.WriteLine($"   Fingerprint: {imported.Fingerprint}");
    Console.WriteLine($"   User ID:     {imported.UserId}");
    Console.WriteLine($"   Has Private: {imported.HasPrivate}");
    return 0;
}

var manifestPath = args.FirstOrDefault(a => !a.StartsWith("--"));

// --fingerprint-only NO lleva manifiesto (publicar.ps1 solo quiere el
// fingerprint para INYECTARLO en el JSON antes de firmar); los demás
// modos sí exigen un latest.json existente. Bug 2026-10-19: este guard
// se perdió en la reescritura que añadió --import/--verify y el paso
// [4/6] de publicar.bat abortaba con el "Uso:".
if (!fingerprintOnly && (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath)))
{
    Console.Error.WriteLine("Uso: SignRelease <latest.json> [--keyring <dir>] " +
        "[--email <id>] [--fingerprint <hex>] [--passphrase <pw>] | --import <asc> | --verify");
    Console.Error.WriteLine("El archivo de manifiesto no existe: " + manifestPath);
    return 1;
}

// ---------------- Modo verify: sanity check contra la clave EMBEBIDA ----------------
// El mismo verificador que ejecutan los updaters instalados — si pasa aquí,
// pasará allí. TOFU a temporal para no tocar el estado de la app.
if (verify)
{
    var verifySigPath = manifestPath + ".sig";
    if (!File.Exists(verifySigPath))
    {
        Console.Error.WriteLine($"No existe la firma: {verifySigPath}");
        return 1;
    }
    var verifier = new ManifestSignatureVerifier(
        null, // clave embebida de producción
        Path.Combine(Path.GetTempPath(), "pcsuite-signrelease-tofu.txt"));
    // MISMA semántica que el updater instalado: firma sobre el texto decodificado
    // (ReadAllText elimina BOM igual que ReadAsStringAsync del HttpClient) y
    // re-codificado a UTF-8. Así --verify aprueba exactamente lo que aprobará
    // cada updater, ni más ni menos.
    var manifestText = await File.ReadAllTextAsync(manifestPath!);
    var result = await verifier.VerifyAsync(
        System.Text.Encoding.UTF8.GetBytes(manifestText),
        await File.ReadAllTextAsync(verifySigPath));
    if (!result.Success)
    {
        Console.Error.WriteLine($"❌ Firma inválida contra la clave embebida: {result.Error}");
        Console.Error.WriteLine("   NO publiques este manifiesto: todos los updaters lo rechazarían.");
        return 1;
    }
    Console.WriteLine("✅ Firma verificada contra la clave embebida (la que usan los updaters).");
    Console.WriteLine($"   Fingerprint: {result.Fingerprint}");
    if (!result.FingerprintMatch)
    {
        Console.WriteLine("   ⚠ Nota: el fingerprint difiere del TOFU previo de este equipo.");
    }
    return 0;
}

// ---------------- Selección de la clave de firma ----------------
var keys = await pgp.ListKeysAsync();
PgpKeyInfo? signingKey = null;
if (!string.IsNullOrWhiteSpace(fingerprint))
{
    signingKey = keys.FirstOrDefault(k =>
        k.Fingerprint.Equals(fingerprint.Trim(), StringComparison.OrdinalIgnoreCase));
    if (signingKey is null)
    {
        Console.Error.WriteLine($"La clave {fingerprint} no está en el keyring.");
        return 1;
    }
}
else
{
    var candidates = keys.Where(k => k.HasPrivate).ToList();
    signingKey = candidates.FirstOrDefault(k =>
                      string.Equals(k.Email, email, StringComparison.OrdinalIgnoreCase))
                  ?? (candidates.Count == 1 ? candidates[0] : null);
    if (signingKey is null)
    {
        Console.Error.WriteLine($"No hay clave privada para '{email}' en {keyringDir}. " +
            "Genera una con GenerateReleaseKey, impórtala con --import, o usa --fingerprint.");
        return 1;
    }
}

// ---------------- Modo fingerprint-only ----------------
var manifestJson = fingerprintOnly ? "" : await File.ReadAllTextAsync(manifestPath!);
if (fingerprintOnly)
{
    Console.WriteLine(FormatFingerprint(signingKey.Fingerprint));
    return 0;
}

// ---------------- Firma del manifiesto ----------------
var signature = await pgp.SignAsync(
    new PgpSignRequest(manifestJson, signingKey.Fingerprint, passphrase));

var sigPath = manifestPath + ".sig";
await File.WriteAllTextAsync(sigPath, signature);
Console.WriteLine($"✅ Firma generada: {sigPath}");

var publicKey = await pgp.ExportKeyAsync(
    new PgpExportKeyRequest(signingKey.Fingerprint, IncludePrivate: false));
var pubPath = manifestPath + ".pub";
await File.WriteAllTextAsync(pubPath, publicKey);
Console.WriteLine($"✅ Clave pública exportada: {pubPath}");

Console.WriteLine();
Console.WriteLine($"Fingerprint: {signingKey.Fingerprint}");
Console.WriteLine($"signingKeyFingerprint (JSON): {FormatFingerprint(signingKey.Fingerprint)}");
return 0;

string? GetOption(string name)
{
    var index = Array.IndexOf(args, "--" + name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string FormatFingerprint(string fingerprint) =>
    string.Join(" ", Enumerable.Range(0, fingerprint.Length / 4)
        .Select(i => fingerprint.Substring(i * 4, 4)));
