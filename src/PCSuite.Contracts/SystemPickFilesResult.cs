namespace PCSuite.Contracts;

/// <summary>Resultado de system.pickFiles: rutas absolutas del diálogo nativo.</summary>
public sealed class SystemPickFilesResult
{
    /// <summary>Rutas absolutas seleccionadas (vacío si se canceló).</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>El usuario cerró el diálogo sin elegir nada.</summary>
    public bool Cancelled { get; init; }
}
