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
    /// Inicia el emparejamiento con un dispositivo DESCUBIERTO (modelo
    /// Bluetooth de comparación numérica). Conecta por TLS, deriva un
    /// código de 6 dígitos de las dos huellas de la conexión viva y lo
    /// muestra en ambos equipos; el desenlace llega vía PairFinished.
    /// </summary>
    Task<LocalSharePairInitiation> PairWithDeviceAsync(
        string deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acepta una solicitud de emparejamiento entrante (lado receptor):
    /// el usuario comparó el código y coincide.
    /// </summary>
    Task AcceptPairingAsync(string requestId, CancellationToken cancellationToken = default);

    /// <summary>Rechaza una solicitud de emparejamiento entrante.</summary>
    Task RejectPairingAsync(string requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancela un emparejamiento iniciado por ESTE equipo (botón Cancelar de
    /// la tarjeta de código): aborta la conexión y el receptor lo verá como
    /// una desconexión limpia.
    /// </summary>
    Task CancelPairingAsync(string requestId, CancellationToken cancellationToken = default);

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
    // Firewall (diagnóstico sin elevación; regla con UAC puntual)
    // ------------------------------------------------------------------

    /// <summary>
    /// Diagnóstico de solo lectura: ¿existe la regla de firewall entrante
    /// de Local Share y cubre el puerto actual? No eleva.
    /// </summary>
    Task<LocalShareFirewallDiagnostic> GetFirewallDiagnosticAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea la regla de firewall entrante (TCP, solo el puerto del servicio,
    /// solo PCSuite.exe) pidiendo UAC puntual vía ElevationManager.
    /// </summary>
    Task AddFirewallRuleAsync(CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Pausa un ENVÍO en curso (16.7.4): el flujo de chunks se congela hasta
    /// ResumeTransferAsync o cancelación. Solo transferencias salientes en
    /// estado Transferring.
    /// </summary>
    Task PauseTransferAsync(string transferId, CancellationToken cancellationToken = default);

    /// <summary>Reanuda un envío pausado.</summary>
    Task ResumeTransferAsync(string transferId, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Solicitud de emparejamiento entrante (lado receptor): mostrar el
    /// código de comparación y pedir decisión (Accept/RejectPairingAsync).
    /// </summary>
    event Action<LocalSharePairRequest>? PairRequested;

    /// <summary>Desenlace de un emparejamiento iniciado por este equipo.</summary>
    event Action<LocalSharePairFinished>? PairFinished;
}
