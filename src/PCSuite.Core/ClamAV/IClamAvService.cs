namespace PCSuite.Core.ClamAV;

/// <summary>
/// Fachada de ClamAV (Task 008 / §13). PCSuite orquesta el CLI, no es el motor
/// antivirus. El frontend envía payloads estructurados (la única ruta es la del
/// customScan, validada aquí como ruta absoluta existente); los argv siguen
/// siendo arreglos fijos construidos en Infrastructure, sin shell y nunca una
/// línea "clamscan …" arbitraria. Escaneos y freshclam se serializan entre sí;
/// status/history no, para que sigan respondiendo durante un escaneo largo.
/// </summary>
public interface IClamAvService
{
    /// <summary>Detección: instalado/no, ruta, versión, definiciones y freshclam.</summary>
    Task<ClamAvStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Instala ClamAV con el paquete fijo Cisco.ClamAV vía winget (reutiliza la
    /// elevación por operación del Task 006 cuando el instalador lo requiere).
    /// </summary>
    Task<ClamAvMutationOutcome> InstallAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Actualiza las definiciones con freshclam. Si falla por permisos (p. ej.
    /// instalación en Program Files) y no es ya administrador, reintenta
    /// elevado vía la allow-list del Task 005 (UAC solo de Windows).
    /// </summary>
    Task<ClamAvMutationOutcome> UpdateDefinitionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Escaneo rápido de ubicaciones comunes del usuario.</summary>
    Task<ClamAvScanOutcome> QuickScanAsync(CancellationToken cancellationToken = default);

    /// <summary>Escaneo completo de todos los discos fijos listos.</summary>
    Task<ClamAvScanOutcome> FullScanAsync(CancellationToken cancellationToken = default);

    /// <summary>Escaneo de una ruta absoluta existente (archivo o carpeta).</summary>
    Task<ClamAvScanOutcome> CustomScanAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Historial de escaneos persistido (más reciente primero).</summary>
    Task<IReadOnlyList<ClamAvScanHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default);
}
