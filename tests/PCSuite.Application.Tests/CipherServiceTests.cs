using System.Security.Cryptography;
using System.Text;
using PCSuite.Infrastructure.Files;

namespace PCSuite.Application.Tests;

/// <summary>
/// CipherService (Task 034) en proceso: round-trips de los 7 codificadores,
/// hash multi-algoritmo, AES-256-GCM y XOR. El round-trip AES es la regresión
/// del bug 2026-10-26: la primera versión descartaba el tag GCM y el
/// descifrado fallaba SIEMPRE — estos tests fallaban sin el fix.
/// </summary>
public sealed class CipherServiceTests
{
    private readonly CipherService _service = new();

    // ------------------------------------------------------------------
    // Codificadores (34.1)
    // ------------------------------------------------------------------

    [Fact]
    public void Encode_Base64_MatchesStandardAndRoundTrips()
    {
        const string input = "PCSuite 0.9 ñ";
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes(input));

        var encoded = _service.Encode("base64", input);
        var decoded = _service.Decode("base64", encoded);

        Assert.Equal(expected, encoded);
        Assert.Equal(input, decoded);
    }

    [Fact]
    public void Encode_Hex_MatchesStandardAndRoundTrips()
    {
        const string input = "cifra ñ!";
        var expected = Convert.ToHexString(Encoding.UTF8.GetBytes(input)).ToLowerInvariant();

        var encoded = _service.Encode("hex", input);
        var decoded = _service.Decode("hex", encoded);

        Assert.Equal(expected, encoded);
        Assert.Equal(input, decoded);
    }

    [Fact]
    public void Encode_Url_RoundTrips()
    {
        const string input = "a b&c=d/e?f";

        var encoded = _service.Encode("url", input);
        var decoded = _service.Decode("url", encoded);

        Assert.Equal(Uri.EscapeDataString(input), encoded);
        Assert.Equal(input, decoded);
    }

    [Fact]
    public void Encode_Rot13_IsSelfInverse()
    {
        var encoded = _service.Encode("rot13", "Hola");
        var decoded = _service.Decode("rot13", encoded);

        Assert.Equal("Ubyn", encoded);
        Assert.Equal("Hola", decoded);
    }

    [Fact]
    public void Encode_Caesar_AppliesConfigurableShiftBothWays()
    {
        var encoded = _service.Encode("caesar", "abc xyz", 5);
        var decoded = _service.Decode("caesar", encoded, 5);

        Assert.Equal("fgh cde", encoded);
        Assert.Equal("abc xyz", decoded);
    }

    [Fact]
    public void Encode_Binary_RoundTrips()
    {
        const string input = "Hi";

        var encoded = _service.Encode("binary", input);
        var decoded = _service.Decode("binary", encoded);

        Assert.Equal("01001000 01101001", encoded);
        Assert.Equal(input, decoded);
    }

    [Fact]
    public void Encode_Morse_RoundTrips()
    {
        const string input = "HOLA MUNDO 123";

        var encoded = _service.Encode("morse", input);
        var decoded = _service.Decode("morse", encoded);

        Assert.Equal(".... --- .-.. .- / -- ..- -. -.. --- / .---- ..--- ...--", encoded);
        Assert.Equal(input, decoded);
    }

    [Fact]
    public void Encode_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _service.Encode("base64", string.Empty));
        Assert.Equal(string.Empty, _service.Decode("base64", string.Empty));
    }

    [Fact]
    public void Encode_UnknownAlgorithm_ThrowsControlled()
    {
        Assert.Throws<NotSupportedException>(() => _service.Encode("no-existe", "x"));
        Assert.Throws<NotSupportedException>(() => _service.Decode("no-existe", "x"));
    }

    // ------------------------------------------------------------------
    // Hash de texto (34.2)
    // ------------------------------------------------------------------

    [Fact]
    public void HashText_MatchesDirectComputation()
    {
        const string input = "texto de prueba";
        var bytes = Encoding.UTF8.GetBytes(input);

        var result = _service.HashText(input);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.Sha256);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant(), result.Sha1);
        Assert.Equal(Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), result.Md5);
        Assert.Equal(Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant(), result.Sha512);
    }

    [Fact]
    public void HashText_EmptyInput_ReturnsEmptyHashes()
    {
        var result = _service.HashText(string.Empty);

        Assert.Equal(string.Empty, result.Sha256);
        Assert.Equal(string.Empty, result.Sha1);
        Assert.Equal(string.Empty, result.Md5);
        Assert.Equal(string.Empty, result.Sha512);
    }

    // ------------------------------------------------------------------
    // AES-256-GCM (34.3) — la regresión del tag descartado vive aquí
    // ------------------------------------------------------------------

    [Fact]
    public void EncryptAes_DecryptAes_RoundTrips()
    {
        const string message = "mensaje secreto con acentos ñ!";

        var ciphertext = _service.EncryptAes(message, "contraseña-segura");
        var recovered = _service.DecryptAes(ciphertext, "contraseña-segura");

        Assert.NotEqual(message, ciphertext);
        Assert.Equal(message, recovered);
    }

    [Fact]
    public void DecryptAes_WrongPassword_ThrowsControlled()
    {
        var ciphertext = _service.EncryptAes("secreto", "clave-correcta");

        var exception = Assert.Throws<NotSupportedException>(() => _service.DecryptAes(ciphertext, "clave-erronea"));

        Assert.Contains("contraseña incorrecta", exception.Message);
    }

    [Fact]
    public void EncryptAes_EmptyPassword_Throws()
    {
        Assert.Throws<NotSupportedException>(() => _service.EncryptAes("x", ""));
        Assert.Throws<NotSupportedException>(() => _service.DecryptAes("AAAA", ""));
    }

    [Fact]
    public void DecryptAes_ShortCiphertext_Throws()
    {
        // "AAAA" = 3 bytes reales: ni salt(16) + nonce(12) + tag(16) alcanzan.
        Assert.Throws<NotSupportedException>(() => _service.DecryptAes("AAAA", "clave"));
    }

    // ------------------------------------------------------------------
    // XOR (34.3)
    // ------------------------------------------------------------------

    [Fact]
    public void Xor_XorDecrypt_RoundTrips()
    {
        const string input = "texto para xor con ñ";

        var base64 = _service.Xor(input, "clave-xor");
        var recovered = _service.XorDecrypt(base64, "clave-xor");

        Assert.NotEqual(input, base64);
        Assert.Equal(input, recovered);
    }

    [Fact]
    public void Xor_EmptyKey_Throws()
    {
        Assert.Throws<NotSupportedException>(() => _service.Xor("x", ""));
        Assert.Throws<NotSupportedException>(() => _service.XorDecrypt("AAAA", ""));
    }
}
