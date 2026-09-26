using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace PCSuite.Core.SSH;

/// <summary>
/// Parser de claves OpenSSH en .NET puro (sin binarios ni dependencias):
/// líneas `.pub`/authorized_keys, blobs wire y el contenedor privado
/// <c>openssh-key-v1</c> — cuya sección pública va en claro incluso cuando la
/// privada está cifrada con passphrase. El fingerprint SHA256 se calcula igual
/// que <c>ssh-keygen -lf</c>: SHA256 sobre el blob completo (type incluido),
/// Base64 sin relleno. Los tipos fuera de §5.1 (p. ej. ssh-dss) se ignoran.
/// </summary>
public static class SshKeyParser
{
    /// <summary>Formato mostrado en la inspección (§5.2).</summary>
    public const string FormatOpenSsh = "OpenSSH";

    /// <summary>Magia del contenedor de clave privada OpenSSH (15 bytes, PROTOCOL.key).</summary>
    public static ReadOnlySpan<byte> OpenSshPrivateKeyMagic => "openssh-key-v1\0"u8;

    /// <summary>
    /// Parsea una línea de clave pública (<c>type base64 [comentario…]</c>).
    /// Devuelve null ante vacíos, comentarios, opciones ("command=…"), tipos
    /// desconocidos, base64 inválido o desajuste entre el tipo de la línea y el
    /// del blob.
    /// </summary>
    public static SshParsedKey? TryParsePublicKeyLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var trimmed = line.Trim();
        if (trimmed[0] == '#')
        {
            return null;
        }

        var typeEnd = 0;
        while (typeEnd < trimmed.Length && !char.IsWhiteSpace(trimmed[typeEnd]))
        {
            typeEnd++;
        }

        if (typeEnd == 0 || typeEnd == trimmed.Length)
        {
            return null;
        }

        var type = trimmed[..typeEnd];

        var b64Start = typeEnd;
        while (b64Start < trimmed.Length && char.IsWhiteSpace(trimmed[b64Start]))
        {
            b64Start++;
        }

        var b64End = b64Start;
        while (b64End < trimmed.Length && !char.IsWhiteSpace(trimmed[b64End]))
        {
            b64End++;
        }

        if (b64End == b64Start)
        {
            return null;
        }

        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(trimmed[b64Start..b64End]);
        }
        catch (FormatException)
        {
            return null;
        }

        var comment = trimmed[b64End..].TrimStart();
        var parsed = TryParsePublicKeyBlob(blob, comment);
        return parsed is not null && parsed.Type == type ? parsed : null;
    }

    /// <summary>Parsea el blob wire de una clave pública (type + campos), con consumo exacto.</summary>
    public static SshParsedKey? TryParsePublicKeyBlob(ReadOnlySpan<byte> blob, string comment)
    {
        var reader = new Reader(blob);
        if (!reader.TryReadStringAscii(out var type))
        {
            return null;
        }

        // Reader es un ref struct: se pasa por ref para que el consumo de bytes
        // regrese al caller (si no, HasRemaining siempre sería true y todo
        // parseo válido acabaría en null).
        SshParsedKey? parsed = type switch
        {
            "ssh-ed25519" => ParseEd25519(ref reader, comment),
            "ssh-rsa" => ParseRsa(ref reader, comment),
            "ecdsa-sha2-nistp256" => ParseEcdsa(ref reader, comment, "nistp256", 256),
            "ecdsa-sha2-nistp384" => ParseEcdsa(ref reader, comment, "nistp384", 384),
            "ecdsa-sha2-nistp521" => ParseEcdsa(ref reader, comment, "nistp521", 521),
            _ => null,
        };

        return parsed is not null && !reader.HasRemaining ? parsed : null;
    }

    /// <summary>
    /// Extrae la clave pública (en claro) de un archivo de clave privada
    /// <c>openssh-key-v1</c>; el comentario no es recuperable en claves
    /// cifradas. Devuelve null si no es un contenedor reconocible.
    /// </summary>
    public static SshParsedKey? TryParseFromPrivateKeyFile(ReadOnlySpan<byte> fileBytes)
    {
        var magic = OpenSshPrivateKeyMagic;
        if (fileBytes.Length < magic.Length || !fileBytes[..magic.Length].SequenceEqual(magic))
        {
            return null;
        }

        var reader = new Reader(fileBytes[magic.Length..]);
        if (!reader.TryReadString(out _) ||       // ciphername (none / aes256-ctr)
            !reader.TryReadString(out _) ||       // kdfname (none / bcrypt)
            !reader.TryReadString(out _) ||       // datos del kdf
            !reader.TryReadU32(out var keyCount) ||
            keyCount != 1 ||
            !reader.TryReadString(out var publicBlob))
        {
            return null;
        }

        return TryParsePublicKeyBlob(publicBlob, comment: string.Empty);
    }

    /// <summary>Fingerprint "SHA256:…" idéntico a <c>ssh-keygen -lf</c> (sin relleno '=').</summary>
    public static string ComputeFingerprint(ReadOnlySpan<byte> blob)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(blob, hash);
        return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
    }

    private static SshParsedKey? ParseEd25519(ref Reader reader, string comment)
    {
        return reader.TryReadString(out var key) && key.Length == 32
            ? Build("ssh-ed25519", "Ed25519", 256, reader, comment)
            : null;
    }

    private static SshParsedKey? ParseRsa(ref Reader reader, string comment)
    {
        if (!reader.TryReadString(out var exponent) || exponent.IsEmpty ||
            !reader.TryReadString(out var modulus) || modulus.IsEmpty)
        {
            return null;
        }

        var n = new BigInteger(modulus, isUnsigned: true, isBigEndian: true);
        var bits = (int)n.GetBitLength();
        return bits > 0 ? Build("ssh-rsa", "RSA", bits, reader, comment) : null;
    }

    private static SshParsedKey? ParseEcdsa(ref Reader reader, string comment, string expectedCurve, int bits)
    {
        if (!reader.TryReadStringAscii(out var curve) || curve != expectedCurve ||
            !reader.TryReadString(out var point) || point.IsEmpty)
        {
            return null;
        }

        return Build("ecdsa-sha2-" + expectedCurve, "ECDSA", bits, reader, comment);
    }

    private static SshParsedKey Build(string type, string algorithm, int bits, in Reader reader, string comment) =>
        new(
            type,
            algorithm,
            bits,
            ComputeFingerprint(reader.Blob),
            comment,
            BuildPublicKeyLine(type, reader.Blob, comment));

    /// <summary>Línea pública canónica: <c>type base64 [comentario]</c>.</summary>
    private static string BuildPublicKeyLine(string type, ReadOnlySpan<byte> blob, string comment)
    {
        var line = type + " " + Convert.ToBase64String(blob);
        return comment.Length > 0 ? line + " " + comment : line;
    }

    /// <summary>Lector de enteros big-endian y cadenas con prefijo de longitud (wire SSH).</summary>
    private ref struct Reader
    {
        private ReadOnlySpan<byte> _span;
        private int _position;

        public Reader(ReadOnlySpan<byte> span)
        {
            _span = span;
            _position = 0;
        }

        /// <summary>Blob original completo (para el fingerprint): el tipo va incluido.</summary>
        public ReadOnlySpan<byte> Blob => _span;

        public bool HasRemaining => _position < _span.Length;

        public bool TryReadU32(out uint value)
        {
            value = 0;
            if (_span.Length - _position < 4)
            {
                return false;
            }

            value = ((uint)_span[_position] << 24) |
                    ((uint)_span[_position + 1] << 16) |
                    ((uint)_span[_position + 2] << 8) |
                    _span[_position + 3];
            _position += 4;
            return true;
        }

        public bool TryReadString(out ReadOnlySpan<byte> value)
        {
            value = default;
            if (!TryReadU32(out var length) || (long)length > _span.Length - _position)
            {
                return false;
            }

            value = _span.Slice(_position, (int)length);
            _position += (int)length;
            return true;
        }

        public bool TryReadStringAscii(out string value)
        {
            value = string.Empty;
            if (!TryReadString(out var bytes) || bytes.IsEmpty)
            {
                return false;
            }

            foreach (var b in bytes)
            {
                if (b <= 0x20 || b > 0x7e)
                {
                    return false;
                }
            }

            value = Encoding.ASCII.GetString(bytes);
            return true;
        }
    }
}
