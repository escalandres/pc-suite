namespace PCSuite.Contracts;

/// <summary>Categoría escaneada por cleaner.scan (tamaño y ficheros antes de limpiar).</summary>
public sealed class CleanerCategoryScanResult
{
    /// <summary>Id fijo de la categoría (userTemp/windowsTemp/updateCache).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Etiqueta visible para la UI.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Raíz fija resuelta en el backend (solo transparencia; nunca la escribe el cliente).</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Ficheros encontrados bajo la raíz.</summary>
    public long FileCount { get; init; }

    /// <summary>Tamaño total en bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>False si la raíz no se pudo leer (ACL); la UI lo muestra como "sin permisos".</summary>
    public bool Accessible { get; init; }
}
