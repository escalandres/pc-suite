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
    /// <summary>
    /// Un escaneo terminó con resultado estructurado (Task 021: el host lo usa
    /// para el toast de amenazas cuando el usuario no está mirando). Solo se
    /// dispara cuando el escaneo llegó a correr (las excepciones de
    /// instalación/timeout NO cuentan: no son escaneos). Args: (kind, outcome).
    /// </summary>
    event Action<ClamAvScanOutcome, string>? ScanCompleted;

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
    Task<ClamAvMutationOutcome> UpdateDefinitionsAsync(
        CancellationToken cancellationToken = default,
        IProgress<ClamAvFreshclamProgress>? progress = null);

    /// <summary>Escaneo rápido de ubicaciones comunes del usuario.</summary>
    /// <summary>Valida la ruta de customScan (raíz absoluta, existente, sin NUL) o lanza <see cref="ClamAvException"/>.</summary>
    string ValidateScanPath(string? path);

    Task<ClamAvScanOutcome> QuickScanAsync(
        CancellationToken cancellationToken = default,
        IProgress<ClamAvScanProgress>? progress = null);

    /// <summary>Escaneo completo de todos los discos fijos listos.</summary>
    Task<ClamAvScanOutcome> FullScanAsync(
        CancellationToken cancellationToken = default,
        IProgress<ClamAvScanProgress>? progress = null);

    /// <summary>Escaneo de una ruta absoluta existente (archivo o carpeta).</summary>
    Task<ClamAvScanOutcome> CustomScanAsync(
        string path,
        CancellationToken cancellationToken = default,
        IProgress<ClamAvScanProgress>? progress = null);

    /// <summary>Historial de escaneos persistido (más reciente primero).</summary>
    Task<IReadOnlyList<ClamAvScanHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default);
}
