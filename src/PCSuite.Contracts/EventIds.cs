namespace PCSuite.Contracts;

public static class EventIds
{
    public const string AppReady = "app.ready";

    /// <summary>
    /// Task 026.3: el host NATIVO capturó un drop de archivos del SO con sus
    /// RUTAS absolutas (el WebView no las entrega a la página — limitación de
    /// Chromium). Las páginas que quieran hacer algo con ellos se suscriben.
    /// </summary>
    public const string SystemFilesDropped = "system.filesDropped";

    /// <summary>
    /// Task 026.4: el cursor arrastra archivos sobre PCSuite (estado + posición
    /// del drag en vivo, capturados por el IDropTarget del host — la página no
    /// ve los eventos de drag del SO). Lo usa la UI para el feedback visual de
    /// las zonas de drop.
    /// </summary>
    public const string SystemDragHover = "system.dragHover";

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
