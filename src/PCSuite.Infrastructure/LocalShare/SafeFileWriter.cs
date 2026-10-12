using System.Security.Cryptography;
using System.Text;

namespace PCSuite.Infrastructure.LocalShare;

/// <summary>
/// Escritura segura de archivos recibidos: valida rutas contra
/// directory traversal, usa nombres temporales aleatorios (GUID),
/// computa SHA-256 en streaming, y publica el archivo final solo
/// después de validar tamaño e integridad.
/// </summary>
public sealed class SafeFileWriter : IDisposable
{
    private readonly string _downloadDirectory;
    private readonly string _tempDirectory;

    public SafeFileWriter(string downloadDirectory)
    {
        _downloadDirectory = downloadDirectory;
        _tempDirectory = Path.Combine(downloadDirectory, ".pcshare-tmp");
        Directory.CreateDirectory(_downloadDirectory);
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Crea un archivo temporal con nombre GUID aleatorio (nunca controlado
    /// por el emisor) para escribir el contenido mientras se valida.
    /// </summary>
    public string CreateTempFile()
    {
        var tempPath = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.part");
        File.Create(tempPath).Dispose();
        return tempPath;
    }

    /// <summary>
    /// Valida una ruta relativa recibida del emisor. Rechaza rutas
    /// absolutas, .., segmentos de unidad, UNC, caracteres de control,
    /// y cualquier ruta que escape del directorio de recepción.
    /// Devuelve la ruta relativa normalizada o lanza ArgumentException.
    /// </summary>
    public string ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("La ruta relativa no puede estar vacía.");
        }

        // Rechazar caracteres de control
        if (relativePath.Any(char.IsControl))
        {
            throw new ArgumentException("La ruta contiene caracteres de control.");
        }

        // Rechazar rutas absolutas y UNC
        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith(@"\\"))
        {
            throw new ArgumentException($"Ruta absoluta rechazada: {relativePath}");
        }

        // Normalizar: GetFullPath contra un root ficticio para resolver .., etc.
        var normalized = Path.GetFullPath(Path.Combine("C:\\_root_", relativePath));

        // Verificar que sigue bajo el root ficticio (no escapó)
        if (!normalized.StartsWith("C:\\_root_", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"La ruta intenta escapar del directorio: {relativePath}");
        }

        // Extraer la parte relativa
        var relativePart = normalized["C:\\_root_".Length..].TrimStart('\\');

        // Rechazar segmentos ".." explícitos (ya validado por GetFullPath, pero
        // ser explícito para claridad)
        if (relativePart.Contains(".."))
        {
            throw new ArgumentException($"La ruta contiene '..': {relativePath}");
        }

        if (relativePart.Length == 0)
        {
            throw new ArgumentException("La ruta relativa resulta vacía tras normalizar.");
        }

        return relativePart;
    }

    /// <summary>
    /// Computa SHA-256 de un archivo en streaming (sin cargarlo en memoria).
    /// </summary>
    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Publica el archivo temporal a su destino final. Crea directorios
    /// intermedios según la ruta relativa, verifica integridad (tamaño +
    /// hash), y maneja colisiones añadiendo sufijos (1), (2)...
    /// </summary>
    public string Publish(string tempPath, string relativePath, long expectedSize, string? expectedHash)
    {
        // Validar que el tamaño real coincide
        var actualSize = new FileInfo(tempPath).Length;
        if (actualSize != expectedSize)
        {
            throw new InvalidOperationException(
                $"Tamaño no coincide: esperado {expectedSize}, recibido {actualSize}.");
        }

        // Validar hash si se declaró
        if (expectedHash is not null)
        {
            var actualHash = ComputeSha256(tempPath);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Hash SHA-256 no coincide: esperado {expectedHash}, obtenido {actualHash}.");
            }
        }

        // Construir ruta final
        var safeRelative = ValidateRelativePath(relativePath);
        var finalPath = Path.Combine(_downloadDirectory, safeRelative);
        var finalDir = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(finalDir))
        {
            Directory.CreateDirectory(finalDir);
        }

        // Manejar colisiones: nunca sobrescribir silenciosamente
        if (File.Exists(finalPath))
        {
            finalPath = FindNonConflictingPath(finalPath);
        }

        // Mover: atomic en el mismo volumen
        File.Move(tempPath, finalPath);
        return finalPath;
    }

    /// <summary>Elimina un archivo temporal (llamado tras error o cancelación).</summary>
    public void DiscardTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Best-effort: no dejar que la limpieza de un temporal falle la transferencia.
        }
    }

    /// <summary>Limpia todos los temporales huérfanos (al arrancar o al limpiar historial).</summary>
    public void CleanupOrphanedTempFiles()
    {
        foreach (var file in Directory.EnumerateFiles(_tempDirectory, "*.part"))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    private static string FindNonConflictingPath(string originalPath)
    {
        var directory = Path.GetDirectoryName(originalPath) ?? "";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
        var extension = Path.GetExtension(originalPath);

        for (var i = 1; i < 100; i++)
        {
            var candidate = Path.Combine(directory, $"{nameWithoutExt} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Fallback extremo: GUID
        return Path.Combine(directory, $"{nameWithoutExt} ({Guid.NewGuid():N}){extension}");
    }

    public void Dispose() => CleanupOrphanedTempFiles();
}
