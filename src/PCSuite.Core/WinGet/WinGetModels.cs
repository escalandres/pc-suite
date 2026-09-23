namespace PCSuite.Core.WinGet;

/// <summary>Fila de paquete ya separada de la tabla de winget (columnas posicionales).</summary>
public sealed record WinGetPackage(
    string Id,
    string Name,
    string Version,
    string? Available = null,
    string? Match = null);

/// <summary>Resultado de la detección de winget (Stage 7: Detection + permisos).</summary>
public sealed record WinGetDetection(
    bool Installed,
    string? Version,
    string? Path,
    bool IsAdministrator);

/// <summary>
/// Resultado de una mutación (install/upgrade/upgradeAll/uninstall).
/// <see cref="Succeeded"/> refleja el código de salida de winget; un fallo
/// controlado NO lanza excepción (solo los errores del propio servicio sí).
/// </summary>
public sealed record WinGetMutationOutcome(
    string? PackageId,
    bool Succeeded,
    int ExitCode,
    bool Elevated,
    string? Detail);
