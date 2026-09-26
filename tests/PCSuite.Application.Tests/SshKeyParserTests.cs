using System.Text;
using PCSuite.Core.SSH;

namespace PCSuite.Application.Tests;

/// <summary>
/// Parser de claves SSH en .NET puro (Task 009 / §5.1–5.2). Los fixtures son
/// claves públicas reales generadas con ssh-keygen durante el desarrollo; los
/// fingerprints esperados son los que devolvió `ssh-keygen -lf`, así que el
/// parser queda cross-validado contra la implementación de referencia. Los
/// contenedores privados son sintéticos (estructura PROTOCOL.key exacta); no
/// se usan binarios ni claves privadas reales.
/// </summary>
public sealed class SshKeyParserTests
{
    private const string Ed25519Line =
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIP03KRsDPmlF0jrjP2+p5Dg+SAVYQbfCi9S/niKjeZjt dev@pcsuite";

    private const string Ed25519Fingerprint = "SHA256:mAihIkCQImolEaLAOVt+nnjXFutxzX0pUq8bsS9mQBk";

    private const string RsaLine =
        "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQDEVhLeYZkYRoSfhUzihmKjN6MYynluunUW9gQcR4abuv08otaAixprcZxMkcgXg" +
        "AWjIJwcn3DwY+poNAxsF06HB0jQzz3y/Y/3txCCBkTv6tX3vBuiMMrRCeWIWjlwXxXz8sAgOdT++C9Zofj4tibg4l2ShvTXN4qa" +
        "8QlsAOKkdhoUqVRr1W32CYPWDMZljttKMFbnbDA8ap1YPBiAJ/OO9p5DEbVP1si8H4nIKfnF7FpVueTp7u2zyP43ZJ0qwqQR8QQ" +
        "C0PfI+uwm/DHB7Q8/OgoltFSpcUc8oi3hgeOoG64xCpaJZcfg+NOHCSlSkbCKijt+agprENYnwPrgMFtJ rsa fixture comment";

    private const string RsaFingerprint = "SHA256:B9M9FXK2qFeIUpEswk8+oTBQt+lJqv+E7spmBkQwlYk";

    private const string EcdsaLine =
        "ecdsa-sha2-nistp384 AAAAE2VjZHNhLXNoYTItbmlzdHAzODQAAAAIbmlzdHAzODQAAABhBPv7/U2D6G4AQFBoMVxYNr8RWDN" +
        "LacmYHn7QFClNASpFrNjS8MQdwkYmZwuTRzZ0egjHfs5lc/7T8osG9T3EG6yLVm+1KLVoBUMbVP48KEW9uGJpdEvHQwyNHqH5CH" +
        "ukfw== ecdsa fixture";

    private const string EcdsaFingerprint = "SHA256:XFuGUqpueXtR6luVYPeOlo110VAL0dO55VD9lfRswUw";

    // ------------------------------------------------------------------
    // Líneas de clave pública (.pub / authorized_keys)
    // ------------------------------------------------------------------

    [Fact]
    public void ParsePublicKeyLine_Ed25519_MatchesSshKeygenFingerprint()
    {
        var key = SshKeyParser.TryParsePublicKeyLine(Ed25519Line);

        Assert.NotNull(key);
        Assert.Equal("ssh-ed25519", key.Type);
        Assert.Equal("Ed25519", key.Algorithm);
        Assert.Equal(256, key.Bits);
        Assert.Equal(Ed25519Fingerprint, key.Fingerprint);
        Assert.Equal("dev@pcsuite", key.Comment);
    }

    [Fact]
    public void ParsePublicKeyLine_Rsa_ReturnsModulusBitsAndCommentWithSpaces()
    {
        var key = SshKeyParser.TryParsePublicKeyLine(RsaLine);

        Assert.NotNull(key);
        Assert.Equal("ssh-rsa", key.Type);
        Assert.Equal("RSA", key.Algorithm);
        Assert.Equal(2048, key.Bits);
        Assert.Equal(RsaFingerprint, key.Fingerprint);
        Assert.Equal("rsa fixture comment", key.Comment);
    }

    [Fact]
    public void ParsePublicKeyLine_EcdsaNistp384_ReturnsCurveBits()
    {
        var key = SshKeyParser.TryParsePublicKeyLine(EcdsaLine);

        Assert.NotNull(key);
        Assert.Equal("ecdsa-sha2-nistp384", key.Type);
        Assert.Equal("ECDSA", key.Algorithm);
        Assert.Equal(384, key.Bits);
        Assert.Equal(EcdsaFingerprint, key.Fingerprint);
        Assert.Equal("ecdsa fixture", key.Comment);
    }

    [Fact]
    public void ParsePublicKeyLine_WithoutComment_ReturnsEmptyComment()
    {
        var withoutComment = Ed25519Line[..Ed25519Line.LastIndexOf(" dev@pcsuite", StringComparison.Ordinal)];

        var key = SshKeyParser.TryParsePublicKeyLine(withoutComment);

        Assert.NotNull(key);
        Assert.Equal(string.Empty, key.Comment);
        Assert.Equal(Ed25519Fingerprint, key.Fingerprint);
    }

    [Fact]
    public void ParsePublicKeyLine_WithLeadingWhitespace_IsTrimmed()
    {
        var key = SshKeyParser.TryParsePublicKeyLine("   " + Ed25519Line + "   ");

        Assert.NotNull(key);
        Assert.Equal(Ed25519Fingerprint, key.Fingerprint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# ssh-ed25519 AAAA comentario")]
    [InlineData("ssh-ed25519")]
    [InlineData("ssh-ed25519 @@@noEsBase64@@@ comentario")]
    [InlineData("ssh-unknown AAAAOTIyMA== comentario")]
    [InlineData("command=\"/bin/true\" ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIP03KRsDPmlF0jrjP2+p5Dg+SAVYQbfCi9S/niKjeZjt c")]
    public void ParsePublicKeyLine_Malformed_ReturnsNull(string? line)
    {
        Assert.Null(SshKeyParser.TryParsePublicKeyLine(line));
    }

    [Fact]
    public void ParsePublicKeyLine_TypeMismatchBetweenLineAndBlob_ReturnsNull()
    {
        // Línea que dice ssh-rsa pero el blob decodificado es ed25519.
        var ed25519Blob = BlobFromLine(Ed25519Line);
        var forged = "ssh-rsa " + Convert.ToBase64String(ed25519Blob) + " impostor";

        Assert.Null(SshKeyParser.TryParsePublicKeyLine(forged));
    }

    [Fact]
    public void ParsePublicKeyLine_TruncatedBlob_ReturnsNull()
    {
        var blob = BlobFromLine(Ed25519Line);
        var truncated = Convert.ToBase64String(blob[..^5]);

        Assert.Null(SshKeyParser.TryParsePublicKeyLine("ssh-ed25519 " + truncated + " c"));
    }

    // ------------------------------------------------------------------
    // Blobs wire
    // ------------------------------------------------------------------

    [Fact]
    public void ParsePublicKeyBlob_TrailingBytes_ReturnsNull()
    {
        var blob = BlobFromLine(Ed25519Line).Concat(new byte[] { 0x00 }).ToArray();

        Assert.Null(SshKeyParser.TryParsePublicKeyBlob(blob, string.Empty));
    }

    [Fact]
    public void ParsePublicKeyBlob_Ed25519WithWrongKeyLength_ReturnsNull()
    {
        var blob = Concat(
            LengthPrefixed(Encoding.ASCII.GetBytes("ssh-ed25519")),
            LengthPrefixed(new byte[31]));

        Assert.Null(SshKeyParser.TryParsePublicKeyBlob(blob, string.Empty));
    }

    // ------------------------------------------------------------------
    // Contenedor de clave privada openssh-key-v1 (sintético)
    // ------------------------------------------------------------------

    [Fact]
    public void ParseFromPrivateKeyFile_PlainContainer_ExtractsEmbeddedPublicKey()
    {
        var container = BuildPrivateContainer(
            BlobFromLine(Ed25519Line), cipher: "none", kdf: "none", kdfData: []);

        var key = SshKeyParser.TryParseFromPrivateKeyFile(container);

        Assert.NotNull(key);
        Assert.Equal("ssh-ed25519", key.Type);
        Assert.Equal(Ed25519Fingerprint, key.Fingerprint);
        Assert.Equal(string.Empty, key.Comment);
    }

    [Fact]
    public void ParseFromPrivateKeyFile_EncryptedHeader_StillExtractsPublicKey()
    {
        // La sección pública viaja en claro aunque la privada esté con passphrase:
        // cipher aes256-ctr + kdf bcrypt con salt(16) + rondas(4) simulados.
        var container = BuildPrivateContainer(
            BlobFromLine(RsaLine),
            cipher: "aes256-ctr",
            kdf: "bcrypt",
            kdfData: Enumerable.Range(0, 20).Select(i => (byte)i).ToArray());

        var key = SshKeyParser.TryParseFromPrivateKeyFile(container);

        Assert.NotNull(key);
        Assert.Equal("ssh-rsa", key.Type);
        Assert.Equal(2048, key.Bits);
        Assert.Equal(RsaFingerprint, key.Fingerprint);
    }

    [Fact]
    public void ParseFromPrivateKeyFile_WrongKeyCount_ReturnsNull()
    {
        using var stream = new MemoryStream();
        stream.Write(SshKeyParser.OpenSshPrivateKeyMagic.ToArray());
        WriteString(stream, Encoding.ASCII.GetBytes("none"));
        WriteString(stream, Encoding.ASCII.GetBytes("none"));
        WriteString(stream, []);
        WriteU32(stream, 2);
        WriteString(stream, BlobFromLine(Ed25519Line));

        Assert.Null(SshKeyParser.TryParseFromPrivateKeyFile(stream.ToArray()));
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    public void ParseFromPrivateKeyFile_NotAContainer_ReturnsNull(byte[] bytes)
    {
        Assert.Null(SshKeyParser.TryParseFromPrivateKeyFile(bytes));
    }

    [Fact]
    public void ParseFromPrivateKeyFile_PublicKeyLineBytes_ReturnsNull()
    {
        Assert.Null(SshKeyParser.TryParseFromPrivateKeyFile(Encoding.UTF8.GetBytes(Ed25519Line)));
    }

    [Fact]
    public void ParsePublicKeyLine_CanonicalPublicKeyLine_RoundTrips()
    {
        var key = SshKeyParser.TryParsePublicKeyLine(RsaLine);
        Assert.NotNull(key);

        // La línea canónica reconstruida por el parser debe volver a parsearse
        // a los mismos campos (la usan inspección e importación del servicio).
        Assert.Equal(RsaLine, key!.PublicKey);
        var reparsed = SshKeyParser.TryParsePublicKeyLine(key.PublicKey);
        Assert.NotNull(reparsed);
        Assert.Equal(key.Fingerprint, reparsed!.Fingerprint);
        Assert.Equal(key.Comment, reparsed.Comment);
    }

    [Fact]
    public void ParsePublicKeyLine_WithoutComment_CanonicalLineOmitsTrailingSpace()
    {
        var line = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIP03KRsDPmlF0jrjP2+p5Dg+SAVYQbfCi9S/niKjeZjt";
        var key = SshKeyParser.TryParsePublicKeyLine(line);

        Assert.NotNull(key);
        Assert.Equal(line, key!.PublicKey);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static byte[] BlobFromLine(string line)
    {
        var parts = line.Split(' ');
        return Convert.FromBase64String(parts[1]);
    }

    private static byte[] BuildPrivateContainer(byte[] publicBlob, string cipher, string kdf, byte[] kdfData)
    {
        using var stream = new MemoryStream();
        stream.Write(SshKeyParser.OpenSshPrivateKeyMagic.ToArray());
        WriteString(stream, Encoding.ASCII.GetBytes(cipher));
        WriteString(stream, Encoding.ASCII.GetBytes(kdf));
        WriteString(stream, kdfData);
        WriteU32(stream, 1);
        WriteString(stream, publicBlob);
        return stream.ToArray();
    }

    private static void WriteU32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteString(Stream stream, byte[] value)
    {
        WriteU32(stream, (uint)value.Length);
        stream.Write(value, 0, value.Length);
    }

    private static byte[] LengthPrefixed(byte[] value)
    {
        using var stream = new MemoryStream();
        WriteString(stream, value);
        return stream.ToArray();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        using var stream = new MemoryStream();
        foreach (var part in parts)
        {
            stream.Write(part, 0, part.Length);
        }

        return stream.ToArray();
    }
}
