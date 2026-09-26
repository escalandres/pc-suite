using PCSuite.Core.PGP;
using PCSuite.Infrastructure.PGP;

namespace PCSuite.Application.Tests;

/// <summary>
/// Keyring PGP (Task 009 / §6): round-trip del índice camelCase, upsert por
/// fingerprint, tolerancia a E/S (índice ausente/corrupto/bloqueado), lectura
/// acotada, fingerprint validado antes de tocar el sistema de archivos (rutas
/// hostiles) y escritura atómica sin temporales. Todo en carpetas temporales:
/// los tests jamás tocan %APPDATA% real.
/// </summary>
public sealed class PgpKeyringStoreTests : IDisposable
{
    private const string Fingerprint = "8F3A9C1D5E7B2A4C6D8E0F1A3B5C7D9E1F2A4B6C";
    private const string OtherFingerprint = "0123456789ABCDEF0123456789ABCDEF01234567";

    private const string PublicArmored =
        "-----BEGIN PGP PUBLIC KEY BLOCK-----\nmQINabc123\n-----END PGP PUBLIC KEY BLOCK-----";

    private const string SecretArmored =
        "-----BEGIN PGP PRIVATE KEY BLOCK-----\nmQINxyz789\n-----END PGP PRIVATE KEY BLOCK-----";

    private readonly TempExecutablePath _temp = new();

    // ------------------------------------------------------------------
    // Índice: round-trip y upsert
    // ------------------------------------------------------------------

    [Fact]
    public void Load_WhenIndexMissing_ReturnsEmpty()
    {
        var store = CreateStore();

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Upsert_ThenLoad_ReturnsEntry_CamelCaseIndex()
    {
        var store = CreateStore();
        var key = Key();
        key = key with { ExpiresAt = key.CreatedAt.AddDays(90) };

        store.Upsert(key);

        Assert.Equal(key, Assert.Single(store.Load()));
        var raw = File.ReadAllText(Path.Combine(_temp.DirectoryPath, "keyring.json"));
        Assert.Contains("\"fingerprint\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"canEncrypt\"", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Fingerprint\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Upsert_SameFingerprint_UpdatesWithoutDuplicate_CaseInsensitive()
    {
        var store = CreateStore();
        store.Upsert(Key());
        store.Upsert(Key(Fingerprint.ToLowerInvariant()) with { HasPrivate = true });
        store.Upsert(Key(OtherFingerprint));

        var keys = store.Load();

        Assert.Equal(2, keys.Count);
        var updated = Assert.Single(keys, k => string.Equals(k.Fingerprint, Fingerprint, StringComparison.OrdinalIgnoreCase));
        Assert.True(updated.HasPrivate);
        Assert.Equal(Fingerprint, updated.Fingerprint); // normalizado a mayúsculas
    }

    // ------------------------------------------------------------------
    // Tolerancia a E/S
    // ------------------------------------------------------------------

    [Fact]
    public void Load_WhenIndexCorrupt_ReturnsEmpty()
    {
        var store = CreateStore();
        File.WriteAllText(Path.Combine(_temp.DirectoryPath, "keyring.json"), "no es json {");

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Load_WhenIndexLocked_ReturnsEmpty_TolerantToIo()
    {
        var store = CreateStore();
        store.Upsert(Key());
        var path = Path.Combine(_temp.DirectoryPath, "keyring.json");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Empty(store.Load());
        }
    }

    [Fact]
    public void Read_WhenKeyFileMissing_ReturnsNull()
    {
        var store = CreateStore();

        Assert.Null(store.ReadPublicKey(Fingerprint));
        Assert.Null(store.ReadSecretKey(Fingerprint));
    }

    [Fact]
    public void Read_WhenKeyFileTooLarge_ReturnsNull()
    {
        var store = CreateStore();
        File.WriteAllText(
            Path.Combine(_temp.DirectoryPath, Fingerprint + ".pub.asc"),
            new string('A', 1_048_577));

        Assert.Null(store.ReadPublicKey(Fingerprint));
    }

    // ------------------------------------------------------------------
    // Material armoured: round-trip, case y escritura atómica
    // ------------------------------------------------------------------

    [Fact]
    public void WriteThenRead_RoundTrip_PublicAndSecret()
    {
        var store = CreateStore();

        store.WritePublicKey(Fingerprint, PublicArmored);
        store.WriteSecretKey(Fingerprint, SecretArmored);

        Assert.Equal(PublicArmored, store.ReadPublicKey(Fingerprint));
        Assert.Equal(SecretArmored, store.ReadSecretKey(Fingerprint));
    }

    [Fact]
    public void Read_WithLowercaseFingerprint_FindsUppercaseFile()
    {
        var store = CreateStore();
        store.WritePublicKey(Fingerprint, PublicArmored);

        Assert.Equal(PublicArmored, store.ReadPublicKey(Fingerprint.ToLowerInvariant()));
    }

    [Fact]
    public void Write_OverwritesExistingKey_AndLeavesNoTemporaryFiles()
    {
        var store = CreateStore();
        store.Upsert(Key());
        store.WritePublicKey(Fingerprint, PublicArmored);
        store.WritePublicKey(Fingerprint, "otro material");

        Assert.Equal("otro material", store.ReadPublicKey(Fingerprint));
        Assert.Empty(Directory.GetFiles(_temp.DirectoryPath, "*.tmp"));
        Assert.Single(store.Load());
    }

    // ------------------------------------------------------------------
    // Fingerprint validado ANTES de tocar el sistema de archivos
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("8F3A9C1D5E7B2A4C6D8E0F1A3B5C7D9E1F2A4B6")] // 39 dígitos
    [InlineData("8F3A9C1D5E7B2A4C6D8E0F1A3B5C7D9E1F2A4B6G")] // no hexadecimal
    [InlineData("..\\..\\keyring")]
    [InlineData("C:\\Windows\\System32")]
    public void Read_WhenFingerprintHostile_ThrowsWithoutTouchingDisk(string fingerprint)
    {
        var store = CreateStore();

        Assert.Throws<ArgumentException>(() => store.ReadPublicKey(fingerprint));
        Assert.Throws<ArgumentException>(() => store.WritePublicKey(fingerprint, PublicArmored));
        Assert.Empty(Directory.GetFiles(_temp.DirectoryPath, "*.asc"));
    }

    [Fact]
    public void Upsert_WhenFingerprintHostile_Throws()
    {
        var store = CreateStore();

        Assert.Throws<ArgumentException>(() => store.Upsert(Key("..\\..\\keyring")));
        Assert.False(File.Exists(Path.Combine(_temp.DirectoryPath, "keyring.json")));
    }

    private PgpKeyringStore CreateStore() => new(_temp.DirectoryPath);

    private static PgpKeyInfo Key(string fingerprint = Fingerprint) =>
        new(
            Fingerprint: fingerprint,
            KeyId: fingerprint.Length >= 16 ? fingerprint[^16..] : string.Empty,
            UserId: "Ana <ana@example.com>",
            Name: "Ana",
            Email: "ana@example.com",
            Algorithm: "ed25519",
            Bits: null,
            CreatedAt: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(1)),
            ExpiresAt: null,
            HasPrivate: false,
            CanEncrypt: true);

    public void Dispose() => _temp.Dispose();
}
