namespace PCSuite.Core.Cleaner;

/// <summary>
/// Fachada del Limpiador (Task 011 / §16 "Temporary file cleanup"). Limpieza
/// de archivos temporales en RAÍCES FIJAS: el frontend solo envía ids de
/// categoría de <see cref="CleanerCategoryIds"/> — nunca rutas — y el
/// servicio valida la entrada, resuelve las ríces y garantiza que no se borra
/// nada fuera de ellas (sin reparse points ni traversal). Sin elevación: lo
/// que la ACL no permite se omite y se contabiliza, jamás rompe la operación.
/// </summary>
public interface ICleanerService
{
    /// <summary>Tamaño y ficheros por categoría (payload cero; una fila por id fijo).</summary>
    Task<CleanerScanOutcome> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Borra dentro de las raíces fijas las categorías pedidas. Id desconocido,
    /// lista vacía o no-objeto → <see cref="CleanerException"/> controlada.
    /// </summary>
    Task<CleanerCleanOutcome> CleanAsync(
        IReadOnlyCollection<string> categoryIds,
        CancellationToken cancellationToken = default);
}
