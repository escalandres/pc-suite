namespace PCSuite.Contracts;

/// <summary>Resultado de limpieza de una categoría (cleaner.clean).</summary>
public sealed class CleanerCategoryCleanResult
{
    /// <summary>Id fijo de la categoría (userTemp/windowsTemp/updateCache).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Ficheros borrados.</summary>
    public long DeletedFiles { get; init; }

    /// <summary>Bytes liberados.</summary>
    public long FreedBytes { get; init; }

    /// <summary>Ficheros omitidos (en uso o sin permiso; p. ej. carpetas que requieren administrador).</summary>
    public long SkippedFiles { get; init; }

    /// <summary>Detalle controlado (p. ej. "requiere administrador"); se omite si no hay.</summary>
    public string? Detail { get; init; }
}
