namespace PCSuite.Contracts;

/// <summary>Un paquete en la tabla devuelta por winget (búsqueda/lista/updates).</summary>
public sealed class WinGetPackageResult
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string Version { get; init; } = "";

    /// <summary>Versión disponible (solo winget.updates); se omite del JSON si es null.</summary>
    public string? Available { get; init; }

    /// <summary>Fragmento coincidente (solo winget.search); se omite del JSON si es null.</summary>
    public string? Match { get; init; }
}
