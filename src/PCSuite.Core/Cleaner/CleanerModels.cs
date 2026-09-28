namespace PCSuite.Core.Cleaner;

/// <summary>
/// Ids de categoría fijos del Limpiador (Task 011 / §16). El frontend solo
/// puede nombrar estos ids: las rutas absolutas se resuelven aquí y el
/// servicio borra exclusivamente dentro de esas raíces.
/// </summary>
public static class CleanerCategoryIds
{
    /// <summary>Temp del usuario (%LOCALAPPDATA%\Temp): limpio sin administrador.</summary>
    public const string UserTemp = "userTemp";

    /// <summary>C:\Windows\Temp: parcial sin administrador (ACL).</summary>
    public const string WindowsTemp = "windowsTemp";

    /// <summary>Caché de Windows Update (SoftwareDistribution\Download): requiere administrador.</summary>
    public const string UpdateCache = "updateCache";

    /// <summary>Conjunto cerrado y en orden fijo de ids válidos.</summary>
    public static readonly IReadOnlyList<string> All = [UserTemp, WindowsTemp, UpdateCache];

    /// <summary>True si el id pertenece a la lista fija (comparación ordinal).</summary>
    public static bool IsKnown(string? id) =>
        id is not null && All.Contains(id, StringComparer.Ordinal);
}

/// <summary>Categoría escaneada: raíz fija resuelta + tamaño antes de limpiar.</summary>
public sealed record CleanerCategoryScan(
    string Id,
    string Label,
    string Path,
    long FileCount,
    long SizeBytes,
    bool Accessible);

/// <summary>Resultado de un escaneo: siempre la lista completa de categorías.</summary>
public sealed record CleanerScanOutcome(IReadOnlyList<CleanerCategoryScan> Categories);

/// <summary>
/// Resultado de limpieza de una categoría. Un fallo parcial (ficheros en uso
/// o sin ACL) NO es excepción: se contabiliza en <see cref="SkippedFiles"/>.
/// </summary>
public sealed record CleanerCategoryClean(
    string Id,
    long DeletedFiles,
    long FreedBytes,
    long SkippedFiles,
    string? Detail);

/// <summary>Resultado de la limpieza de las categorías solicitadas.</summary>
public sealed record CleanerCleanOutcome(IReadOnlyList<CleanerCategoryClean> Categories);
