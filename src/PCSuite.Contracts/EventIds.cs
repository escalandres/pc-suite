namespace PCSuite.Contracts;

public static class EventIds
{
    public const string AppReady = "app.ready";

    /// <summary>Progreso de actualización de definiciones ClamAV (freshclam).</summary>
    public const string ClamAvFreshclamProgress = "clamav.freshclam.progress";

    /// <summary>Progreso de escaneo ClamAV (clamscan).</summary>
    public const string ClamAvScanProgress = "clamav.scan.progress";

    /// <summary>Ciclo de vida de jobs: estado, progreso y resultado.</summary>
    public const string JobEvent = "job.event";

    // Eventos Local Share (Task 016)

    /// <summary>Un dispositivo fue descubierto, emparejado o revocado.</summary>
    public const string LocalShareDeviceChanged = "localshare.device-changed";

    /// <summary>Llega una solicitud de transferencia que el usuario debe aceptar/rechazar.</summary>
    public const string LocalShareIncomingRequest = "localshare.incoming-request";

    /// <summary>Progreso de una transferencia en curso (throttled ~4/s).</summary>
    public const string LocalShareTransferProgress = "localshare.transfer-progress";

    /// <summary>Una transferencia llegó a estado terminal (completed/cancelled/failed).</summary>
    public const string LocalShareTransferFinished = "localshare.transfer-finished";

    /// <summary>
    /// Llega una solicitud de emparejamiento (lado receptor): mostrar el código
    /// de comparación y decidir (localshare.pairAccept / pairReject).
    /// </summary>
    public const string LocalSharePairRequested = "localshare.pair-request";

    /// <summary>Desenlace de un emparejamiento iniciado por este equipo (accepted/rejected/failed).</summary>
    public const string LocalSharePairFinished = "localshare.pair-finished";
}
