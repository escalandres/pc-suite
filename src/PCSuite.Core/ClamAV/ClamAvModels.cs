namespace PCSuite.Core.ClamAV;

/// <summary>Tipos de escaneo (normalizados para la UI y el historial).</summary>
public static class ClamAvScanKinds
{
    public const string Quick = "quick";

    public const string Full = "full";

    public const string Custom = "custom";
}

/// <summary>Estado de ClamAV detectado (clamscan localizado + freshclam junto).</summary>
public sealed record ClamAvStatus(
    bool Installed,
    string? Path,
    string? Version,
    string? Definitions,
    bool CanUpdateDefinitions);

/// <summary>
/// Resultado de una mutación (install/updateDefinitions). <see cref="Succeeded"/>
/// refleja el código de salida del CLI o de winget; un fallo controlado NO es
/// excepción (solo los errores del propio servicio sí lanzan ClamAvException).
/// </summary>
public sealed record ClamAvMutationOutcome(
    bool Succeeded,
    int ExitCode,
    bool Elevated,
    string? Detail);

/// <summary>Amenaza detectada (ruta del archivo + firma que lo marcó).</summary>
public sealed record ClamAvThreat(string File, string Threat);

/// <summary>Resultado de un escaneo (salida resumida de clamscan).</summary>
public sealed record ClamAvScanOutcome(
    bool Succeeded,
    int ExitCode,
    IReadOnlyList<ClamAvThreat> Threats,
    int ScannedFiles,
    int ScannedDirectories,
    int InfectedFiles,
    string? EngineVersion,
    int? KnownViruses,
    string? Detail);

/// <summary>Entrada del historial de escaneos (persistido en JSON).</summary>
public sealed record ClamAvScanHistoryEntry(
    DateTimeOffset Timestamp,
    string Kind,
    string? Path,
    bool Succeeded,
    int ScannedFiles,
    int InfectedFiles,
    IReadOnlyList<ClamAvThreat> Threats,
    string? Detail);
