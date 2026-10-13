using System.Text.Json;

namespace PCSuite.Contracts;

// DTOs del bridge para localshare.* (Task 016).
// El mapeo enum → string vive en las operaciones, no en BridgeJson.

/// <summary>Resultado de localshare.status.</summary>
public sealed class LocalShareStatusResult
{
    public string Status { get; init; } = "stopped";

    public int Port { get; init; }

    public string? DisplayName { get; init; }

    public bool DiscoveryEnabled { get; init; }

    public string? DownloadDirectory { get; init; }

    public string? Error { get; init; }
}

/// <summary>Un dispositivo visible en la red (resultado de localshare.devices).</summary>
public sealed class SharedDeviceResult
{
    public string DeviceId { get; init; } = "";

    public string DisplayName { get; init; } = "";

    /// <summary>unknown | paired | revoked.</summary>
    public string Trust { get; init; } = "unknown";

    /// <summary>Primera IP conocida (para display, la conexión usa la del emparejamiento).</summary>
    public string? Address { get; init; }

    public int Port { get; init; }

    /// <summary>
    /// ¿Se anunció en los últimos 45 s? (Los anuncios van cada 15 s: dos
    /// perdidos seguidos pueden ser ruido; tres, apagado.) Señal heurística
    /// para la UI — la verdad para transferir es la conexión TCP.
    /// </summary>
    public bool Online { get; init; }

    public DateTimeOffset? LastSeenAt { get; init; }
}

/// <summary>Resultado de localshare.devices.</summary>
public sealed class SharedDeviceCollectionResult
{
    public IReadOnlyList<SharedDeviceResult> Devices { get; init; } = [];
}

/// <summary>Resultado de localshare.send (payload: { deviceId, paths }).</summary>
public sealed class LocalShareSendResult
{
    public string TransferId { get; init; } = "";
}

/// <summary>Una transferencia con sus items (resultado de localshare.transfers / transfer-detail).</summary>
public sealed class ShareTransferResult
{
    public string TransferId { get; init; } = "";

    /// <summary>sending | receiving.</summary>
    public string Direction { get; init; } = "sending";

    public string PeerDisplayName { get; init; } = "";

    /// <summary>pendingApproval | approved | transferring | verifying | completed | rejected | cancelled | failed.</summary>
    public string Status { get; init; } = "pendingApproval";

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public long TotalBytes { get; init; }

    public long TransferredBytes { get; init; }

    public int ItemCount { get; init; }

    public string? DestinationSummary { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<ShareTransferItemResult> Items { get; init; } = [];
}

/// <summary>Un item de transferencia expuesto al frontend.</summary>
public sealed class ShareTransferItemResult
{
    public string ItemId { get; init; } = "";

    public string RelativePath { get; init; } = "";

    public long ExpectedSize { get; init; }

    public string? ExpectedHash { get; init; }

    public string Status { get; init; } = "pendingApproval";

    public long TransferredBytes { get; init; }
}

/// <summary>Resultado de localshare.transfers.</summary>
public sealed class ShareTransferCollectionResult
{
    public IReadOnlyList<ShareTransferResult> Transfers { get; init; } = [];
}

/// <summary>Configuración del módulo (resultado de localshare.config con payload null).</summary>
public sealed class LocalShareConfigResult
{
    public string DisplayName { get; init; } = "";

    public string DownloadDirectory { get; init; } = "";

    public int Port { get; init; }

    public bool DiscoveryEnabled { get; init; }

    public bool AutoAcceptFromPaired { get; init; }

    public int MaxSimultaneousTransfers { get; init; }

    public long MaxBandwidthBytesPerSecond { get; init; }

    public long MaxTransferSizeBytes { get; init; }
}

/// <summary>Payload de localshare.config para actualizar (PATCH-style: campos opcionales).</summary>
public sealed class LocalShareConfigPayload
{
    public string? DisplayName { get; init; }

    public string? DownloadDirectory { get; init; }

    public int? Port { get; init; }

    public bool? DiscoveryEnabled { get; init; }

    public bool? AutoAcceptFromPaired { get; init; }

    public int? MaxSimultaneousTransfers { get; init; }

    public long? MaxBandwidthBytesPerSecond { get; init; }
}

/// <summary>Payload de localshare.send.</summary>
public sealed class LocalShareSendPayload
{
    public string DeviceId { get; init; } = "";

    /// <summary>Rutas absolutas locales de archivos y/o carpetas a enviar.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];
}

/// <summary>Payload de localshare.pair: iniciar emparejamiento con un dispositivo descubierto.</summary>
public sealed class LocalSharePairPayload
{
    public string DeviceId { get; init; } = "";
}

/// <summary>Payload de localshare.pairAccept / localshare.pairReject.</summary>
public sealed class LocalSharePairDecisionPayload
{
    public string RequestId { get; init; } = "";
}

/// <summary>Resultado de localshare.pair: el código ya está disponible.</summary>
public sealed class LocalSharePairInitiationResult
{
    /// <summary>Identificador de este intento (correlaciona con el evento pair-finished).</summary>
    public string RequestId { get; init; } = "";

    /// <summary>Código de comparación: DEBE coincidir con el del otro equipo.</summary>
    public string Code { get; init; } = "";

    public string PeerDisplayName { get; init; } = "";
}

/// <summary>Payload del evento localshare.pair-request (lado receptor).</summary>
public sealed class LocalSharePairRequestedPayload
{
    public string RequestId { get; init; } = "";

    public string PeerDeviceId { get; init; } = "";

    public string PeerDisplayName { get; init; } = "";

    /// <summary>Código derivado de las dos huellas TLS vivas: el usuario lo compara con la pantalla del otro equipo.</summary>
    public string Code { get; init; } = "";
}

/// <summary>Payload del evento localshare.pair-finished (lado iniciador).</summary>
public sealed class LocalSharePairFinishedPayload
{
    public string RequestId { get; init; } = "";

    /// <summary>accepted | rejected | failed.</summary>
    public string Outcome { get; init; } = "";

    public string? DeviceId { get; init; }

    public string? DisplayName { get; init; }

    public string? ErrorMessage { get; init; }
}

/// <summary>Payload genérico de localshare.accept / localshare.reject / localshare.cancel.</summary>
public sealed class LocalShareTransferPayload
{
    public string TransferId { get; init; } = "";
}

/// <summary>Payload de localshare.revoke.</summary>
public sealed class LocalShareRevokePayload
{
    public string DeviceId { get; init; } = "";
}

/// <summary>Payload del evento localshare.device-changed.</summary>
public sealed class LocalShareDeviceChangedPayload
{
    public string DeviceId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Trust { get; init; } = "unknown";
    public string? Address { get; init; }
    public int Port { get; init; }
}

/// <summary>Payload del evento localshare.incoming-request.</summary>
public sealed class LocalShareIncomingRequestPayload
{
    public string TransferId { get; init; } = "";
    public string PeerDisplayName { get; init; } = "";
    public int ItemCount { get; init; }
    public long TotalBytes { get; init; }
    public IReadOnlyList<ShareTransferItemResult> Items { get; init; } = [];
}

/// <summary>Payload del evento localshare.transfer-progress.</summary>
public sealed class LocalShareTransferProgressPayload
{
    public string TransferId { get; init; } = "";
    public long TransferredBytes { get; init; }
    public long TotalBytes { get; init; }
    public long BytesPerSecond { get; init; }
    public int ItemsCompleted { get; init; }
    public int ItemsTotal { get; init; }
    public string? CurrentFile { get; init; }
}

/// <summary>Payload del evento localshare.transfer-finished.</summary>
public sealed class LocalShareTransferFinishedPayload
{
    public string TransferId { get; init; } = "";
    public string Status { get; init; } = "";
    public string? ErrorMessage { get; init; }
    public long TotalBytes { get; init; }
}

/// <summary>Resultado de localshare.firewallStatus (solo lectura).</summary>
public sealed class LocalShareFirewallStatusResult
{
    /// <summary>null = no se pudo determinar (netsh ausente o error).</summary>
    public bool? RuleExists { get; init; }

    /// <summary>La regla TCP existente cubre el puerto actual del servicio.</summary>
    public bool PortMatches { get; init; }

    /// <summary>La regla UDP de detección (multicast 5354) existe.</summary>
    public bool DiscoveryPortMatches { get; init; }

    /// <summary>Puerto TCP del servicio en este momento.</summary>
    public int Port { get; init; }
}

/// <summary>Resultado de localshare.firewallRule (tras UAC puntual).</summary>
public sealed class LocalShareFirewallRuleResult
{
    public bool Created { get; init; }
    public string Detail { get; init; } = "";
}
