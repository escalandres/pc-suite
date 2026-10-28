namespace PCSuite.Contracts;

/// <summary>
/// DTO del catálogo de comandos de reparación (Task 036): lo que ve la UI.
/// El argv JAMÁS viaja — solo el id; el displayCommand es informativo.
/// </summary>
public sealed class WindowsCommandInfoResult
{
    /// <summary>Id allow-listed que viaja en windows.runCommand.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Nombre corto (ej. "SFC — reparar archivos de sistema").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Qué hace y cuándo usarlo.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Agrupación en la UI (ej. "Reparación del sistema").</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Comando mostrado al usuario para transparencia ("sfc /scannow").</summary>
    public string DisplayCommand { get; init; } = string.Empty;

    /// <summary>Duración estimada ("segundos", "10–30 min").</summary>
    public string DurationHint { get; init; } = string.Empty;

    /// <summary>Confirmación reforzada en la UI (los que duran o reparan).</summary>
    public bool Heavy { get; init; }

    /// <summary>Log oficial donde queda el detalle (CBS.log, dism.log).</summary>
    public string LogPath { get; init; } = string.Empty;
}

/// <summary>Resultado de windows.listCommands.</summary>
public sealed class WindowsCommandCollectionResult
{
    public List<WindowsCommandInfoResult> Commands { get; init; } = [];
}

/// <summary>Payload de windows.runCommand (Task 036): SOLO el id del catálogo.</summary>
public sealed class WindowsRunPayload
{
    /// <summary>Id allow-listed; cualquier otra cosa se rechaza SIN UAC.</summary>
    public string? CommandId { get; init; }
}

/// <summary>Respuesta inmediata de windows.runCommand: el desenlace llega por el job.</summary>
public sealed class WindowsRunStartResult
{
    public string JobId { get; init; } = string.Empty;
}

/// <summary>
/// Result del job de windows.runCommand (viaja en job.event / job.get):
/// exit code traducido a mensaje apto para la UI + puntero al log oficial.
/// </summary>
public sealed class WindowsRunOutcomeResult
{
    public bool Succeeded { get; init; }

    public int ExitCode { get; init; }

    /// <summary>Mensaje controlado en español (jamás salida cruda).</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Hint de dónde revisar el detalle (CBS.log / dism.log).</summary>
    public string LogHint { get; init; } = string.Empty;
}
