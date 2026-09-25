namespace PCSuite.Contracts;

/// <summary>Resultado de <see cref="OperationIds.WslStatus"/> (Task 007).</summary>
public sealed class WslStatusResult
{
    /// <summary>True si wsl.exe se encontró en la PATH.</summary>
    public bool Installed { get; init; }

    /// <summary>Ruta resolvida de wsl.exe; se omite si no está instalado.</summary>
    public string? Path { get; init; }

    /// <summary>running | stopped | unknown (estado del conjunto de distribuciones).</summary>
    public string State { get; init; } = "unknown";

    /// <summary>Versión de WSL (wsl --version); se omite si es desconocida.</summary>
    public string? Version { get; init; }

    /// <summary>Distribución por defecto (wsl --status); se omite si es desconocida.</summary>
    public string? DefaultDistro { get; init; }
}
