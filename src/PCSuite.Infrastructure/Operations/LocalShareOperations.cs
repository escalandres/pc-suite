using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.LocalShare;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

// Operaciones localshare.* del puente: traducen el ILocalShareService
// a DTOs camelCase. El mapeo enum → string vive aquí.

/// <summary>localshare.status — estado del servicio (payload cero).</summary>
public sealed class LocalShareStatusOperation(ILocalShareService service) : IOperation
{
    public string Id => OperationIds.LocalShareStatus;
    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        var s = service;
        var dto = new LocalShareStatusResult
        {
            Status = s.Status.ToString().ToLowerInvariant(),
            Port = s.EffectivePort,
            DisplayName = s.Settings.DisplayName,
            DiscoveryEnabled = s.Settings.DiscoveryEnabled,
            DownloadDirectory = s.Settings.DownloadDirectory,
        };
        return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(dto)));
    }
}

/// <summary>localshare.start — inicia el listener (payload cero).</summary>
public sealed class LocalShareStartOperation(ILocalShareService service) : IOperation
{
    public string Id => OperationIds.LocalShareStart;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        await service.StartAsync(ct).ConfigureAwait(false);
        return OperationResult.Ok(BridgeJson.ToJson(new { started = true, port = service.EffectivePort }));
    }
}

/// <summary>localshare.stop — detiene el listener (payload cero).</summary>
public sealed class LocalShareStopOperation(ILocalShareService service) : IOperation
{
    public string Id => OperationIds.LocalShareStop;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        await service.StopAsync(ct).ConfigureAwait(false);
        return OperationResult.Ok(BridgeJson.ToJson(new { stopped = true }));
    }
}

/// <summary>localshare.devices — lista dispositivos (payload cero).</summary>
public sealed class LocalShareDevicesOperation(ILocalShareService service) : IOperation
{
    public string Id => OperationIds.LocalShareDevices;
    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        var dto = new SharedDeviceCollectionResult
        {
            Devices = [.. service.GetDevices().Select(ToDeviceDto)],
        };
        return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(dto)));
    }

    internal static SharedDeviceResult ToDeviceDto(SharedDevice device) => new()
    {
        DeviceId = device.DeviceId,
        DisplayName = device.DisplayName,
        Trust = device.Trust.ToString().ToLowerInvariant(),
        Address = device.KnownAddresses.FirstOrDefault(),
        Port = device.Port,
        LastSeenAt = device.LastSeenAt,
    };
}

/// <summary>localshare.pair — emparejar por IP:puerto (payload: { address, port }).</summary>
public sealed class LocalSharePairOperation(ILocalShareService service) : IOperation
{
    public string Id => OperationIds.LocalSharePair;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out LocalSharePairPayload? payload) || payload is null)
        {
            return OperationResult.Fail("error", "El payload debe ser { address, port }.");
        }

        try
        {
            var device = await service.PairAsync(payload.Address, payload.Port, ct).ConfigureAwait(false);
            var dto = new LocalSharePairResult
            {
                DeviceId = device.DeviceId,
                DisplayName = device.DisplayName,
                Pin = "000000", // TODO: generar PIN real
                PendingConfirmation = true,
            };
            return OperationResult.Ok(BridgeJson.ToJson(dto));
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("pair_failed", ex.Message);
        }
    }
}

/// <summary>localshare.send — enviar archivos (payload: { deviceId, paths }).</summary>
public sealed class LocalShareSendOperation(ILocalShareService service) : IOperation
{
    public string Id => OperationIds.LocalShareSend;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out LocalShareSendPayload? payload) || payload is null)
        {
            return OperationResult.Fail("error", "El payload debe ser { deviceId, paths }.");
        }

        try
        {
            var transfer = await service.SendAsync(payload.DeviceId, payload.Paths, ct).ConfigureAwait(false);
            return OperationResult.Ok(BridgeJson.ToJson(new LocalShareSendResult { TransferId = transfer.TransferId }));
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail("send_failed", ex.Message);
        }
    }
}

/// <summary>localshare.accept — aceptar solicitud entrante (payload: { transferId }).</summary>
public sealed class LocalShareAcceptOperation(ILocalShareService service) : IOperation
{
    public string Id => OperationIds.LocalShareAccept;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out LocalShareTransferPayload? payload) || payload is null)
        {
            return OperationResult.Fail("error", "El payload debe ser { transferId }.");
        }

        await service.AcceptAsync(payload.TransferId, ct).ConfigureAwait(false);
        return OperationResult.Ok(BridgeJson.ToJson(new { accepted = true }));
    }
}

/// <summary>localshare.reject — rechazar solicitud entrante (payload: { transferId }).</summary>
public sealed class LocalShareRejectOperation(ILocalShareService service) : IOperation
{
    public string Id => OperationIds.LocalShareReject;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out LocalShareTransferPayload? payload) || payload is null)
        {
            return OperationResult.Fail("error", "El payload debe ser { transferId }.");
        }

        await service.RejectAsync(payload.TransferId, ct).ConfigureAwait(false);
        return OperationResult.Ok(BridgeJson.ToJson(new { rejected = true }));
    }
}

/// <summary>localshare.transfers — listar transferencias (payload cero).</summary>
public sealed class LocalShareTransfersOperation(ILocalShareService service) : IOperation
{
    public string Id => OperationIds.LocalShareTransfers;
    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        var dto = new ShareTransferCollectionResult
        {
            Transfers = [.. service.GetTransfers().Select(t => ToTransferDto(t))],
        };
        return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(dto)));
    }

    internal static ShareTransferResult ToTransferDto(ShareTransfer t, IReadOnlyList<ShareTransferItem>? items = null) => new()
    {
        TransferId = t.TransferId,
        Direction = t.Direction.ToString().ToLowerInvariant(),
        PeerDisplayName = t.PeerDisplayName,
        Status = t.Status.ToString().ToLowerInvariant(),
        CreatedAt = t.CreatedAt,
        StartedAt = t.StartedAt,
        CompletedAt = t.CompletedAt,
        TotalBytes = t.TotalBytes,
        TransferredBytes = t.TransferredBytes,
        ItemCount = t.ItemCount,
        DestinationSummary = t.DestinationSummary,
        ErrorCode = t.ErrorCode,
        ErrorMessage = t.ErrorMessage,
        Items = items is not null ? [.. items.Select(i => ToItemDto(i))] : [],
    };

    private static ShareTransferItemResult ToItemDto(ShareTransferItem i) => new()
    {
        ItemId = i.ItemId,
        RelativePath = i.RelativePath,
        ExpectedSize = i.ExpectedSize,
        ExpectedHash = i.ExpectedHash,
        Status = i.Status.ToString().ToLowerInvariant(),
        TransferredBytes = i.TransferredBytes,
    };
}

/// <summary>localshare.cancelTransfer — cancelar (payload: { transferId }).</summary>
public sealed class LocalShareCancelTransferOperation(ILocalShareService service) : IOperation
{
    public string Id => OperationIds.LocalShareCancelTransfer;
    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out LocalShareTransferPayload? payload) || payload is null)
        {
            return Task.FromResult(OperationResult.Fail("error", "El payload debe ser { transferId }."));
        }

        var cancelled = service.CancelTransfer(payload.TransferId);
        return Task.FromResult(
            OperationResult.Ok(BridgeJson.ToJson(new { cancelled })));
    }
}

/// <summary>localshare.config — leer o actualizar configuración.</summary>
public sealed class LocalShareConfigOperation(ILocalShareService service) : IOperation
{
    public string Id => OperationIds.LocalShareConfig;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        // Payload nulo o vacío → leer; payload con campos → actualizar
        if (context.Payload is null || context.Payload.Value.ValueKind == JsonValueKind.Null
            || (context.Payload.Value.ValueKind == JsonValueKind.Object
                && context.Payload.Value.GetRawText() == "{}"))
        {
            var s = service.Settings;
            var dto = new LocalShareConfigResult
            {
                DisplayName = s.DisplayName,
                DownloadDirectory = s.DownloadDirectory,
                Port = s.Port,
                DiscoveryEnabled = s.DiscoveryEnabled,
                AutoAcceptFromPaired = s.AutoAcceptFromPaired,
                MaxTransferSizeBytes = s.MaxTransferSizeBytes,
            };
            return OperationResult.Ok(BridgeJson.ToJson(dto));
        }

        if (!ToolMapper.TryParsePayload(context.Payload, out LocalShareConfigPayload? payload) || payload is null)
        {
            return OperationResult.Fail("error", "Payload de configuración inválido.");
        }

        var current = service.Settings;
        if (payload.DisplayName is not null) current.DisplayName = payload.DisplayName;
        if (payload.DownloadDirectory is not null) current.DownloadDirectory = payload.DownloadDirectory;
        if (payload.Port is { } port) current.Port = port;
        if (payload.DiscoveryEnabled is { } discovery) current.DiscoveryEnabled = discovery;
        if (payload.AutoAcceptFromPaired is { } auto) current.AutoAcceptFromPaired = auto;

        await service.UpdateSettingsAsync(current, ct).ConfigureAwait(false);
        return OperationResult.Ok(BridgeJson.ToJson(new { updated = true }));
    }
}

/// <summary>localshare.clearHistory — limpiar historial (payload cero).</summary>
public sealed class LocalShareClearHistoryOperation(ILocalShareService service) : IOperation
{
    public string Id => OperationIds.LocalShareClearHistory;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        await service.ClearHistoryAsync(ct).ConfigureAwait(false);
        return OperationResult.Ok(BridgeJson.ToJson(new { cleared = true }));
    }
}

/// <summary>localshare.revoke — revocar confianza (payload: { deviceId }).</summary>
public sealed class LocalShareRevokeOperation(ILocalShareService service) : IOperation
{
    public string Id => OperationIds.LocalShareRevoke;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out LocalShareRevokePayload? payload) || payload is null)
        {
            return OperationResult.Fail("error", "El payload debe ser { deviceId }.");
        }

        await service.RevokeTrustAsync(payload.DeviceId, ct).ConfigureAwait(false);
        return OperationResult.Ok(BridgeJson.ToJson(new { revoked = true }));
    }
}
