using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PCSuite.Core.Files;
using PCSuite.Infrastructure.Files;

namespace PCSuite.Application.Tests;

/// <summary>
/// Protección de CARPETAS (Task 033.5): contenedor único PCD1 con ZIP en
/// streaming — árbol completo preservado (subdirectorios, nombres
/// unicode, carpetas vacías, archivos vacíos, > 64 KB), auto-detección
/// archivo/carpeta en la misma op, restauración que NUNCA sobrescribe
/// ("Carpeta (1)"), contraseña errónea sin carpeta parcial, borrado
/// seguro de árboles rechazado con mensaje claro y guard anti-zip-slip
/// contra contenedores hostiles fabricados.
/// </summary>
public sealed class FileSecurityFolderTests : IDisposable
{
    private readonly TempExecutablePath _temp = new();
    private readonly FileSecurityService _service = new();

    public void Dispose() => _temp.Dispose();

    private string CreateFolder(string name)
    {
        var path = Path.Combine(_temp.DirectoryPath, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string folder, string relative, byte[] content)
    {
        var path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    // ------------------------------------------------------------------

    [Fact]
    public async Task EncryptDecrypt_Folder_RoundTrip_PreservesTreeAndBytes()
    {
        var source = CreateFolder("MiCarpeta");
        var big = RandomNumberGenerator.GetBytes(200_000); // > 64 KB: streaming
        var small = "corta"u8.ToArray();
        var unicode = "día ñoño"u8.ToArray();
        WriteFile(source, "a.txt", big);
        WriteFile(source, @"sub\deep\b.bin", small);
        WriteFile(source, "vacío", Array.Empty<byte>()); // archivo vacío
        Directory.CreateDirectory(Path.Combine(source, "carpeta-vacía"));
        WriteFile(source, "día ñoño.txt", unicode);

        var encrypted = await _service.EncryptAsync(new FileEncryptRequest(source, "clave"));

        // Contenedor ÚNICO con magia PCD1 y kind folder.
        Assert.EndsWith(".pcsuite-enc", encrypted.OutputPath);
        Assert.Equal("folder", encrypted.Kind);
        Assert.True(File.Exists(encrypted.OutputPath));
        var header = new byte[4];
        await using (var stream = File.OpenRead(encrypted.OutputPath))
        {
            await stream.ReadExactlyAsync(header);
        }
        Assert.Equal("PCD1", Encoding.ASCII.GetString(header));

        // El original NO se tocó (sin deleteOriginal).
        Assert.Equal(big, await File.ReadAllBytesAsync(Path.Combine(source, "a.txt")));

        var decrypted = await _service.DecryptAsync(new FileDecryptRequest(encrypted.OutputPath, "clave"));

        // La carpeta original sigue en su sitio: la restauración NUNCA la
        // pisa — aterriza en "MiCarpeta (1)".
        Assert.Equal("folder", decrypted.Kind);
        Assert.Equal(Path.Combine(_temp.DirectoryPath, "MiCarpeta (1)"), decrypted.OutputPath);

        var restored = decrypted.OutputPath;
        Assert.Equal(big, await File.ReadAllBytesAsync(Path.Combine(restored, "a.txt")));
        Assert.Equal(small, await File.ReadAllBytesAsync(Path.Combine(restored, "sub", "deep", "b.bin")));
        Assert.Empty(await File.ReadAllBytesAsync(Path.Combine(restored, "vacío")));
        Assert.True(Directory.Exists(Path.Combine(restored, "carpeta-vacía")));
        Assert.Equal(unicode, await File.ReadAllBytesAsync(Path.Combine(restored, "día ñoño.txt")));
    }

    [Fact]
    public async Task Decrypt_WhenSourceFolderIsGone_RestoresUnderOriginalName()
    {
        var source = CreateFolder("Respaldo");
        WriteFile(source, "x.txt", "contenido"u8.ToArray());

        var encrypted = await _service.EncryptAsync(new FileEncryptRequest(source, "clave"));
        Directory.Delete(source, recursive: true); // el usuario se llevó la carpeta

        var decrypted = await _service.DecryptAsync(new FileDecryptRequest(encrypted.OutputPath, "clave"));

        // Sin colisión: el nombre limpio, sin "(1)".
        Assert.Equal(Path.Combine(_temp.DirectoryPath, "Respaldo"), decrypted.OutputPath);
        Assert.Equal("contenido"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(decrypted.OutputPath, "x.txt")));
    }

    [Fact]
    public async Task EncryptDecrypt_EmptyFolder_ProducesAWorkingContainer()
    {
        var source = CreateFolder("Vacía");

        var encrypted = await _service.EncryptAsync(new FileEncryptRequest(source, "clave"));
        var decrypted = await _service.DecryptAsync(new FileDecryptRequest(encrypted.OutputPath, "clave"));

        Assert.Equal("folder", decrypted.Kind);
        Assert.True(Directory.Exists(decrypted.OutputPath));
        Assert.Empty(Directory.GetFileSystemEntries(decrypted.OutputPath));
    }

    [Fact]
    public async Task Encrypt_File_StillReportsFileKindWithFileMagic()
    {
        // Auto-detección: la misma op, archivo → PCS1 + kind "file".
        var path = Path.Combine(_temp.DirectoryPath, "documento.txt");
        await File.WriteAllTextAsync(path, "hola");

        var encrypted = await _service.EncryptAsync(new FileEncryptRequest(path, "clave"));

        Assert.Equal("file", encrypted.Kind);
        var header = new byte[4];
        await using (var stream = File.OpenRead(encrypted.OutputPath))
        {
            await stream.ReadExactlyAsync(header);
        }
        Assert.Equal("PCS1", Encoding.ASCII.GetString(header));
    }

    [Fact]
    public async Task Encrypt_Folder_RejectsDeleteOriginalWithClearMessage()
    {
        var source = CreateFolder("Temporal");
        WriteFile(source, "x.txt", "x"u8.ToArray());

        var exception = await Assert.ThrowsAsync<FileSecurityException>(
            () => _service.EncryptAsync(new FileEncryptRequest(source, "clave", DeleteOriginal: true)));

        Assert.Contains("borrado seguro de carpetas", exception.Message);
        Assert.False(File.Exists(source + ".pcsuite-enc"));
    }

    [Fact]
    public async Task Decrypt_FolderContainer_WrongPassword_LeavesNoPartialFolder()
    {
        var source = CreateFolder("Otra");
        WriteFile(source, "x.txt", "contenido"u8.ToArray());
        var encrypted = await _service.EncryptAsync(new FileEncryptRequest(source, "correcta"));

        var exception = await Assert.ThrowsAsync<FileSecurityException>(
            () => _service.DecryptAsync(new FileDecryptRequest(encrypted.OutputPath, "errónea")));

        Assert.Contains("contraseña incorrecta", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(_temp.DirectoryPath, "Otra (1)")));

        // En la carpeta solo conviven el original y el contenedor.
        Assert.Equal(2, Directory.GetFileSystemEntries(_temp.DirectoryPath).Length);
    }

    [Fact]
    public async Task Decrypt_FolderContainer_ZipSlip_IsBlocked()
    {
        // Contenedor hostil: ZIP con entrada "../escape.txt" cifrado con el
        // formato PCD1 legítimo (quien conozca la contraseña puede fabricar
        // uno). La restauración DEBE reventar antes de escribir fuera.
        using var zipBuffer = new MemoryStream();
        using (var zip = new ZipArchive(zipBuffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("../escape.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("fuera!");
        }

        var zipBytes = zipBuffer.ToArray();
        var salt = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            "clave"u8.ToArray(), salt, 100_000, HashAlgorithmName.SHA256, 32);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Longitud EXACTA: 36 de header + ciphertext real (con su PKCS7).
        // (Primera versión del test con buffer sobredimensionado: los
        // ceros de cola rompían el padding y el desencriptado respondía
        // "contraseña incorrecta" antes de llegar al guard.)
        var cipher = aes.CreateEncryptor().TransformFinalBlock(zipBytes, 0, zipBytes.Length);
        var container = new byte[4 + 16 + 16 + cipher.Length];
        Encoding.ASCII.GetBytes("PCD1").CopyTo(container, 0);
        salt.CopyTo(container, 4);
        iv.CopyTo(container, 4 + 16);
        cipher.CopyTo(container, 4 + 16 + 16);
        var containerPath = Path.Combine(_temp.DirectoryPath, "malicioso.pcsuite-enc");
        await File.WriteAllBytesAsync(containerPath, container);

        var exception = await Assert.ThrowsAsync<FileSecurityException>(
            () => _service.DecryptAsync(new FileDecryptRequest(containerPath, "clave")));

        Assert.Contains("escapan", exception.Message);
        Assert.False(File.Exists(Path.Combine(_temp.DirectoryPath, "escape.txt")));
    }
}
