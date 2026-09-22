namespace PCSuite.Core.Processes;

/// <summary>
/// Solicitud tipada de ejecución de un proceso. Los argumentos viajan como
/// lista (se pasan al sistema sin pasar por un shell: sin injection de
/// comandos). Los callbacks permiten recibir la salida en streaming.
/// </summary>
public sealed record ProcessRequest
{
    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    /// <summary>Si el proceso excede este tiempo, se termina y se lanza <see cref="ProcessTimeoutException"/>.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Elevación por operación (Task 005): ShellExecute con "runas" — el
    /// prompt UAC lo genera Windows—, sin redirección de streams ni modo
    /// interactivo, y FileName obligatoriamente en ruta absoluta. La app
    /// completa jamás corre elevada.
    /// </summary>
    public bool RequiresElevation { get; init; }

    /// <summary>
    /// Lanzamiento interactivo visible (Task 004, "Launch where applicable"):
    /// sin redirección de streams y con ventana de consola propia, para que la
    /// herramienta se comunique directamente con el usuario (REPL, TUI).
    /// Incompatible con <see cref="Timeout"/> y con los callbacks de línea
    /// (el proceso vive hasta que el usuario lo cierre).
    /// </summary>
    public bool Interactive { get; init; }

    /// <summary>Se invoca por cada línea de stdout conforme llega (streaming).</summary>
    public Action<string>? OnStandardOutput { get; init; }

    /// <summary>Se invoca por cada línea de stderr conforme llega (streaming).</summary>
    public Action<string>? OnStandardError { get; init; }
}
