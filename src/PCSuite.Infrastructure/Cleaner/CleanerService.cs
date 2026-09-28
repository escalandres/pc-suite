using PCSuite.Contracts;
using PCSuite.Core.Cleaner;

namespace PCSuite.Infrastructure.Cleaner;

// Task 011 / parte 11.2: escaneo y borrado SOLO dentro de raíces fijas. El
// frontend jamás envía rutas (solo ids de CleanerCategoryIds); el mapa de
// raíces se inyecta aquí y en producción lo construye CreateDefaultRoots con
// las tres ubicaciones del sistema. Sin elevación: lo que la ACL no permite
// se omite y se contabiliza, nunca rompe la operación (§16 "Future:
// Temporary file cleanup").

/// <summary>Raíz fija de una categoría: etiqueta visible + ruta absoluta resuelta.</summary>
public sealed record CleanerRoot(string Label, string Path);

public sealed class CleanerService : ICleanerService
{
    private readonly Dictionary<string, CleanerRoot> _roots;

    /// <summary>Raíces de producción: Temp de usuario, C:\Windows\Temp y SoftwareDistribution\Download.</summary>
    public CleanerService()
        : this(CreateDefaultRoots())
    {
    }

    /// <summary>Raíces inyectables (tests); debe cubrir las 3 categorías fijas.</summary>
    public CleanerService(IReadOnlyDictionary<string, CleanerRoot> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        foreach (var id in CleanerCategoryIds.All)
        {
            if (!roots.TryGetValue(id, out var root) || string.IsNullOrWhiteSpace(root.Path))
            {
                throw new InvalidOperationException($"Raiz fija ausente para la categoria '{id}'.");
            }
        }

        _roots = new Dictionary<string, CleanerRoot>(roots, StringComparer.Ordinal);
    }

    public Task<CleanerScanOutcome> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () => new CleanerScanOutcome(
                CleanerCategoryIds.All
                    .Select(id => ScanCategory(id, cancellationToken))
                    .ToList()),
            cancellationToken);

    public async Task<CleanerCleanOutcome> CleanAsync(
        IReadOnlyCollection<string> categoryIds,
        CancellationToken cancellationToken = default)
    {
        // Validación estricta: ids desconocidos o lista vacía → error controlado
        // (mensaje genérico, sin eco del id recibido, nunca una ruta).
        if (categoryIds is null || categoryIds.Count == 0)
        {
            throw new CleanerException(
                BridgeErrorCodes.CleanerFailed,
                "Debe indicar al menos una categoria.");
        }

        foreach (var id in categoryIds)
        {
            if (!CleanerCategoryIds.IsKnown(id))
            {
                throw new CleanerException(
                    BridgeErrorCodes.CleanerFailed,
                    "Categoria no valida.");
            }
        }

        var ordered = categoryIds.Distinct(StringComparer.Ordinal).ToList();
        var results = await Task.Run(
            () => ordered.Select(id => CleanCategory(id, cancellationToken)).ToList(),
            cancellationToken).ConfigureAwait(false);

        return new CleanerCleanOutcome(results);
    }

    private CleanerCategoryScan ScanCategory(string id, CancellationToken cancellationToken)
    {
        var root = _roots[id];
        long files = 0;
        long size = 0;
        var accessible = true;

        try
        {
            foreach (var file in EnumerateFiles(root.Path, cancellationToken))
            {
                try
                {
                    size += new FileInfo(file).Length;
                    files++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Fichero desaparecido o ilegible entre enumerate y stat: se cuenta como si no existiera.
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Raíz ausente: 0 ficheros y accesible (no es un problema de ACL).
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Raíz ilegible (ACL): la UI lo muestra como "sin permisos".
            accessible = false;
        }

        return new CleanerCategoryScan(id, root.Label, root.Path, files, size, accessible);
    }

    private CleanerCategoryClean CleanCategory(string id, CancellationToken cancellationToken)
    {
        var root = _roots[id];
        long deleted = 0;
        long freed = 0;
        long skipped = 0;

        try
        {
            foreach (var file in EnumerateFiles(root.Path, cancellationToken))
            {
                long length;
                try
                {
                    length = new FileInfo(file).Length;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue; // desapareció o ilegible: no es skipped ni deleted
                }

                try
                {
                    File.Delete(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    skipped++; // en uso (locked) o sin ACL: se omite, nunca rompe
                    continue;
                }

                deleted++;
                freed += length;
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Raíz ausente: nada que borrar.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CleanerCategoryClean(
                id, 0, 0, 0, "Sin permisos sobre la carpeta; puede requerir administrador.");
        }

        // ponytail: los directorios vacíos que queden no se eliminan (0 bytes, inofensivos).
        var detail = skipped > 0 ? "Archivos en uso o sin permisos omitidos." : null;
        return new CleanerCategoryClean(id, deleted, freed, skipped, detail);
    }

    /// <summary>
    /// Enumeración recursiva fija: ignora directorios inaccesibles (ACL) y
    /// NUNCA sigue reparse points, de modo que una junction dentro de la raíz
    /// no puede sacar el borrado fuera de ella.
    /// </summary>
    private static IEnumerable<string> EnumerateFiles(string root, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var file in Directory.EnumerateFiles(root, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return file;
        }
    }

    private static IReadOnlyDictionary<string, CleanerRoot> CreateDefaultRoots()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return new Dictionary<string, CleanerRoot>(StringComparer.Ordinal)
        {
            [CleanerCategoryIds.UserTemp] = new("Temp del usuario", Path.Combine(localAppData, "Temp")),
            [CleanerCategoryIds.WindowsTemp] = new("Temp de Windows", Path.Combine(windows, "Temp")),
            [CleanerCategoryIds.UpdateCache] =
                new("Caché de Windows Update", Path.Combine(windows, "SoftwareDistribution", "Download")),
        };
    }
}
