using System.Security.Cryptography;
using System.Text;

namespace PCSuite.Infrastructure.Files;

/// <summary>
/// Servicio de cifrado/codificación de texto (Task 034): Base64, Hex, URL,
/// ROT13, César, Binario, Morse, hash multi-algoritmo, AES-256-GCM y XOR.
/// Todo en proceso, sin red, sin elevación.
/// </summary>
public sealed class CipherService
{
    // ------------------------------------------------------------------
    // Codificadores (34.1)
    // ------------------------------------------------------------------

    public string Encode(string algorithm, string input, int? shift = null, string? key = null)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return algorithm.ToLowerInvariant() switch
        {
            "base64" => Convert.ToBase64String(Encoding.UTF8.GetBytes(input)),
            "hex" => Convert.ToHexString(Encoding.UTF8.GetBytes(input)).ToLowerInvariant(),
            "url" => Uri.EscapeDataString(input),
            "rot13" => ApplyShift(input, 13),
            "caesar" => ApplyShift(input, shift ?? 3),
            "binary" => string.Join(' ', Encoding.UTF8.GetBytes(input).Select(b => Convert.ToString(b, 2).PadLeft(8, '0'))),
            "morse" => ToMorse(input),
            _ => throw new NotSupportedException($"Algoritmo no soportado: {algorithm}"),
        };
    }

    public string Decode(string algorithm, string input, int? shift = null, string? key = null)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return algorithm.ToLowerInvariant() switch
        {
            "base64" => Encoding.UTF8.GetString(Convert.FromBase64String(input.Trim())),
            "hex" => Encoding.UTF8.GetString(Convert.FromHexString(input.Trim().Replace(" ", ""))),
            "url" => Uri.UnescapeDataString(input),
            "rot13" => ApplyShift(input, 13), // auto-inverso
            "caesar" => ApplyShift(input, -(shift ?? 3)),
            "binary" => FromBinary(input.Trim()),
            "morse" => FromMorse(input.Trim()),
            _ => throw new NotSupportedException($"Algoritmo no soportado: {algorithm}"),
        };
    }

    // ------------------------------------------------------------------
    // Hash de texto (34.2)
    // ------------------------------------------------------------------

    public TextHashResult HashText(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return new TextHashResult("", "", "", "");
        }

        var bytes = Encoding.UTF8.GetBytes(input);
        return new TextHashResult(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant(),
            Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(),
            Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant());
    }

    // ------------------------------------------------------------------
    // AES-256-GCM (34.3)
    // ------------------------------------------------------------------

    public string EncryptAes(string input, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new NotSupportedException("La contraseña no puede estar vacía.");
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);

        var plaintext = Encoding.UTF8.GetBytes(input);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aesGcm = new AesGcm(key, tagSizeInBytes: 16);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        // Formato: base64(salt[16] + nonce[12] + tag[16] + ciphertext).
        // El tag VIAJA con el ciphertext — sin él no hay descifrado posible.
        // (Fix 2026-10-26: la primera versión copiaba 16 bytes de ceros en
        // su lugar y el tag se perdía → DecryptAes autenticaba contra ceros
        // y fallaba SIEMPRE.)
        var combined = new byte[salt.Length + nonce.Length + tag.Length + ciphertext.Length];
        salt.CopyTo(combined, 0);
        nonce.CopyTo(combined, salt.Length);
        tag.CopyTo(combined, salt.Length + nonce.Length);
        ciphertext.CopyTo(combined, salt.Length + nonce.Length + tag.Length);

        return Convert.ToBase64String(combined);
    }

    public string DecryptAes(string base64Input, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new NotSupportedException("La contraseña no puede estar vacía.");
        }

        var combined = Convert.FromBase64String(base64Input.Trim());
        if (combined.Length < 16 + 12 + 16)
        {
            throw new NotSupportedException("El ciphertext es demasiado corto para ser AES-256-GCM válido.");
        }

        var salt = combined[..16];
        var nonce = combined[16..28];
        var tag = combined[28..44];
        var ciphertext = combined[44..];

        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);

        var plaintext = new byte[ciphertext.Length];
        using var aesGcm = new AesGcm(key, tagSizeInBytes: 16);
        try
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException)
        {
            throw new NotSupportedException("No se pudo descifrar: contraseña incorrecta o texto cifrado alterado.");
        }
        return Encoding.UTF8.GetString(plaintext);
    }

    public string Xor(string input, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new NotSupportedException("La clave no puede estar vacía.");
        }

        var inputBytes = Encoding.UTF8.GetBytes(input);
        var keyBytes = Encoding.UTF8.GetBytes(key);

        var result = new byte[inputBytes.Length];
        for (var i = 0; i < inputBytes.Length; i++)
        {
            result[i] = (byte)(inputBytes[i] ^ keyBytes[i % keyBytes.Length]);
        }

        return Convert.ToBase64String(result);
    }

    public string XorDecrypt(string base64Input, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new NotSupportedException("La clave no puede estar vacía.");
        }

        var inputBytes = Convert.FromBase64String(base64Input.Trim());
        var keyBytes = Encoding.UTF8.GetBytes(key);

        var result = new byte[inputBytes.Length];
        for (var i = 0; i < inputBytes.Length; i++)
        {
            result[i] = (byte)(inputBytes[i] ^ keyBytes[i % keyBytes.Length]);
        }

        return Encoding.UTF8.GetString(result);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string ApplyShift(string text, int shift)
    {
        shift = ((shift % 26) + 26) % 26;
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= 'a' and <= 'z')
            {
                sb.Append((char)('a' + (c - 'a' + shift) % 26));
            }
            else if (c is >= 'A' and <= 'Z')
            {
                sb.Append((char)('A' + (c - 'A' + shift) % 26));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string FromBinary(string binary)
    {
        var bytes = binary
            .Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => Convert.ToByte(token, 2))
            .ToArray();
        return Encoding.UTF8.GetString(bytes);
    }

    private static readonly Dictionary<char, string> MorseTable = new()
    {
        ['A'] = ".-", ['B'] = "-...", ['C'] = "-.-.", ['D'] = "-..", ['E'] = ".",
        ['F'] = "..-.", ['G'] = "--.", ['H'] = "....", ['I'] = "..", ['J'] = ".---",
        ['K'] = "-.-", ['L'] = ".-..", ['M'] = "--", ['N'] = "-.", ['O'] = "---",
        ['P'] = ".--.", ['Q'] = "--.-", ['R'] = ".-.", ['S'] = "...", ['T'] = "-",
        ['U'] = "..-", ['V'] = "...-", ['W'] = ".--", ['X'] = "-..-", ['Y'] = "-.--",
        ['Z'] = "--..", ['0'] = "-----", ['1'] = ".----", ['2'] = "..---",
        ['3'] = "...--", ['4'] = "....-", ['5'] = ".....", ['6'] = "-....",
        ['7'] = "--...", ['8'] = "---..", ['9'] = "----.",
        ['.'] = ".-.-.-", [','] = "--..--", ['?'] = "..--..", ['!'] = "-.-.--",
        [' '] = "/",
    };

    private static readonly Dictionary<string, char> ReverseMorse = MorseTable
        .ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    private static string ToMorse(string input)
    {
        var sb = new StringBuilder();
        foreach (var c in input.ToUpperInvariant())
        {
            if (MorseTable.TryGetValue(c, out var code))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(code);
            }
        }
        return sb.ToString();
    }

    private static string FromMorse(string morse)
    {
        var sb = new StringBuilder();
        foreach (var token in morse.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ReverseMorse.TryGetValue(token, out var c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

public sealed record TextHashResult(string Sha256, string Sha1, string Md5, string Sha512);
