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

        await using var input = File.OpenRead(request.InputPath);
        await using var output = File.Create(outputPath);

        // Header: magia + salt + IV
        await output.WriteAsync(Encoding.ASCII.GetBytes(MagicHeader), cancellationToken);
        await output.WriteAsync(salt, cancellationToken);
        await output.WriteAsync(iv, cancellationToken);

        // AES-256-CBC en streaming (64KB chunks)
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var encrypted = encryptor.TransformFinalBlock(buffer, 0, read);
            await output.WriteAsync(encrypted, cancellationToken);
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
        var header = new byte[MagicHeader.Length + SaltSize + IvSize];
        await input.ReadExactlyAsync(header, cancellationToken);

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

        await using var output = File.Create(outputPath);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var buffer = new byte[64 * 1024 + 32]; // +32 para el padding final
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var decrypted = decryptor.TransformFinalBlock(buffer, 0, read);
            await output.WriteAsync(decrypted, cancellationToken);
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
        using var crc32 = new Crc32();

        await using var stream = File.OpenRead(path);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            md5.TransformBlock(buffer, 0, read, null, 0);
            sha1.TransformBlock(buffer, 0, read, null, 0);
            sha256.TransformBlock(buffer, 0, read, null, 0);
            crc32.TransformBlock(buffer, 0, read, null, 0);
        }

        md5.TransformFinalBlock([], 0, 0);
        sha1.TransformFinalBlock([], 0, 0);
        sha256.TransformFinalBlock([], 0, 0);
        crc32.TransformFinalBlock([], 0, 0);

        return new FileHashResult(
            path,
            Convert.ToHexString(sha256.Hash!).ToLowerInvariant(),
            Convert.ToHexString(sha1.Hash!).ToLowerInvariant(),
            Convert.ToHexString(md5.Hash!).ToLowerInvariant(),
            crc32.HashUInt32);
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
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            stream.Seek(0, SeekOrigin.Begin);
            var written = 0L;
            while (written < size)
            {
                if (pass == passes - 1)
                {
                    // Última pasada: ceros
                    Array.Clear(buffer);
                }
                else
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

        File.Delete(path);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
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

/// <summary>CRC32 (IEEE 802.3) para el hash multi-algoritmo de archivos.</summary>
internal sealed class Crc32 : HashAlgorithm
{
    private const uint Polynomial = 0xEDB88320;
    private static readonly uint[] Table = BuildTable();

    private uint _hash;

    public uint HashUInt32 => _hash;

    public override void Initialize() => _hash = 0xFFFFFFFF;

    protected override void HashCore(byte[] array, int ibStart, int cbSize)
    {
        for (var i = ibStart; i < ibStart + cbSize; i++)
        {
            _hash = (_hash >> 8) ^ Table[(_hash ^ array[i]) & 0xFF];
        }
    }

    protected override byte[] HashFinal()
    {
        var final = ~_hash;
        return BitConverter.GetBytes(final);
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
