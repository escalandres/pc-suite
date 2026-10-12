using PCSuite.Core.Jobs;

namespace PCSuite.Core.LocalShare;

/// <summary>
/// Interfaz de dominio del servicio Local Share.
/// Implementada en Infrastructure; consumida por las operaciones del bridge
/// y (futuro) por el componente Blazor.
/// </summary>
public interface ILocalShareService
{
    // ------------------------------------------------------------------
    // Ciclo de vida del servicio
    // ------------------------------------------------------------------

    /// <summary>Estado actual del servicio (Stopped/Starting/Running/Error).</summary>
    LocalShareServiceStatus Status { get; }

    /// <summary>Puerto TCP efectivo (útil cuando Port=0 y el SO asignó uno).</summary>
    int EffectivePort { get; }

    /// <summary>Inicia el listener TCP+TLS y el responder mDNS si está habilitado.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Detiene el listener, cancela transferencias activas y detiene mDNS.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    // ------------------------------------------------------------------
    // Configuración
    // ------------------------------------------------------------------

    /// <summary>Configuración actual (snapshot).</summary>
    LocalShareSettings Settings { get; }

    /// <summary>Actualiza la configuración y la persiste. Si el servicio está corriendo, reinicia el listener.</summary>
    Task UpdateSettingsAsync(LocalShareSettings settings, CancellationToken cancellationToken = default);

    // ------------------------------------------------------------------
    // Dispositivos
    // ------------------------------------------------------------------

    /// <summary>Dispositivos conocidos: detectados por mDNS + emparejados + vistos por IP manual.</summary>
    IReadOnlyList<SharedDevice> GetDevices();

    /// <summary>Dispositivos emparejados (confiables).</summary>
    IReadOnlyList<SharedDevice> GetPairedDevices();

    /// <summary>
    /// Inicia el emparejamiento con un dispositivo por IP:puerto.
    /// Muestra un código PIN en ambos lados para verificación manual.
    /// </summary>
    Task<SharedDevice> PairAsync(string address, int port, CancellationToken cancellationToken = default);

    /// <summary>Confirma el PIN mostrado en el otro equipo y establece la confianza.</summary>
    Task<SharedDevice> ConfirmPairingAsync(string deviceId, string pin, CancellationToken cancellationToken = default);

    /// <summary>Revoca la confianza de un dispositivo emparejado.</summary>
    Task RevokeTrustAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra un dispositivo emparejado con su huella de certificado.
    /// Se usa tras verificar el PIN: la huella del par ya se conoce
    /// (fue intercambiada durante la negociación TLS).
    /// </summary>
    SharedDevice RegisterPairedDevice(
        string deviceId,
        string displayName,
        string address,
        int port,
        string certificateThumbprint);

    // ------------------------------------------------------------------
    // Transferencias (salientes)
    // ------------------------------------------------------------------

    /// <summary>
    /// Solicita una transferencia de archivos a un dispositivo.
    /// Crea un Job, valida rutas locales y envía la solicitud al receptor.
    /// </summary>
    Task<ShareTransfer> SendAsync(
        string deviceId,
        IReadOnlyList<string> localPaths,
        CancellationToken cancellationToken = default);

    // ------------------------------------------------------------------
    // Transferencias (entrantes)
    // ------------------------------------------------------------------

    /// <summary>Acepta una solicitud entrante (el usuario pulsó Aceptar).</summary>
    Task AcceptAsync(string transferId, CancellationToken cancellationToken = default);

    /// <summary>Rechaza una solicitud entrante (el usuario pulsó Rechazar).</summary>
    Task RejectAsync(string transferId, CancellationToken cancellationToken = default);

    // ------------------------------------------------------------------
    // Transferencias (comunes)
    // ------------------------------------------------------------------

    /// <summary>Todas las transferencias conocidas (activas + historial reciente).</summary>
    IReadOnlyList<ShareTransfer> GetTransfers();

    /// <summary>Detalles de una transferencia con sus items.</summary>
    (ShareTransfer Transfer, IReadOnlyList<ShareTransferItem> Items)? GetTransfer(string transferId);

    /// <summary>Cancela una transferencia en curso (cualquier dirección).</summary>
    bool CancelTransfer(string transferId);

    /// <summary>Elimina transferencias terminadas del historial.</summary>
    Task ClearHistoryAsync(CancellationToken cancellationToken = default);

    // ------------------------------------------------------------------
    // Eventos (hacia la UI via IEventPublisher)
    // ------------------------------------------------------------------

    /// <summary>Un dispositivo fue descubierto o cambió de estado.</summary>
    event Action<SharedDevice>? DeviceChanged;

    /// <summary>Llega una solicitud de transferencia entrante (el receptor debe aceptar/rechazar).</summary>
    event Action<ShareTransfer, IReadOnlyList<ShareTransferItem>>? IncomingRequest;

    /// <summary>Progreso actualizado de una transferencia (throttled ~4/s).</summary>
    event Action<TransferProgress>? ProgressChanged;

    /// <summary>Una transferencia llegó a estado terminal.</summary>
    event Action<ShareTransfer>? TransferFinished;
}
