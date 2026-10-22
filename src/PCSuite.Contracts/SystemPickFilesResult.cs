namespace PCSuite.Contracts;

/// <summary>Resultado de system.pickFiles: rutas absolutas del diálogo nativo.</summary>
public sealed class SystemPickFilesResult
{
    /// <summary>Rutas absolutas seleccionadas (vacío si se canceló).</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>El usuario cerró el diálogo sin elegir nada.</summary>
    public bool Cancelled { get; init; }
}

/// <summary>
/// Payload OPCIONAL de system.pickFiles (Task 026): los callers que solo
/// quieren UN archivo (el escaneo de ClamAV) desactivan la multiselección.
/// Sin payload (o inválido) → multiselección: Local Share no cambia.
/// </summary>
public sealed class SystemPickFilesPayload
{
    /// <summary>Permitir elegir varios archivos (true por omisión).</summary>
    public bool Multiselect { get; init; } = true;
}

/// <summary>Resultado de system.pickFolder (Task 026): UNA carpeta absoluta del diálogo nativo.</summary>
public sealed class SystemPickFolderResult
{
    /// <summary>Ruta absoluta de la carpeta elegida (null si se canceló).</summary>
    public string? Path { get; init; }

    /// <summary>El usuario cerró el diálogo sin elegir nada (o la elección no fue una carpeta).</summary>
    public bool Cancelled { get; init; }
}
