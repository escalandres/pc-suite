namespace PCSuite.Core.WinGet;

/// <summary>
/// Fachada de winget (Task 006). El frontend NUNCA envía argumentos de
/// comando: solo payloads estructurados que estas operaciones validan antes
/// de construir los argv fijos. Las lecturas y las mutaciones se serializan
/// entre sí (el índice de winget no admite ejecuciones concurrentes).
/// </summary>
public interface IWinGetService
{
    /// <summary>Detección de winget (instalado, versión, ruta) + privilegio actual.</summary>
    Task<WinGetDetection> DetectAsync(CancellationToken cancellationToken = default);

    /// <summary>Busca paquetes por texto libre en el origen winget.</summary>
    Task<IReadOnlyList<WinGetPackage>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Paquetes instalados cuyo origen es winget.</summary>
    Task<IReadOnlyList<WinGetPackage>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Paquetes con actualización disponible ([] si no hay ninguna).</summary>
    Task<IReadOnlyList<WinGetPackage>> UpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Instala un paquete por id exacto (puede elevar vía UAC si hace falta).</summary>
    Task<WinGetMutationOutcome> InstallAsync(string packageId, CancellationToken cancellationToken = default);

    /// <summary>Actualiza un paquete concreto por id exacto.</summary>
    Task<WinGetMutationOutcome> UpgradeAsync(string packageId, CancellationToken cancellationToken = default);

    /// <summary>Actualiza todos los paquetes actualizables.</summary>
    Task<WinGetMutationOutcome> UpgradeAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Desinstala un paquete por id exacto.</summary>
    Task<WinGetMutationOutcome> UninstallAsync(string packageId, CancellationToken cancellationToken = default);
}
