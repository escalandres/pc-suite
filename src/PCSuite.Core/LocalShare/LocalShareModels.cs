namespace PCSuite.Core.LocalShare;

/// <summary>
/// Modelos de dominio de Local Share (Task 016).
/// Puros, sin dependencias de infraestructura ni serialización.
/// Los DTOs del bridge viven en Contracts; estos son para la lógica.
/// </summary>

// ---------------------------------------------------------------------------
// Estados del servicio
// ---------------------------------------------------------------------------

/// <summary>Estado del servicio Local Share en esta máquina.</summary>
public enum LocalShareServiceStatus
{
    Stopped,
    Starting,
    Running,
    Error,
}

// ---------------------------------------------------------------------------
// Estados de transferencia
// ---------------------------------------------------------------------------

/// <summary>
/// Estados de una transferencia. El receptor y el emisor comparten estos
/// valores para que la UI muestre un estado coherente en ambos lados.
/// Transiciones válidas: PendingApproval → Approved → Transferring →
/// Verifying → Completed; en cualquier estado activo → Cancelled/Failed.
/// </summary>
public enum TransferStatus
{
    /// <summary>Solicitada, esperando aprobación del receptor.</summary>
    PendingApproval,

    /// <summary>El receptor aprobó, preparando archivos temporales.</summary>
    Approved,

    /// <summary>Transfiriendo contenido en streaming.</summary>
    Transferring,

    /// <summary>Contenido completo, computando hash final (normalmente instantáneo).</summary>
    Verifying,

    /// <summary>Envío pausado por el usuario (16.7.4): sin flujo hasta reanudar.</summary>
    Paused,

    /// <summary>Terminada con éxito, archivos publicados.</summary>
    Completed,

    /// <summary>Rechazada por el receptor.</summary>
    Rejected,

    /// <summary>Cancelada por cualquiera de los lados.</summary>
    Cancelled,

    /// <summary>Fallo (desconexión, hash incorrecto, disco, etc.).</summary>
    Failed,
}

// ---------------------------------------------------------------------------
// Estados de dispositivo
// ---------------------------------------------------------------------------

/// <summary>Estado de confianza de un dispositivo descubierto o emparejado.</summary>
public enum DeviceTrust
{
    /// <summary>Visto por mDNS o conexión manual, sin emparejar.</summary>
    Unknown,

    /// <summary>Emparejado y confiable (huella de certificado verificada).</summary>
    Paired,

    /// <summary>Confianza revocada explícitamente.</summary>
    Revoked,
}

// ---------------------------------------------------------------------------
// Dispositivo
// ---------------------------------------------------------------------------

/// <summary>
/// Un dispositivo en la red local que ejecuta PCSuite con Local Share.
/// El deviceId es un GUID generado por instalación (persistente).
/// </summary>
public sealed class SharedDevice
{
    public required string DeviceId { get; init; }

    /// <summary>Nombre visible del equipo (configurable en Ajustes).</summary>
    public required string DisplayName { get; set; }

    /// <summary>Direcciones IP vistas recientemente (mDNS puede anunciar varias).</summary>
    public IReadOnlyList<string> KnownAddresses { get; set; } = [];

    /// <summary>Puerto TCP del servicio de transferencia.</summary>
    public int Port { get; set; }

    public DeviceTrust Trust { get; set; } = DeviceTrust.Unknown;

    /// <summary>Huella SHA-256 del certificado TLS del par (hex minúscula).</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>Última vez que se vio (mDNS o conexión exitosa).</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    public bool IsPaired => Trust == DeviceTrust.Paired;
}

// ---------------------------------------------------------------------------
// Transferencia
// ---------------------------------------------------------------------------

/// <summary>
/// Una transferencia de archivos entre dos dispositivos. Cada transferencia
/// es un Job en el IJobManager (ciclo de vida, cancelación, progreso).
/// </summary>
public sealed class ShareTransfer
{
    public required string TransferId { get; init; }

    /// <summary>Envío o recepción (desde la perspectiva de ESTA máquina).</summary>
    public required TransferDirection Direction { get; init; }

    public required string PeerDeviceId { get; init; }

    public required string PeerDisplayName { get; init; }

    public TransferStatus Status { get; set; } = TransferStatus.PendingApproval;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Total de bytes a transferir (suma de todos los items).</summary>
    public long TotalBytes { get; set; }

    /// <summary>Bytes transferidos hasta el momento (progreso en vivo).</summary>
    public long TransferredBytes { get; set; }

    /// <summary>Número de archivos/carpetas en la transferencia.</summary>
    public int ItemCount { get; set; }

    /// <summary>Resumen legible del destino (solo receptor).</summary>
    public string? DestinationSummary { get; set; }

    /// <summary>Código de error tipado cuando Status es Failed.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Mensaje apto para la UI (sin stack traces).</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Dirección de una transferencia desde la perspectiva local.</summary>
public enum TransferDirection
{
    Sending,
    Receiving,
}

// ---------------------------------------------------------------------------
// Item de transferencia
// ---------------------------------------------------------------------------

/// <summary>
/// Un archivo o carpeta dentro de una transferencia. Las carpetas crean
/// items por cada archivo contenido, con la ruta relativa preservada.
/// </summary>
public sealed class ShareTransferItem
{
    public required string ItemId { get; init; }
    public required string TransferId { get; init; }

    /// <summary>Ruta relativa al root de la transferencia (validada: sin .., sin absolutas).</summary>
    public required string RelativePath { get; init; }

    /// <summary>Tamaño esperado en bytes (declarado por el emisor, verificado al recibir).</summary>
    public long ExpectedSize { get; init; }

    /// <summary>SHA-256 esperado (hex minúscula, computado por el emisor antes de enviar).</summary>
    public string? ExpectedHash { get; init; }

    /// <summary>Estado del item individual.</summary>
    public TransferStatus Status { get; set; } = TransferStatus.PendingApproval;

    /// <summary>Bytes transferidos de ESTE item.</summary>
    public long TransferredBytes { get; set; }

    /// <summary>Ruta absoluta local del archivo (solo el emisor la conoce; nunca viaja por la red).</summary>
    public string? LocalSourcePath { get; set; }

    /// <summary>Ruta absoluta del archivo temporal del receptor (asignada al aceptar).</summary>
    public string? TempPath { get; set; }

    /// <summary>Ruta final publicada del receptor (tras validar integridad).</summary>
    public string? FinalPath { get; set; }
}

// ---------------------------------------------------------------------------
// Configuración
// ---------------------------------------------------------------------------

/// <summary>
/// Configuración persistida del módulo Local Share.
/// Se guarda como JSON en PCSuitePaths.DataDirectory.
/// </summary>
public sealed class LocalShareSettings
{
    /// <summary>Nombre visible de este equipo en la red.</summary>
    public string DisplayName { get; set; } = Environment.MachineName;

    /// <summary>Carpeta de recepción de archivos.</summary>
    public string DownloadDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads", "PCSuite");

    /// <summary>
    /// Puerto TCP del listener (0 = auto-assign). Por defecto FUERA del rango
    /// efímero de Windows (49152-65535): un puerto dentro de ese rango puede
    /// colisionar con conexiones salientes aleatorias del sistema (socket en
    /// TIME_WAIT bloquea el bind y el servicio no arranca).
    /// </summary>
    public int Port { get; set; } = 45180;

    /// <summary>Descubrimiento mDNS activado.</summary>
    public bool DiscoveryEnabled { get; set; } = true;

    /// <summary>Aceptar automáticamente de dispositivos emparejados (false = siempre pedir confirmación).</summary>
    public bool AutoAcceptFromPaired { get; set; } = false;

    /// <summary>
    /// Transferencias entrantes simultáneas (1-10). Cada una retiene una
    /// conexión mientras espera la decisión del usuario, por lo que un valor
    /// bajo con solicitudes pendientes hace que el receptor responda "busy".
    /// </summary>
    public int MaxSimultaneousTransfers { get; set; } = 3;

    /// <summary>
    /// Límite de velocidad de ENVÍO en bytes/segundo (0 = sin límite). Se aplica
    /// regulando el ritmo de los chunks de 64KB; no afecta la recepción.
    /// </summary>
    public long MaxBandwidthBytesPerSecond { get; set; } = 0;

    /// <summary>Tamaño máximo por transferencia en bytes (0 = sin límite).</summary>
    public long MaxTransferSizeBytes { get; set; } = 0;

    /// <summary>DeviceId de ESTA instalación (GUID persistente).</summary>
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("D");

    /// <summary>Huella del certificado TLS propio (generado al primer arranque).</summary>
    public string? CertificateThumbprint { get; set; }
}

/// <summary>
/// Diagnóstico de firewall de Local Share (solo lectura, sin elevación).
/// RuleExists null = no se pudo determinar (netsh ausente o error).
/// </summary>
public sealed record LocalShareFirewallDiagnostic(bool? RuleExists, bool PortMatches)
{
    public static LocalShareFirewallDiagnostic Unknown { get; } = new(null, false);
}

// ---------------------------------------------------------------------------
// Emparejamiento (modelo Bluetooth: comparación numérica)
// ---------------------------------------------------------------------------

/// <summary>
/// Solicitud de emparejamiento entrante (lado receptor): el otro equipo
/// quiere emparejarse. La UI muestra el código para comparación visual.
/// </summary>
public sealed record LocalSharePairRequest(
    string RequestId,
    string PeerDeviceId,
    string PeerDisplayName,
    string Code,
    string Address,
    int Port);

/// <summary>
/// Desenlace de un emparejamiento iniciado por ESTE equipo (lado iniciador).
/// </summary>
public enum LocalSharePairOutcome
{
    /// <summary>El receptor aceptó: ambos equipos quedan emparejados.</summary>
    Accepted,

    /// <summary>El receptor rechazó o la solicitud expiró.</summary>
    Rejected,

    /// <summary>Fallo técnico (conexión, TLS, identidad no verificable).</summary>
    Failed,
}

/// <summary>
/// Respuesta inmediata al iniciar un emparejamiento: el código que AMBOS
/// equipos deben mostrar (comparación numérica estilo Bluetooth). El
/// desenlace llega después vía el evento PairFinished.
/// </summary>
public sealed record LocalSharePairInitiation(
    string RequestId,
    string Code,
    string PeerDisplayName);

/// <summary>
/// Notificación de desenlace de emparejamiento para el iniciador.
/// </summary>
public sealed record LocalSharePairFinished(
    string RequestId,
    LocalSharePairOutcome Outcome,
    string? PeerDeviceId = null,
    string? PeerDisplayName = null,
    string? ErrorMessage = null);

// ---------------------------------------------------------------------------
// Progreso
// ---------------------------------------------------------------------------

/// <summary>
/// Snapshot de progreso de una transferencia, para eventos hacia la UI.
/// </summary>
public sealed class TransferProgress
{
    public required string TransferId { get; init; }
    public required long TransferredBytes { get; init; }
    public required long TotalBytes { get; init; }
    public required int ItemsCompleted { get; init; }
    public required int ItemsTotal { get; init; }

    /// <summary>Bytes por segundo (ventana móvil de 2s, 0 si no hay datos).</summary>
    public long BytesPerSecond { get; init; }

    /// <summary>Archivo que se está transfiriendo en este momento.</summary>
    public string? CurrentFile { get; init; }
}
