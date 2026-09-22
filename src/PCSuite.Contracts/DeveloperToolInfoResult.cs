namespace PCSuite.Contracts;

/// <summary>Estado de una herramienta de desarrollador expuesto al frontend.</summary>
public sealed class DeveloperToolInfoResult
{
    /// <summary>Id estable (p. ej. <c>node</c>, <c>claude-code</c>).</summary>
    public string Id { get; init; } = "";

    /// <summary>Nombre visible (p. ej. <c>Node.js</c>).</summary>
    public string Name { get; init; } = "";

    /// <summary>True si el ejecutable se localizó en la PATH.</summary>
    public bool IsInstalled { get; init; }

    /// <summary>Versión reportada por <c>--version</c>; null si es desconocida.</summary>
    public string? Version { get; init; }

    /// <summary>Ruta completa del ejecutable detectado; null si no está instalada.</summary>
    public string? Path { get; init; }
}
