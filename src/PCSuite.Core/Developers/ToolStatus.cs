namespace PCSuite.Core.Developers;

/// <summary>Resultado de la detección de una herramienta de desarrollador.</summary>
public sealed class ToolStatus
{
    /// <summary>True si se localizó el ejecutable en la PATH.</summary>
    public bool IsInstalled { get; init; }

    /// <summary>
    /// Versión reportada por <c>--version</c>, sin el prefijo <c>v</c>.
    /// Null si no se pudo determinar (timeout, salida inesperada...).
    /// </summary>
    public string? Version { get; init; }

    /// <summary>Ruta completa del ejecutable detectado.</summary>
    public string? Path { get; init; }
}
