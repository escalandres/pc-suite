using System.Security.Cryptography;
using System.Text;
using PCSuite.Core.Files;

namespace PCSuite.Infrastructure.Files;

/// <summary>
/// Servicio de seguridad de archivos (Task 033): cifrado AES-256-CBC con
/// PBKDF2, esteganografía LSB en PNG, borrado seguro multi-pasada y
/// hash multi-algoritmo. Todo local, sin red, sin elevación.
///
/// Formato de archivo encriptado (.pcsuite-enc):
///   [16 bytes magia: PCS1][16 bytes salt][16 bytes IV][ciphertext AES-CBC]
///   PBKDF2(SHA-256, 100_000 iter) deriva la clave de la contraseña + salt.
/// </summary>
public sealed class FileSecurityService : IFileSecurityService
{
    private const string MagicHeader = "PCS1";

    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32; // AES-256
    private const int Pbkdf2Iterations = 100_000;

    public async Task<FileEncryptResult> EncryptAsync(
        FileEncryptRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatePath(request.InputPath);
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new FileSecurityException("La contraseña no puede estar vacía.");
        }

        var outputPath = request.OutputPath ?? request.InputPath + ".pcsuite-enc";
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(request.Password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);

        // AES-256-CBC en streaming vía CryptoStream: aplica UN solo relleno
        // PKCS7 al final del archivo. (Fix 2026-10-26: la primera versión
        // llamaba TransformFinalBlock por chunk de 64 KB, lo que rellenaba
        // CADA chunk como si fuera el último — al descifrar, los límites de
        // lectura no coincidían con los de escritura y los archivos de más
        // de 64 KB se corrompían o reventaban con "padding inválido".)
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        try
        {
            // El input vive DENTRO del try: su handle de lectura debe
            // cerrarse antes del borrado seguro del original — ShredCoreAsync
            // reabre el archivo para escribir y un lector abierto lo bloquea
            // (tercer bug de la tanda 2026-10-26: DeleteOriginal SIEMPRE
            // fallaba con IOException).
            await using var input = File.OpenRead(request.InputPath);
            await using var output = File.Create(outputPath);

            // Header: magia + salt + IV
            await output.WriteAsync(Encoding.ASCII.GetBytes(MagicHeader), cancellationToken);
            await output.WriteAsync(salt, cancellationToken);
            await output.WriteAsync(iv, cancellationToken);

            using (var encryptor = aes.CreateEncryptor())
            await using (var crypto = new CryptoStream(output, encryptor, CryptoStreamMode.Write))
            {
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await crypto.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await crypto.FlushFinalBlockAsync(cancellationToken);
            }
        }
        catch (ArgumentException)
        {
            TryDelete(outputPath);
            throw new FileSecurityException("La ruta del archivo no es válida.");
        }
        catch (IOException)
        {
            // Sin .pcsuite-enc a medias: un archivo parcial no descifra y
            // solo confundiría al usuario.
            TryDelete(outputPath);
            throw new FileSecurityException("No se pudo encriptar el archivo (error de disco).");
        }

        // Borrado seguro del original si se pidió
        if (request.DeleteOriginal == true)
        {
            await ShredCoreAsync(request.InputPath, 3, cancellationToken);
        }

        return new FileEncryptResult(
            outputPath,
            await ComputeSha256Async(outputPath, cancellationToken));
    }

    public async Task<FileDecryptResult> DecryptAsync(
        FileDecryptRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatePath(request.InputPath);
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new FileSecurityException("La contraseña no puede estar vacía.");
        }

        await using var input = File.OpenRead(request.InputPath);

        byte[] header;
        try
        {
            header = new byte[MagicHeader.Length + SaltSize + IvSize];
            await input.ReadExactlyAsync(header, cancellationToken);
        }
        catch (IOException)
        {
            // Truncado o ilegible: ni siquiera llega a tener el header
            // completo (36 bytes) — mismo mensaje controlado que la magia.
            throw new FileSecurityException("El archivo no es un .pcsuite-enc válido (header incorrecto).");
        }

        // Validar magia
        var magic = Encoding.ASCII.GetString(header, 0, MagicHeader.Length);
        if (magic != MagicHeader)
        {
            throw new FileSecurityException("El archivo no es un .pcsuite-enc válido (header incorrecto).");
        }

        var salt = header[MagicHeader.Length..(MagicHeader.Length + SaltSize)];
        var iv = header[(MagicHeader.Length + SaltSize)..];

        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(request.Password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);

        var outputPath = request.OutputPath ?? Path.ChangeExtension(request.InputPath, null);
        outputPath = outputPath.EndsWith(".pcsuite-enc")
            ? outputPath[..^".pcsuite-enc".Length]
            : outputPath;
        if (string.IsNullOrEmpty(Path.GetExtension(outputPath)))
        {
            outputPath += ".decrypted";
        }

        // NUNCA sobrescribir: el nombre derivado suele ser la ruta del
        // ORIGINAL todavía presente (encrypt sin "borrar original" y decrypt
        // comparten carpeta). Incidente 2026-10-26: el descifrado pisó el
        // PDF original y el usuario perdió la única copia no encriptada —
        // de aquí en adelante el desencriptado aterriza en "nombre (1).ext".
        outputPath = DeriveNonCollidingPath(outputPath);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        try
        {
            await using var output = File.Create(outputPath);
            using (var decryptor = aes.CreateDecryptor())
            await using (var crypto = new CryptoStream(output, decryptor, CryptoStreamMode.Write))
            {
                // Streaming en ambos sentidos: los límites de lectura del
                // descifrado NO dependen de los de la escritura (la garantía
                // que el cifrado por TransformFinalBlock por chunk rompía).
                await input.CopyToAsync(crypto, 64 * 1024, cancellationToken);
            }
        }
        catch (CryptographicException)
        {
            // Contraseña incorrecta o ciphertext dañado: sin salida parcial.
            TryDelete(outputPath);
            throw new FileSecurityException("No se pudo descifrar: contraseña incorrecta o archivo no válido.");
        }
        catch (ArgumentException)
        {
            TryDelete(outputPath);
            throw new FileSecurityException("La ruta del archivo no es válida.");
        }
        catch (IOException)
        {
            TryDelete(outputPath);
            throw new FileSecurityException("No se pudo descifrar el archivo (error de disco).");
        }

        return new FileDecryptResult(
            outputPath,
            await ComputeSha256Async(outputPath, cancellationToken));
    }

    public async Task<FileShredResult> ShredAsync(
        FileShredRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatePath(request.Path);
        var passes = Math.Clamp(request.Passes ?? 3, 1, 7);

        await ShredCoreAsync(request.Path, passes, cancellationToken);
        return new FileShredResult(request.Path, passes);
    }

    public async Task<FileHashResult> HashAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ValidatePath(path);

        using var md5 = MD5.Create();
        using var sha1 = SHA1.Create();
        using var sha256 = SHA256.Create();
        var crc32 = new Crc32();

        await using var stream = File.OpenRead(path);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            md5.TransformBlock(buffer, 0, read, null, 0);
            sha1.TransformBlock(buffer, 0, read, null, 0);
            sha256.TransformBlock(buffer, 0, read, null, 0);
            crc32.Append(buffer.AsSpan(0, read));
        }

        md5.TransformFinalBlock([], 0, 0);
        sha1.TransformFinalBlock([], 0, 0);
        sha256.TransformFinalBlock([], 0, 0);

        return new FileHashResult(
            path,
            Convert.ToHexString(sha256.Hash!).ToLowerInvariant(),
            Convert.ToHexString(sha1.Hash!).ToLowerInvariant(),
            Convert.ToHexString(md5.Hash!).ToLowerInvariant(),
            crc32.Value);
    }

    public Task<StegoEmbedResult> StegoEmbedAsync(
        StegoEmbedRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Esteganografía se implementa en la sub-tarea 33.2.");
    }

    public Task<StegoExtractResult> StegoExtractAsync(
        StegoExtractRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Esteganografía se implementa en la sub-tarea 33.2.");
    }

    // ------------------------------------------------------------------

    private static async Task ShredCoreAsync(string path, int passes, CancellationToken ct)
    {
        var size = new FileInfo(path).Length;
        var buffer = new byte[64 * 1024];

        for (var pass = 0; pass < passes; pass++)
        {
            // Última pasada: ceros; el resto, aleatorio.
            var zeros = pass == passes - 1;
            if (zeros)
            {
                Array.Clear(buffer);
            }

            await using var stream = await OpenForWriteWithRetryAsync(path, ct);
            var written = 0L;
            while (written < size)
            {
                if (!zeros)
                {
                    RandomNumberGenerator.Fill(buffer);
                }

                var toWrite = (int)Math.Min(buffer.Length, size - written);
                await stream.WriteAsync(buffer.AsMemory(0, toWrite), ct);
                written += toWrite;
            }

            stream.Flush();
            stream.Flush(flushToDisk: true);
        }

        await DeleteWithRetryAsync(path, ct);
    }

    /// <summary>
    /// El antivirus o el indexador retienen un instante los archivos
    /// recién escritos (la carrera que hacía flaquear el shred en la suite
    /// completa): reintentos breves antes de dejar pasar el IOException.
    /// </summary>
    private static async Task<FileStream> OpenForWriteWithRetryAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(50, ct);
            }
        }
    }

    private static async Task DeleteWithRetryAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(50, ct);
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Ruta libre de colisiones: inserta " (1)", " (2)"… antes de la
    /// extensión mientras el destino exista. El desencriptado jamás
    /// destruye un archivo que ya estaba en disco.
    /// </summary>
    private static string DeriveNonCollidingPath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            throw new FileSecurityException("La ruta debe ser absoluta.");
        }

        if (!File.Exists(path))
        {
            throw new FileSecurityException($"El archivo no existe: {path}");
        }
    }
}

/// <summary>
/// CRC32 (IEEE 802.3) — acumulador simple y explícito: Append por chunk y
/// Value al final. NO hereda de HashAlgorithm a propósito: la máquina de
/// estados de la clase base (Initialize/State) re-inicializa el registro
/// alrededor del TransformFinalBlock y el valor leído después llegaba en 0
/// (bug 2026-10-26, cazado por el vector de prueba "123456789" → 0xCBF43926).
/// Con ciclo de vida propio el resultado es determinista.
/// </summary>
internal sealed class Crc32
{
    private const uint Polynomial = 0xEDB88320;
    private static readonly uint[] Table = BuildTable();

    private uint _hash = 0xFFFFFFFF;

    /// <summary>Valor final CRC-32 de todo lo acumulado con <see cref="Append"/>.</summary>
    public uint Value => ~_hash;

    public void Append(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            _hash = (_hash >> 8) ^ Table[(_hash ^ b) & 0xFF];
        }
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var entry = i;
            for (var j = 0; j < 8; j++)
            {
                entry = (entry & 1) != 0 ? Polynomial ^ (entry >> 1) : entry >> 1;
            }
            table[i] = entry;
        }
        return table;
    }
}
