using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using PCSuite.Contracts;
using PCSuite.Core.PGP;
using PCSuite.Infrastructure.PGP;
// El alias evita la ambigüedad con Org.BouncyCastle.Bcpg.OpenPgp.PgpException.
using PgpException = PCSuite.Core.PGP.PgpException;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests de <see cref="PgpService"/> (Task 009 / §6): generación de pares
/// ed25519/RSA vía BouncyCastle, validación de entrada con mensajes estáticos
/// (jamás la contraseña), round-trip export/import hacia un keyring nuevo,
/// prefijo de fingerprint, exportación privada restringida e importaciones
/// corruptas. Todo en carpetas temporales: los tests no tocan %APPDATA% real.
/// </summary>
public sealed class PgpServiceTests : IDisposable
{
    private const string Name = "Ana Test";

    private const string Email = "ana@test.es";

    private const string UserId = "Ana Test <ana@test.es>";

    /// <summary>Marcador que jamás debe aparecer en un mensaje de error.</summary>
    private const string MarkerPassphrase = "MARCADOR-SECRETO-QUE-NUNCA-APARECE";

    private readonly TempExecutablePath _temp = new();

    public void Dispose() => _temp.Dispose();

    // ------------------------------------------------------------------
    // Generación
    // ------------------------------------------------------------------

    [Fact]
    public async Task GenerateKey_Ed25519_CreatesCompleteKeyring_AndParsesBack()
    {
        var (store, service) = CreateKeyring();

        var info = await service.GenerateKeyAsync(NewKey());

        Assert.Matches("^[0-9A-F]{40}$", info.Fingerprint);
        Assert.Matches("^[0-9A-F]{16}$", info.KeyId);
        Assert.Equal(UserId, info.UserId);
        Assert.Equal(Name, info.Name);
        Assert.Equal(Email, info.Email);
        Assert.Equal(PgpAlgorithms.Ed25519, info.Algorithm);
        Assert.Null(info.Bits); // Bits se expone solo para RSA en la UI.
        Assert.True(info.HasPrivate);
        Assert.True(info.CanEncrypt);
        Assert.Null(info.ExpiresAt);
        Assert.Single(store.Load());

        var publicArmored = store.ReadPublicKey(info.Fingerprint);
        var secretArmored = store.ReadSecretKey(info.Fingerprint);
        Assert.NotNull(publicArmored);
        Assert.Contains("-----BEGIN PGP PUBLIC KEY BLOCK-----", publicArmored);
        Assert.Contains("-----END PGP PUBLIC KEY BLOCK-----", publicArmored);
        Assert.NotNull(secretArmored);
        Assert.Contains("-----BEGIN PGP PRIVATE KEY BLOCK-----", secretArmored);
        Assert.Contains("-----END PGP PRIVATE KEY BLOCK-----", secretArmored);

        // Round-trip de parseo: anillo con primaria + subclave, S2K ≠ 0 (BC no
        // admite usage 0: sin contraseña se protege con cadena vacía),
        // autofirma de certificación SHA-256 y subclave ECDH de cifrado.
        var ring = ReadSecretRing(secretArmored);
        var keys = ring.GetSecretKeys().ToList();
        Assert.Equal(2, keys.Count);

        var master = keys[0];
        Assert.True(master.S2kUsage != 0, "S2K usage 0 = clave sin proteger.");
        Assert.Equal(PublicKeyAlgorithmTag.EdDsa_Legacy, master.PublicKey.Algorithm);

        PgpSignature? selfSignature = null;
        foreach (var signature in master.PublicKey.GetSignaturesForId(UserId))
        {
            selfSignature = signature;
            break;
        }

        Assert.NotNull(selfSignature);
        Assert.InRange(
            (int)selfSignature.SignatureType,
            (int)PgpSignature.DefaultCertification,
            (int)PgpSignature.PositiveCertification);
        Assert.Equal(HashAlgorithmTag.Sha256, selfSignature.HashAlgorithm);
        Assert.True(
            selfSignature.GetHashedSubPackets().GetKeyFlags() == 3,
            "primaria debe tener flags firmar|certificar");

        var subkey = keys[1];
        Assert.True(subkey.PublicKey.IsEncryptionKey);
        Assert.Equal(PublicKeyAlgorithmTag.ECDH, subkey.PublicKey.Algorithm);

        PgpSignature? binding = null;
        foreach (var signature in subkey.PublicKey.GetSignatures())
        {
            binding = signature;
            break;
        }

        Assert.NotNull(binding);
        Assert.Equal((int)PgpSignature.SubkeyBinding, (int)binding.SignatureType);
        Assert.Equal(HashAlgorithmTag.Sha256, binding.HashAlgorithm);
    }

    [Fact]
    public async Task GenerateKey_Rsa_DefaultsTo3072_ExpiresIn90Days_Protected()
    {
        var (store, service) = CreateKeyring();

        var info = await service.GenerateKeyAsync(
            NewKey(algorithm: PgpAlgorithms.Rsa, expirationDays: 90, passphrase: "secreto-de-prueba"));

        Assert.Equal(PgpAlgorithms.Rsa, info.Algorithm);
        Assert.Equal(3072, info.Bits);
        Assert.NotNull(info.ExpiresAt);
        var lifetime = info.ExpiresAt.Value - info.CreatedAt;
        Assert.True(Math.Abs(lifetime.TotalDays - 90) < 0.05, $"caducidad real: {lifetime.TotalDays} días");

        var secretArmored = store.ReadSecretKey(info.Fingerprint);
        Assert.NotNull(secretArmored);
        var master = ReadSecretRing(secretArmored).GetSecretKey();
        Assert.True(master.S2kUsage != 0, "clave con contraseña debe tener S2K usage distinto de 0.");
        Assert.Equal(3072, master.PublicKey.BitStrength);
    }

    [Theory]
    [InlineData("", "ana@test.es", null, null, null, null)] // nombre vacío
    [InlineData("Ana <x>", "ana@test.es", null, null, null, null)] // '<' en el nombre
    [InlineData("Ana", "no-es-email", null, null, null, null)] // email inválido
    [InlineData("Ana", "ana@test.es", "dsa", null, null, null)] // algoritmo fuera de allow-list
    [InlineData("Ana", "ana@test.es", "rsa", 1024, null, null)] // bits no admitidos
    [InlineData("Ana", "ana@test.es", "ed25519", 2048, null, null)] // bits con ed25519
    [InlineData("Ana", "ana@test.es", null, null, 0, null)] // caducidad 0
    [InlineData("Ana", "ana@test.es", null, null, 4000, null)] // caducidad > 3650
    public async Task GenerateKey_InvalidRequest_FailsWithPgpFailed_AndWritesNothing(
        string name,
        string email,
        string? algorithm,
        int? bits,
        int? expirationDays,
        string? passphrase)
    {
        var (store, service) = CreateKeyring();

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            service.GenerateKeyAsync(NewKey(name, email, algorithm, bits, expirationDays, passphrase)));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(exception.ErrorMessage));
        Assert.Empty(Directory.GetFiles(_temp.DirectoryPath, "*.asc"));
        Assert.False(File.Exists(Path.Combine(_temp.DirectoryPath, "keyring.json")));
        Assert.Empty(store.Load());
    }

    [Fact]
    public async Task GenerateKey_PassphraseTooLong_FailsWithoutEchoingPassphrase()
    {
        var (_, service) = CreateKeyring();
        var passphrase = MarkerPassphrase + new string('a', 1024);

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            service.GenerateKeyAsync(NewKey(passphrase: passphrase)));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
        Assert.DoesNotContain(MarkerPassphrase, exception.ErrorMessage);
        Assert.DoesNotContain(passphrase, exception.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // Listado
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListKeys_EmptyKeyring_ReturnsEmpty()
    {
        var (_, service) = CreateKeyring();

        var keys = await service.ListKeysAsync();

        Assert.Empty(keys);
    }

    [Fact]
    public async Task ListKeys_TwoKeys_ReturnsBothNewestFirst()
    {
        var (_, service) = CreateKeyring();
        await service.GenerateKeyAsync(NewKey());
        await service.GenerateKeyAsync(NewKey(name: "Otro Usuario", email: "otro@test.es"));

        var keys = await service.ListKeysAsync();

        Assert.Equal(2, keys.Count);
        Assert.Equal(2, keys.Select(key => key.Fingerprint).Distinct().Count());
        // Orden no estricto: dos claves generadas en el mismo segundo empatan.
        Assert.True(keys[0].CreatedAt >= keys[1].CreatedAt, "la más reciente primero.");
    }

    // ------------------------------------------------------------------
    // Exportación / importación
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExportKey_PublicByPrefix_ImportIntoFreshStore_RoundTrips()
    {
        var (_, service) = CreateKeyring();
        var info = await service.GenerateKeyAsync(NewKey());

        // Prefijo corto: el matching debe ser por prefijo, no por igualdad exacta.
        var armored = await service.ExportKeyAsync(
            new PgpExportKeyRequest(info.Fingerprint[..8], IncludePrivate: false));

        Assert.Contains("-----BEGIN PGP PUBLIC KEY BLOCK-----", armored);
        Assert.DoesNotContain("-----BEGIN PGP PRIVATE", armored);

        using var fresh = new TempExecutablePath();
        var freshStore = new PgpKeyringStore(fresh.DirectoryPath);
        var freshService = new PgpService(freshStore);

        var imported = await freshService.ImportKeyAsync(new PgpImportKeyRequest(armored));

        Assert.Equal(info.Fingerprint, imported.Fingerprint);
        Assert.Equal(info.UserId, imported.UserId);
        Assert.Equal(PgpAlgorithms.Ed25519, imported.Algorithm);
        Assert.False(imported.HasPrivate);
        Assert.True(imported.CanEncrypt);
        Assert.Single(freshStore.Load());
        Assert.NotNull(freshStore.ReadPublicKey(imported.Fingerprint));
        Assert.Null(freshStore.ReadSecretKey(imported.Fingerprint));
    }

    [Fact]
    public async Task ExportKey_Secret_RestoresPrivateMaterialOnImport()
    {
        var (_, service) = CreateKeyring();
        var info = await service.GenerateKeyAsync(NewKey());

        var armored = await service.ExportKeyAsync(
            new PgpExportKeyRequest(info.Fingerprint, IncludePrivate: true));

        Assert.Contains("-----BEGIN PGP PRIVATE KEY BLOCK-----", armored);

        using var fresh = new TempExecutablePath();
        var freshStore = new PgpKeyringStore(fresh.DirectoryPath);
        var freshService = new PgpService(freshStore);

        var imported = await freshService.ImportKeyAsync(new PgpImportKeyRequest(armored));

        Assert.Equal(info.Fingerprint, imported.Fingerprint);
        Assert.True(imported.HasPrivate, "la importación privada debe conservar la copia privada.");
        Assert.Single(freshStore.Load());
        Assert.NotNull(freshStore.ReadSecretKey(imported.Fingerprint));
        Assert.NotNull(freshStore.ReadPublicKey(imported.Fingerprint)); // derivada del anillo privado.
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF01234567")] // fingerprint inexistente
    [InlineData("FFFFFFFF")] // prefijo que no coincide con la clave generada
    public async Task ExportKey_WhenFingerprintUnknown_Fails(string fingerprint)
    {
        var (_, service) = CreateKeyring();
        await service.GenerateKeyAsync(NewKey()); // hay una clave distinta en el keyring.

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            service.ExportKeyAsync(new PgpExportKeyRequest(fingerprint, IncludePrivate: false)));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(exception.ErrorMessage));
    }

    [Fact]
    public async Task ExportKey_WhenOnlyPublicImported_FailsForPrivate()
    {
        var (_, service) = CreateKeyring();
        var info = await service.GenerateKeyAsync(NewKey());
        var publicArmored = await service.ExportKeyAsync(
            new PgpExportKeyRequest(info.Fingerprint, IncludePrivate: false));

        using var fresh = new TempExecutablePath();
        var freshService = new PgpService(new PgpKeyringStore(fresh.DirectoryPath));
        await freshService.ImportKeyAsync(new PgpImportKeyRequest(publicArmored));

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            freshService.ExportKeyAsync(new PgpExportKeyRequest(info.Fingerprint, IncludePrivate: true)));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
        Assert.DoesNotContain("MARCADOR", exception.ErrorMessage); // mensaje estático, sin datos.
    }

    // ------------------------------------------------------------------
    // Importación de material inválido
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("esto no es una clave PGP, es texto cualquiere pegado a mano.")]
    [InlineData("-----BEGIN PGP PUBLIC KEY BLOCK-----\nbasura basura\n-----END PGP PUBLIC KEY BLOCK-----")]
    public async Task ImportKey_WhenNotAValidRing_Fails(string armored)
    {
        var (_, service) = CreateKeyring();

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            service.ImportKeyAsync(new PgpImportKeyRequest(armored)));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(exception.ErrorMessage));
        // Nunca material a medias ni índice parcial: o todo o nada.
        Assert.Empty(Directory.GetFiles(_temp.DirectoryPath, "*.asc"));
        Assert.False(File.Exists(Path.Combine(_temp.DirectoryPath, "keyring.json")));
    }

    [Fact]
    public async Task ImportKey_WhenOversized_Fails()
    {
        var (_, service) = CreateKeyring();
        var armored = "-----BEGIN PGP PUBLIC KEY BLOCK-----\n" + new string('A', 1_048_576);

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            service.ImportKeyAsync(new PgpImportKeyRequest(armored)));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
        Assert.Empty(Directory.GetFiles(_temp.DirectoryPath, "*.asc"));
    }

    // ------------------------------------------------------------------
    // Criptografía (9.6): cifrar / descifrar / firmar / verificar
    // ------------------------------------------------------------------

    [Fact]
    public async Task EncryptDecrypt_RoundTrip_ReturnsOriginal_AndHidesPlaintext()
    {
        var (_, service) = CreateKeyring();
        var info = await service.GenerateKeyAsync(NewKey(passphrase: "contraseña-de-prueba"));
        const string text = "Mensaje secreto con ñ y acentos: ¡funciona!";

        var armored = await service.EncryptAsync(
            new PgpEncryptRequest(text, [info.Fingerprint[..12]]));

        Assert.StartsWith("-----BEGIN PGP MESSAGE-----", armored, StringComparison.Ordinal);
        Assert.DoesNotContain(text, armored); // el texto jamás aparece en claro
        Assert.DoesNotContain("secreto", armored);

        var plain = await service.DecryptAsync(new PgpDecryptRequest(armored, "contraseña-de-prueba"));

        Assert.Equal(text, plain);
    }

    [Fact]
    public async Task Encrypt_ExactEmailRecipient_IgnoresCase()
    {
        var (_, service) = CreateKeyring();
        await service.GenerateKeyAsync(NewKey());

        var armored = await service.EncryptAsync(
            new PgpEncryptRequest("hola", ["ANA@TEST.ES"]));

        Assert.Equal("hola", await service.DecryptAsync(new PgpDecryptRequest(armored, null)));
    }

    [Fact]
    public async Task Encrypt_TwoRecipients_PrefixedFingerprintAndEmail_Decrypts()
    {
        var (_, service) = CreateKeyring();
        var ana = await service.GenerateKeyAsync(NewKey());
        var otro = await service.GenerateKeyAsync(NewKey(name: "Otro Usuario", email: "otro@test.es"));

        var armored = await service.EncryptAsync(
            new PgpEncryptRequest("para los dos", [ana.Fingerprint[..10], "otro@test.es"]));

        Assert.Equal("para los dos", await service.DecryptAsync(new PgpDecryptRequest(armored, null)));
    }

    [Fact]
    public async Task Encrypt_UnknownRecipient_Fails()
    {
        var (_, service) = CreateKeyring();
        await service.GenerateKeyAsync(NewKey()); // hay clave, pero no la pedida.

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            service.EncryptAsync(new PgpEncryptRequest("x", ["nadie@test.es"])));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(exception.ErrorMessage));
        Assert.DoesNotContain("   at ", exception.ErrorMessage);
    }

    [Fact]
    public async Task Encrypt_WithoutRecipients_Fails()
    {
        var (_, service) = CreateKeyring();
        await service.GenerateKeyAsync(NewKey());

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            service.EncryptAsync(new PgpEncryptRequest("x", [])));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task Encrypt_OversizedText_Fails()
    {
        var (_, service) = CreateKeyring();
        var info = await service.GenerateKeyAsync(NewKey());

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            service.EncryptAsync(new PgpEncryptRequest(new string('a', 1_048_577), [info.Fingerprint])));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("esto no es un mensaje PGP, es texto cualquiera pegado a mano.")]
    [InlineData("-----BEGIN PGP MESSAGE-----\nAAAAAAAAAAAAAAAA\n-----END PGP MESSAGE-----")]
    public async Task Decrypt_WhenNotAValidMessage_Fails(string armored)
    {
        var (_, service) = CreateKeyring();
        await service.GenerateKeyAsync(NewKey()); // hay claves privadas: no es ese el problema.

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            service.DecryptAsync(new PgpDecryptRequest(armored, null)));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(exception.ErrorMessage));
        Assert.DoesNotContain("   at ", exception.ErrorMessage);
    }

    [Fact]
    public async Task Decrypt_WrongPassphrase_FailsWithoutEchoingPassphrase()
    {
        var (_, service) = CreateKeyring();
        var info = await service.GenerateKeyAsync(NewKey(passphrase: MarkerPassphrase));
        var armored = await service.EncryptAsync(new PgpEncryptRequest("hola", [info.Fingerprint]));
        const string wrong = "otra-contraseña";

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            service.DecryptAsync(new PgpDecryptRequest(armored, wrong)));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
        Assert.DoesNotContain(wrong, exception.ErrorMessage);
        Assert.DoesNotContain(MarkerPassphrase, exception.ErrorMessage);
        Assert.DoesNotContain("   at ", exception.ErrorMessage);
    }

    [Fact]
    public async Task Decrypt_WhenMessageEncryptedForForeignKey_Fails()
    {
        // La tienda solo conoce la pública de Ana: la privada vive en otra.
        var (_, service) = CreateKeyring();
        var ana = await service.GenerateKeyAsync(NewKey());
        var publicArmored = await service.ExportKeyAsync(
            new PgpExportKeyRequest(ana.Fingerprint, IncludePrivate: false));

        using var fresh = new TempExecutablePath();
        var freshService = new PgpService(new PgpKeyringStore(fresh.DirectoryPath));
        await freshService.ImportKeyAsync(new PgpImportKeyRequest(publicArmored));
        await freshService.GenerateKeyAsync(NewKey(name: "Beto", email: "beto@test.es"));

        var armored = await freshService.EncryptAsync(new PgpEncryptRequest("para Ana", [ana.Fingerprint]));

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            freshService.DecryptAsync(new PgpDecryptRequest(armored, null)));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(exception.ErrorMessage));
    }

    [Fact]
    public async Task SignVerify_RoundTrip_ByPrefix_NoPassphraseOnOpenKey()
    {
        var (_, service) = CreateKeyring();
        var info = await service.GenerateKeyAsync(NewKey()); // clave abierta con cadena vacía.
        const string text = "Texto firmado con ñ, acentos y ¡signos!";

        var signature = await service.SignAsync(new PgpSignRequest(text, info.Fingerprint[..8], null));

        Assert.StartsWith("-----BEGIN PGP SIGNATURE-----", signature, StringComparison.Ordinal);
        Assert.DoesNotContain(text, signature);

        var outcome = await service.VerifyAsync(new PgpVerifyRequest(text, signature));

        Assert.True(outcome.Valid);
        Assert.Null(outcome.Detail);
        Assert.NotNull(outcome.Signer);
        Assert.Equal(info.Fingerprint, outcome.Signer!.Fingerprint);
    }

    [Fact]
    public async Task SignVerify_RoundTrip_WithPassphrase()
    {
        var (_, service) = CreateKeyring();
        var info = await service.GenerateKeyAsync(NewKey(passphrase: "firme-fuerte"));

        var signature = await service.SignAsync(
            new PgpSignRequest("documento", info.Fingerprint, "firme-fuerte"));
        var outcome = await service.VerifyAsync(new PgpVerifyRequest("documento", signature));

        Assert.True(outcome.Valid);
        Assert.NotNull(outcome.Signer);
        Assert.Equal(info.KeyId, outcome.Signer!.KeyId);
    }

    [Fact]
    public async Task Verify_TamperedText_InvalidButSignerIdentified()
    {
        var (_, service) = CreateKeyring();
        var info = await service.GenerateKeyAsync(NewKey());
        var signature = await service.SignAsync(new PgpSignRequest("original", info.Fingerprint, null));

        var outcome = await service.VerifyAsync(new PgpVerifyRequest("alterado", signature));

        Assert.False(outcome.Valid);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Detail));
        Assert.NotNull(outcome.Signer); // el firmante se identifica aunque no cuadre.
        Assert.Equal(info.Fingerprint, outcome.Signer!.Fingerprint);
    }

    [Fact]
    public async Task Verify_UnknownSigner_InvalidWithoutSigner()
    {
        var (_, service) = CreateKeyring();
        var ana = await service.GenerateKeyAsync(NewKey());
        var signature = await service.SignAsync(new PgpSignRequest("hola", ana.Fingerprint, null));

        using var fresh = new TempExecutablePath();
        var freshService = new PgpService(new PgpKeyringStore(fresh.DirectoryPath));
        await freshService.GenerateKeyAsync(NewKey(name: "Beto", email: "beto@test.es"));

        var outcome = await freshService.VerifyAsync(new PgpVerifyRequest("hola", signature));

        Assert.False(outcome.Valid);
        Assert.Null(outcome.Signer);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Detail));
    }

    [Theory]
    [InlineData("")]
    [InlineData("esto no es una firma PGP, es texto cualquiera.")]
    [InlineData("-----BEGIN PGP SIGNATURE-----\nbasura basura\n-----END PGP SIGNATURE-----")]
    public async Task Verify_IllegibleSignature_IsOutcomeNotError(string signature)
    {
        var (_, service) = CreateKeyring();
        await service.GenerateKeyAsync(NewKey());

        var outcome = await service.VerifyAsync(new PgpVerifyRequest("cualquier texto", signature));

        Assert.False(outcome.Valid);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Detail));
        Assert.Null(outcome.Signer);
    }

    [Fact]
    public async Task Sign_WrongPassphrase_FailsWithoutEchoingPassphrase()
    {
        var (_, service) = CreateKeyring();
        var info = await service.GenerateKeyAsync(NewKey(passphrase: MarkerPassphrase));
        const string wrong = "otra-contraseña";

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            service.SignAsync(new PgpSignRequest("texto", info.Fingerprint, wrong)));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
        Assert.DoesNotContain(wrong, exception.ErrorMessage);
        Assert.DoesNotContain(MarkerPassphrase, exception.ErrorMessage);
        Assert.DoesNotContain("   at ", exception.ErrorMessage);
    }

    [Fact]
    public async Task Sign_WhenKeyIsPublicOnly_Fails()
    {
        var (_, service) = CreateKeyring();
        var info = await service.GenerateKeyAsync(NewKey());
        var publicArmored = await service.ExportKeyAsync(
            new PgpExportKeyRequest(info.Fingerprint, IncludePrivate: false));

        using var fresh = new TempExecutablePath();
        var freshService = new PgpService(new PgpKeyringStore(fresh.DirectoryPath));
        await freshService.ImportKeyAsync(new PgpImportKeyRequest(publicArmored));

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            freshService.SignAsync(new PgpSignRequest("texto", info.Fingerprint, null)));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(exception.ErrorMessage));
    }

    [Fact]
    public async Task Sign_UnknownFingerprint_Fails()
    {
        var (_, service) = CreateKeyring();
        await service.GenerateKeyAsync(NewKey()); // hay una clave distinta en el keyring.

        var exception = await Assert.ThrowsAsync<PgpException>(() =>
            service.SignAsync(new PgpSignRequest("texto", "FFFFFFFF", null)));

        Assert.Equal(BridgeErrorCodes.PgpFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task EncryptSign_Rsa3072_RoundTrips()
    {
        var (_, service) = CreateKeyring();
        var info = await service.GenerateKeyAsync(
            NewKey(algorithm: PgpAlgorithms.Rsa, passphrase: "rsa-passphrase"));

        var armored = await service.EncryptAsync(
            new PgpEncryptRequest("texto RSA", [info.Fingerprint[..10]]));
        Assert.Equal("texto RSA", await service.DecryptAsync(new PgpDecryptRequest(armored, "rsa-passphrase")));

        var signature = await service.SignAsync(
            new PgpSignRequest("texto RSA", info.Fingerprint, "rsa-passphrase"));
        var outcome = await service.VerifyAsync(new PgpVerifyRequest("texto RSA", signature));

        Assert.True(outcome.Valid);
        Assert.Equal(info.Fingerprint, outcome.Signer!.Fingerprint);
    }

    // ------------------------------------------------------------------
    // Utilidades
    // ------------------------------------------------------------------

    /// <summary>Keyring temporal + servicio (carpeta que jamás es %APPDATA%).</summary>
    private (PgpKeyringStore Store, PgpService Service) CreateKeyring()
    {
        Directory.CreateDirectory(_temp.DirectoryPath);
        var store = new PgpKeyringStore(_temp.DirectoryPath);
        return (store, new PgpService(store));
    }

    private static PgpGenerateKeyRequest NewKey(
        string name = Name,
        string email = Email,
        string? algorithm = null,
        int? bits = null,
        int? expirationDays = null,
        string? passphrase = null) =>
        new(name, email, algorithm!, bits, expirationDays, passphrase);

    private static PgpSecretKeyRing ReadSecretRing(string armored)
    {
        using var stream = new ArmoredInputStream(new MemoryStream(Encoding.UTF8.GetBytes(armored)));
        var factory = new PgpObjectFactory(stream);
        foreach (var packet in factory.AllPgpObjects())
        {
            if (packet is PgpSecretKeyRing ring)
            {
                return ring;
            }
        }

        throw new InvalidOperationException("El texto no contiene un anillo de claves privadas.");
    }
}
