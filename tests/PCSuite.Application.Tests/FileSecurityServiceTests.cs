using System.Security.Cryptography;
using PCSuite.Core.Files;
using PCSuite.Infrastructure.Files;

namespace PCSuite.Application.Tests;

/// <summary>
/// FileSecurityService (Task 033) sobre archivos reales en carpetas
/// temporales: el round-trip con 200 KB es la regresión del bug 2026-10-26 —
/// la primera versión cifraba con TransformFinalBlock por chunk de 64 KB
/// (rellenando CADA chunk) y los archivos grandes se corrompían; con
/// CryptoStream el relleno PKCS7 es uno solo y el límite de lectura no
/// importa. También: contraseña errónea sin salida parcial, hash contra
/// cómputo directo, CRC32 contra el vector de prueba IEEE ("123456789" →
/// 0xCBF43926) y borrado seguro.
/// </summary>
public sealed class FileSecurityServiceTests : IDisposable
{
    private readonly TempExecutablePath _temp = new();
    private readonly FileSecurityService _service = new();

    public void Dispose() => _temp.Dispose();

    private string CreateFile(string name, byte[] content)
    {
        var path = Path.Combine(_temp.DirectoryPath, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    // ------------------------------------------------------------------
    // Encriptar / Desencriptar (33.1)
    // ------------------------------------------------------------------

    [Fact]
    public async Task EncryptDecrypt_RoundTrip_LargeFile()
    {
        // 200 KB deliberadamente > 64 KB: multiplica los límites de chunk
        // del cifrado en streaming — la regresión directa del bug.
        var content = RandomNumberGenerator.GetBytes(200_000);
        var inputPath = CreateFile("documento.txt", content);

        var encrypted = await _service.EncryptAsync(new FileEncryptRequest(inputPath, "clave-de-prueba"));
        Assert.True(File.Exists(encrypted.OutputPath));
        Assert.EndsWith(".pcsuite-enc", encrypted.OutputPath);
        Assert.NotEmpty(encrypted.Sha256);

        // El ciphertext NO puede ser el contenido original.
        Assert.NotEqual(content, await File.ReadAllBytesAsync(encrypted.OutputPath));

        var decrypted = await _service.DecryptAsync(new FileDecryptRequest(encrypted.OutputPath, "clave-de-prueba"));
        Assert.Equal(content, await File.ReadAllBytesAsync(decrypted.OutputPath));
        Assert.Equal(inputPath, decrypted.OutputPath);

        // El original sigue intacto (no se pidió borrado seguro).
        Assert.True(File.Exists(inputPath));
    }

    [Fact]
    public async Task Decrypt_WrongPassword_ThrowsAndLeavesNoPartialOutput()
    {
        var inputPath = CreateFile("secreto.txt", "contenido"u8.ToArray());
        var encrypted = await _service.EncryptAsync(new FileEncryptRequest(inputPath, "correcta"));
        var expectedOutput = Path.Combine(_temp.DirectoryPath, "secreto.txt");

        var exception = await Assert.ThrowsAsync<FileSecurityException>(
            () => _service.DecryptAsync(new FileDecryptRequest(encrypted.OutputPath, "erronea")));

        Assert.Contains("contraseña incorrecta", exception.Message);

        // Sin salida parcial: un archivo a medias solo confundiría.
        Assert.False(File.Exists(expectedOutput));
    }

    [Fact]
    public async Task Decrypt_InvalidHeader_Throws()
    {
        var path = CreateFile("falso.pcsuite-enc", "esto no es un archivo pcsuite"u8.ToArray());

        var exception = await Assert.ThrowsAsync<FileSecurityException>(
            () => _service.DecryptAsync(new FileDecryptRequest(path, "clave")));

        Assert.Contains("no es un .pcsuite-enc válido", exception.Message);
    }

    [Fact]
    public async Task Encrypt_EmptyPassword_Throws()
    {
        var path = CreateFile("x.txt", [1, 2, 3]);

        await Assert.ThrowsAsync<FileSecurityException>(
            () => _service.EncryptAsync(new FileEncryptRequest(path, "")));
    }

    [Fact]
    public async Task Encrypt_RelativePath_Throws()
    {
        await Assert.ThrowsAsync<FileSecurityException>(
            () => _service.EncryptAsync(new FileEncryptRequest("relativo.txt", "clave")));
    }

    [Fact]
    public async Task Encrypt_MissingFile_Throws()
    {
        var missing = Path.Combine(_temp.DirectoryPath, "no-existe.txt");

        await Assert.ThrowsAsync<FileSecurityException>(
            () => _service.EncryptAsync(new FileEncryptRequest(missing, "clave")));
    }

    [Fact]
    public async Task Encrypt_WithDeleteOriginal_ShredsTheOriginal()
    {
        var content = RandomNumberGenerator.GetBytes(1_000);
        var inputPath = CreateFile("caduca.txt", content);

        var encrypted = await _service.EncryptAsync(
            new FileEncryptRequest(inputPath, "clave", DeleteOriginal: true));

        Assert.False(File.Exists(inputPath));
        Assert.True(File.Exists(encrypted.OutputPath));

        // Y el .pcsuite-enc descifra bien después del borrado del original.
        var decrypted = await _service.DecryptAsync(new FileDecryptRequest(encrypted.OutputPath, "clave"));
        Assert.Equal(content, await File.ReadAllBytesAsync(decrypted.OutputPath));
    }

    // ------------------------------------------------------------------
    // Hash multi-algoritmo (33.4)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Hash_MatchesDirectComputation_AndKnownCrc32()
    {
        // Vector de prueba clásico de CRC-32/IEEE: "123456789" → 0xCBF43926.
        var path = CreateFile("vector.bin", "123456789"u8.ToArray());
        var bytes = "123456789"u8.ToArray();

        var result = await _service.HashAsync(path);

        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            result.Sha256);
        Assert.Equal(
            Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant(),
            result.Sha1);
        Assert.Equal(
            Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(),
            result.Md5);
        Assert.Equal(0xCBF43926u, result.Crc32);
    }

    // ------------------------------------------------------------------
    // Borrado seguro (33.3)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Shred_OverwritesAndDeletesTheFile()
    {
        var path = CreateFile("destino.txt", "datos sensibles"u8.ToArray());

        var result = await _service.ShredAsync(new FileShredRequest(path));

        Assert.False(File.Exists(path));
        Assert.Equal(3, result.Passes);
    }

    [Fact]
    public async Task Shred_MissingFile_Throws()
    {
        var missing = Path.Combine(_temp.DirectoryPath, "no-existe.txt");

        await Assert.ThrowsAsync<FileSecurityException>(
            () => _service.ShredAsync(new FileShredRequest(missing)));
    }
}
