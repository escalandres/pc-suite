namespace PCSuite.Contracts;

/// <summary>Una distribución de <c>wsl --list --verbose</c>.</summary>
public sealed class WslDistroResult
{
    /// <summary>Nombre de la distribución (sin el marcador '*' de la default).</summary>
    public string Name { get; init; } = "";

    /// <summary>Estado en crudo tal como lo escribe wsl (Running / Stopped).</summary>
    public string State { get; init; } = "";

    /// <summary>Versión de plataforma WSL de la distribución (1 o 2).</summary>
    public int Version { get; init; }
}
