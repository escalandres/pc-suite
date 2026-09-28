namespace PCSuite.Contracts;

/// <summary>
/// Payload de cleaner.clean: SOLO ids de categoría de la lista fija
/// (userTemp/windowsTemp/updateCache). El frontend jamás envía rutas;
/// la validación y las raíces viven en el servicio.
/// </summary>
public sealed class CleanerCleanPayload
{
    /// <summary>Ids de categoría a limpiar; validados contra la lista fija.</summary>
    public IReadOnlyList<string>? Categories { get; init; }
}
