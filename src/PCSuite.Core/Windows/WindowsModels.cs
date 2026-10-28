namespace PCSuite.Core.Windows;

/// <summary>Fallo controlado del catálogo de comandos de reparación (sin stack traces).</summary>
public sealed class WindowsRepairException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

/// <summary>
/// Una entrada del catálogo de comandos de Windows (Task 036). El argv es
/// FIJO por diseño: el frontend solo conoce el <see cref="Id"/> — no existe
/// ruta a un comando arbitrario (el puente es tipado, jamás un shell).
/// </summary>
public sealed record WindowsCommand(
    string Id,
    string Name,
    string Description,
    string Category,
    string FileName,
    IReadOnlyList<string> Arguments,
    string DisplayCommand,
    string DurationHint,
    bool Heavy,
    string LogPath);

/// <summary>Desenlace de un comando ejecutado.</summary>
public sealed record WindowsRunOutcome(
    bool Succeeded,
    int ExitCode,
    string Message,
    string LogHint);

public interface IWindowsRepairService
{
    /// <summary>El catálogo completo (para windows.listCommands).</summary>
    IReadOnlyList<WindowsCommand> List();

    /// <summary>
    /// Ejecuta el comando del catálogo con elevación por operación (UAC),
    /// consola visible y SIN tope de reloj (lección 024). Devuelve el
    /// desenlace ya traducido a mensaje controlado.
    /// </summary>
    Task<WindowsRunOutcome> RunAsync(string commandId, CancellationToken cancellationToken = default);
}
