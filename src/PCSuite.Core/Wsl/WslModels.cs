namespace PCSuite.Core.Wsl;

/// <summary>Estados del conjunto de distribuciones (normalizados para la UI).</summary>
public static class WslStates
{
    public const string Running = "running";

    public const string Stopped = "stopped";

    public const string Unknown = "unknown";
}

/// <summary>Estado de WSL detectado (wsl --version / --status / --list --verbose).</summary>
public sealed record WslStatus(
    bool Installed,
    string? Path,
    string State,
    string? Version,
    string? DefaultDistro);

/// <summary>Fila de distribución de <c>wsl --list --verbose</c> (estado en crudo).</summary>
public sealed record WslDistro(string Name, string State, int Version);

/// <summary>
/// Resultado de una mutación (update/shutdown/start). <see cref="Succeeded"/>
/// refleja el código de salida de wsl; un fallo del CLI NO es excepción
/// (solo los errores controlados del propio servicio sí lanzan WslException).
/// </summary>
public sealed record WslMutationOutcome(
    bool Succeeded,
    int ExitCode,
    bool Elevated,
    string? Detail);
