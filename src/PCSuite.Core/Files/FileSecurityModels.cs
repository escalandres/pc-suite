namespace PCSuite.Core.Files;

/// <summary>Fallo controlado de seguridad de archivos (sin stack traces).</summary>
public sealed class FileSecurityException(string message) : Exception(message);

// --- Encriptar / Desencriptar ---
// Kind: "file" (PCS1) o "folder" (PCD1, Task 033.5) — el frontend ramifica
// el mensaje del resultado según qué se protegió.

public sealed record FileEncryptRequest(
    string InputPath,
    string Password,
    string? OutputPath = null,
    bool? DeleteOriginal = null);

public sealed record FileEncryptResult(string OutputPath, string Sha256, string Kind = "file");

public sealed record FileDecryptRequest(
    string InputPath,
    string Password,
    string? OutputPath = null);

public sealed record FileDecryptResult(string OutputPath, string Sha256, string Kind = "file");

// --- Borrado seguro ---

public sealed record FileShredRequest(string Path, int? Passes = null);

public sealed record FileShredResult(string Path, int Passes);

// --- Hash multi-algoritmo ---

public sealed record FileHashResult(
    string Path,
    string Sha256,
    string Sha1,
    string Md5,
    uint Crc32);

// --- Esteganografía (33.2) ---

public sealed record StegoEmbedRequest(
    string ImagePath,
    string? TextPayload = null,
    string? FilePayloadPath = null,
    string? Password = null);

public sealed record StegoEmbedResult(string OutputPath);

public sealed record StegoExtractRequest(
    string ImagePath,
    string? Password = null);

public sealed record StegoExtractResult(
    string? Text,
    string? OutputFilePath,
    bool HasFilePayload);

// --- Interfaz ---

public interface IFileSecurityService
{
    Task<FileEncryptResult> EncryptAsync(FileEncryptRequest request, CancellationToken cancellationToken = default);
    Task<FileDecryptResult> DecryptAsync(FileDecryptRequest request, CancellationToken cancellationToken = default);
    Task<FileShredResult> ShredAsync(FileShredRequest request, CancellationToken cancellationToken = default);
    Task<FileHashResult> HashAsync(string path, CancellationToken cancellationToken = default);
    Task<StegoEmbedResult> StegoEmbedAsync(StegoEmbedRequest request, CancellationToken cancellationToken = default);
    Task<StegoExtractResult> StegoExtractAsync(StegoExtractRequest request, CancellationToken cancellationToken = default);
}
