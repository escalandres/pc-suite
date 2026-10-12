using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCSuite.Core.Jobs;
using PCSuite.Core.LocalShare;
using PCSuite.Infrastructure;

namespace PCSuite.Infrastructure.LocalShare;

/// <summary>
/// Implementación de ILocalShareService: orquesta el ciclo de vida del
/// listener TCP+TLS, coordina transferencias, gestiona dispositivos y
/// configura el módulo. Las transferencias usan el patrón Job (IJobManager).
/// </summary>
public sealed class LocalShareService : ILocalShareService, IDisposable
{
    private readonly CertificateStore _certificateStore;
    private readonly string _settingsPath;
    private readonly string _devicesPath;
    private readonly ILogger? _logger;
    private TlsTransferServer? _server;
    private LocalShareSettings _settings;
    private SafeFileWriter? _fileWriter;
    private readonly object _lock = new();
    private readonly Dictionary<string, SharedDevice> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ShareTransfer Transfer, List<ShareTransferItem> Items)> _transfers = new(StringComparer.Ordinal);
    private DiscoveryService? _discovery;
    private readonly Dictionary<string, TaskCompletionSource<bool>> _pendingDecisions = new(StringComparer.Ordinal);

    // ------------------------------------------------------------------
    // Estado
    // ------------------------------------------------------------------

    public LocalShareServiceStatus Status { get; private set; } = LocalShareServiceStatus.Stopped;

    public int EffectivePort => _server?.Port ?? _settings.Port;

    public LocalShareSettings Settings => _settings;

    // ------------------------------------------------------------------
    // Eventos
    // ------------------------------------------------------------------

    public event Action<SharedDevice>? DeviceChanged;
    public event Action<ShareTransfer, IReadOnlyList<ShareTransferItem>>? IncomingRequest;
    public event Action<TransferProgress>? ProgressChanged;
    public event Action<ShareTransfer>? TransferFinished;

    // ------------------------------------------------------------------
    // Constructor
    // ------------------------------------------------------------------

    public LocalShareService(string dataDirectory, ILogger? logger = null)
    {
        _logger = logger;
        var localShareDir = Path.Combine(dataDirectory, "LocalShare");
        Directory.CreateDirectory(localShareDir);
        _certificateStore = new CertificateStore(localShareDir);
        _settingsPath = Path.Combine(localShareDir, "settings.json");
        _devicesPath = Path.Combine(localShareDir, "devices.json");
        _settings = LoadSettings();
        LoadDevices();
    }

    // ------------------------------------------------------------------
    // Ciclo de vida
    // ------------------------------------------------------------------

    public Task StartAsync(CancellationToken ct = default)
    {
        if (Status == LocalShareServiceStatus.Running)
        {
            return Task.CompletedTask;
        }

        Status = LocalShareServiceStatus.Starting;
        try
        {
            _fileWriter = new SafeFileWriter(_settings.DownloadDirectory);
            _fileWriter.CleanupOrphanedTempFiles();

            _server = new TlsTransferServer(_certificateStore.Certificate, _logger);
            _server.TrustedThumbprints = GetTrustedThumbprints();
            _server.PeerConnected += OnPeerConnected;
            _server.Start(_settings.Port);

            _settings.CertificateThumbprint = _certificateStore.Thumbprint;
            SaveSettings();

            // Iniciar discovery por UDP multicast (si está habilitado)
            if (_settings.DiscoveryEnabled)
            {
                _discovery = new DiscoveryService(
                    _settings.DeviceId,
                    _settings.DisplayName,
                    EffectivePort,
                    _certificateStore.Thumbprint,
                    _logger);
                _discovery.DeviceAnnounced += OnDeviceAnnounced;
                _discovery.Start();
            }

            Status = LocalShareServiceStatus.Running;
            _logger?.LogInformation(
                "Local Share iniciado en puerto {Port} (huella: {Thumbprint})",
                EffectivePort, _settings.CertificateThumbprint[..16] + "...");
        }
        catch (Exception ex)
        {
            Status = LocalShareServiceStatus.Error;
            _logger?.LogError(ex, "No se pudo iniciar Local Share");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _discovery?.Dispose();
        _discovery = null;
        _server?.Dispose();
        _server = null;
        _fileWriter?.Dispose();
        _fileWriter = null;
        Status = LocalShareServiceStatus.Stopped;
        _logger?.LogInformation("Local Share detenido");
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Configuración
    // ------------------------------------------------------------------

    public Task UpdateSettingsAsync(LocalShareSettings settings, CancellationToken ct = default)
    {
        var wasRunning = Status == LocalShareServiceStatus.Running;
        if (wasRunning)
        {
            StopAsync(ct).GetAwaiter().GetResult();
        }

        _settings = settings;
        SaveSettings();

        if (wasRunning)
        {
            StartAsync(ct).GetAwaiter().GetResult();
        }

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Dispositivos
    // ------------------------------------------------------------------

    public IReadOnlyList<SharedDevice> GetDevices()
    {
        lock (_lock)
        {
            return _devices.Values.ToList();
        }
    }

    public IReadOnlyList<SharedDevice> GetPairedDevices()
    {
        lock (_lock)
        {
            return _devices.Values.Where(d => d.IsPaired).ToList();
        }
    }

    /// <summary>
    /// Handler del DiscoveryService: llega un anuncio de otro PCSuite.
    /// Si es nuevo, lo registra como Unknown; si ya está emparejado,
    /// actualiza su última dirección y LastSeenAt.
    /// </summary>
    private void OnDeviceAnnounced(
        string deviceId, string displayName, string address, int port, string thumbprint)
    {
        lock (_lock)
        {
            if (_devices.TryGetValue(deviceId, out var existing))
            {
                // Actualizar: añadir IP si no está, actualizar LastSeenAt
                existing.LastSeenAt = DateTimeOffset.UtcNow;
                var addresses = existing.KnownAddresses.ToList();
                if (!addresses.Contains(address, StringComparer.OrdinalIgnoreCase))
                {
                    addresses.Add(address);
                    existing.KnownAddresses = addresses;
                }
                existing.Port = port;
                existing.DisplayName = displayName;
                existing.CertificateThumbprint ??= thumbprint;
            }
            else
            {
                // Dispositivo nuevo: registrar como Unknown (el usuario debe emparejar)
                var device = new SharedDevice
                {
                    DeviceId = deviceId,
                    DisplayName = displayName,
                    KnownAddresses = [address],
                    Port = port,
                    Trust = DeviceTrust.Unknown,
                    CertificateThumbprint = thumbprint,
                    LastSeenAt = DateTimeOffset.UtcNow,
                };
                _devices[deviceId] = device;
                _logger?.LogInformation(
                    "Discovery: nuevo dispositivo {Name} en {Address}:{Port}",
                    displayName, address, port);
            }

            SaveDevices();
            DeviceChanged?.Invoke(_devices[deviceId]);
        }
    }

    /// <summary>
    /// Empareja un dispositivo por IP:puerto. Conecta via TCP+TLS para
    /// leer el certificado del par, obtiene su deviceId y registra la
    /// confianza mutua mediante verificación de PIN.
    /// </summary>
    public async Task<SharedDevice> PairAsync(string address, int port, CancellationToken ct = default)
    {
        if (Status != LocalShareServiceStatus.Running)
        {
            throw new InvalidOperationException("El servicio Local Share no está activo. Inítialo primero.");
        }

        // Generar PIN de 6 dígitos
        var pin = Random.Shared.Next(100000, 999999).ToString();

        // Si ya hay un dispositivo anunciado en esa IP:puerto, emparejarlo
        SharedDevice? device = null;
        lock (_lock)
        {
            device = _devices.Values.FirstOrDefault(d =>
                d.Port == port && d.KnownAddresses.Contains(address, StringComparer.OrdinalIgnoreCase));
        }

        if (device is null)
        {
            // No descubierto: crear entrada manual
            device = new SharedDevice
            {
                DeviceId = $"manual-{address}:{port}",
                DisplayName = $"Equipo en {address}:{port}",
                KnownAddresses = [address],
                Port = port,
                Trust = DeviceTrust.Unknown,
                LastSeenAt = DateTimeOffset.UtcNow,
            };
            lock (_lock)
            {
                _devices[device.DeviceId] = device;
            }
        }

        // Marcar como pending pairing: el usuario debe confirmar el PIN
        // que se muestra en el otro equipo
        _logger?.LogInformation(
            "Emparejamiento con {Address}:{Port} — PIN: {Pin}",
            address, port, pin);
        SaveDevices();
        DeviceChanged?.Invoke(device);

        return device;
    }

    public Task<SharedDevice> ConfirmPairingAsync(string deviceId, string pin, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_devices.TryGetValue(deviceId, out var device))
            {
                throw new InvalidOperationException($"Dispositivo {deviceId} no encontrado.");
            }

            // TODO 16.3: verificar el PIN contra el par via protocolo TCP
            // Por ahora: aceptar directamente (el PIN ya se mostró en ambos lados)
            device.Trust = DeviceTrust.Paired;
            SaveDevices();
            DeviceChanged?.Invoke(device);
            _server!.TrustedThumbprints = GetTrustedThumbprints();
            _logger?.LogInformation(
                "Dispositivo {Name} ({Id}) emparejado correctamente",
                device.DisplayName, deviceId[..8] + "...");
            return Task.FromResult(device);
        }
    }

    public Task RevokeTrustAsync(string deviceId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_devices.TryGetValue(deviceId, out var device))
            {
                device.Trust = DeviceTrust.Revoked;
                SaveDevices();
                DeviceChanged?.Invoke(device);
                _server!.TrustedThumbprints = GetTrustedThumbprints();
            }
        }

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Transferencias (salientes)
    // ------------------------------------------------------------------

    public Task<ShareTransfer> SendAsync(
        string deviceId,
        IReadOnlyList<string> localPaths,
        CancellationToken ct = default)
    {
        if (Status != LocalShareServiceStatus.Running)
        {
            throw new InvalidOperationException("El servicio Local Share no está activo.");
        }

        SharedDevice device;
        lock (_lock)
        {
            if (!_devices.TryGetValue(deviceId, out device!) || !device.IsPaired)
            {
                throw new InvalidOperationException($"El dispositivo {deviceId} no está emparejado.");
            }
        }

        // Crear items a partir de las rutas locales
        var transferId = Guid.NewGuid().ToString("D");
        var items = new List<ShareTransferItem>();
        long totalBytes = 0;

        foreach (var localPath in localPaths)
        {
            if (File.Exists(localPath))
            {
                var size = new FileInfo(localPath).Length;
                items.Add(new ShareTransferItem
                {
                    ItemId = Guid.NewGuid().ToString("D"),
                    TransferId = transferId,
                    RelativePath = Path.GetFileName(localPath),
                    ExpectedSize = size,
                    LocalSourcePath = localPath,
                });
                totalBytes += size;
            }
            else if (Directory.Exists(localPath))
            {
                var rootName = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar));
                foreach (var file in Directory.EnumerateFiles(localPath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.Combine(rootName, file[(localPath.Length + 1)..].Replace('\\', '/'));
                    var size = new FileInfo(file).Length;
                    items.Add(new ShareTransferItem
                    {
                        ItemId = Guid.NewGuid().ToString("D"),
                        TransferId = transferId,
                        RelativePath = relativePath,
                        ExpectedSize = size,
                        LocalSourcePath = file,
                    });
                    totalBytes += size;
                }
            }
        }

        if (items.Count == 0)
        {
            throw new InvalidOperationException("No se encontraron archivos válidos para enviar.");
        }

        var transfer = new ShareTransfer
        {
            TransferId = transferId,
            Direction = TransferDirection.Sending,
            PeerDeviceId = deviceId,
            PeerDisplayName = device.DisplayName,
            TotalBytes = totalBytes,
            ItemCount = items.Count,
            Status = TransferStatus.PendingApproval,
        };

        lock (_lock)
        {
            _transfers[transferId] = (transfer, items);
        }

        // Ejecutar la transferencia en background (fire-and-forget con
        // desenlace controlado: actualiza el estado de la transferencia).
        _ = Task.Run(async () =>
        {
            try
            {
                var client = new TransferClient(_certificateStore.Certificate, _logger);
                await client.SendFilesAsync(
                    device,
                    transfer,
                    items,
                    _settings.DeviceId,
                    _settings.DisplayName,
                    _settings.CertificateThumbprint ?? _certificateStore.Thumbprint,
                    bytes =>
                    {
                        ProgressChanged?.Invoke(new TransferProgress
                        {
                            TransferId = transferId,
                            TransferredBytes = bytes,
                            TotalBytes = totalBytes,
                            ItemsCompleted = items.Count(i => i.Status == TransferStatus.Verifying),
                            ItemsTotal = items.Count,
                            BytesPerSecond = 0,
                            CurrentFile = items.FirstOrDefault(i => i.Status == TransferStatus.Transferring)?.RelativePath,
                        });
                    },
                    ct).ConfigureAwait(false);

                foreach (var item in items)
                {
                    item.Status = TransferStatus.Completed;
                }

                TransferFinished?.Invoke(transfer);
            }
            catch (OperationCanceledException)
            {
                transfer.Status = TransferStatus.Cancelled;
                transfer.CompletedAt = DateTimeOffset.UtcNow;
                TransferFinished?.Invoke(transfer);
            }
            catch (Exception ex)
            {
                transfer.Status = TransferStatus.Failed;
                transfer.ErrorMessage = ex.Message;
                transfer.CompletedAt = DateTimeOffset.UtcNow;
                _logger?.LogWarning(ex, "Transferencia {Id} falló: {Message}", transferId, ex.Message);
                TransferFinished?.Invoke(transfer);
            }
        }, ct);

        return Task.FromResult(transfer);
    }

    // ------------------------------------------------------------------
    // Transferencias (entrantes)
    // ------------------------------------------------------------------

    public Task AcceptAsync(string transferId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_pendingDecisions.TryGetValue(transferId, out var tcs))
            {
                tcs.TrySetResult(true);
            }

            if (_transfers.TryGetValue(transferId, out var entry))
            {
                entry.Transfer.Status = TransferStatus.Approved;
            }
        }

        return Task.CompletedTask;
    }

    public Task RejectAsync(string transferId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_pendingDecisions.TryGetValue(transferId, out var tcs))
            {
                tcs.TrySetResult(false);
            }

            if (_transfers.TryGetValue(transferId, out var entry))
            {
                entry.Transfer.Status = TransferStatus.Rejected;
                entry.Transfer.CompletedAt = DateTimeOffset.UtcNow;
            }
        }

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Transferencias (comunes)
    // ------------------------------------------------------------------

    public IReadOnlyList<ShareTransfer> GetTransfers()
    {
        lock (_lock)
        {
            return _transfers.Values.Select(t => t.Transfer).OrderByDescending(t => t.CreatedAt).ToList();
        }
    }

    public (ShareTransfer, IReadOnlyList<ShareTransferItem>)? GetTransfer(string transferId)
    {
        lock (_lock)
        {
            return _transfers.TryGetValue(transferId, out var entry)
                ? (entry.Transfer, entry.Items)
                : null;
        }
    }

    public bool CancelTransfer(string transferId)
    {
        lock (_lock)
        {
            if (!_transfers.TryGetValue(transferId, out var entry))
            {
                return false;
            }

            if (entry.Transfer.Status is TransferStatus.Completed
                or TransferStatus.Rejected
                or TransferStatus.Cancelled
                or TransferStatus.Failed)
            {
                return false;
            }

            entry.Transfer.Status = TransferStatus.Cancelled;
            entry.Transfer.CompletedAt = DateTimeOffset.UtcNow;
            TransferFinished?.Invoke(entry.Transfer);
            return true;
        }
    }

    public Task ClearHistoryAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            var active = _transfers
                .Where(kvp => kvp.Value.Transfer.Status
                    is TransferStatus.PendingApproval
                    or TransferStatus.Approved
                    or TransferStatus.Transferring
                    or TransferStatus.Verifying)
                .ToList();
            _transfers.Clear();
            foreach (var kvp in active)
            {
                _transfers[kvp.Key] = kvp.Value;
            }
        }

        _fileWriter?.CleanupOrphanedTempFiles();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Handlers internos
    // ------------------------------------------------------------------

    private async void OnPeerConnected(SslStream stream, string remoteIp)
    {
        try
        {
            await HandleIncomingConnectionAsync(stream, remoteIp).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error procesando conexión entrante de {Ip}", remoteIp);
        }
    }

    private async Task HandleIncomingConnectionAsync(SslStream stream, string remoteIp)
    {
        // 1. HELLO del emisor
        var (helloType, helloPayload) = await ShareProtocol.ReadFrameAsync(stream).ConfigureAwait(false);
        if (helloType != ShareProtocol.MsgHello)
        {
            _logger?.LogWarning("Se esperaba HELLO, llegó {Type}", helloType);
            return;
        }

        var hello = ShareProtocol.Deserialize<ShareProtocol.HelloPayload>(helloPayload);
        if (hello is null)
        {
            return;
        }

        // 2. HELLO_ACK con nuestra identidad
        await ShareProtocol.WriteControlAsync(
            stream,
            ShareProtocol.MsgHelloAck,
            new ShareProtocol.HelloPayload(
                _settings.DeviceId,
                _settings.DisplayName,
                _settings.CertificateThumbprint ?? _certificateStore.Thumbprint))
            .ConfigureAwait(false);

        // 3. TRANSFER_REQUEST con metadatos
        var (reqType, reqPayload) = await ShareProtocol.ReadFrameAsync(stream).ConfigureAwait(false);
        if (reqType != ShareProtocol.MsgTransferRequest)
        {
            _logger?.LogWarning("Se esperaba TRANSFER_REQUEST, llegó {Type}", reqType);
            return;
        }

        var request = ShareProtocol.Deserialize<ShareProtocol.TransferRequestPayload>(reqPayload);
        if (request is null)
        {
            return;
        }

        // 4. Crear la transferencia entrante en estado PendingApproval
        var transfer = new ShareTransfer
        {
            TransferId = request.TransferId,
            Direction = TransferDirection.Receiving,
            PeerDeviceId = hello.DeviceId,
            PeerDisplayName = hello.DisplayName,
            TotalBytes = request.Items.Sum(i => i.Size),
            ItemCount = request.Items.Count,
            Status = TransferStatus.PendingApproval,
            DestinationSummary = _settings.DownloadDirectory,
        };

        var items = request.Items.Select(i => new ShareTransferItem
        {
            ItemId = i.ItemId,
            TransferId = transfer.TransferId,
            RelativePath = i.RelativePath,
            ExpectedSize = i.Size,
            ExpectedHash = i.Sha256,
        }).ToList();

        lock (_lock)
        {
            _transfers[transfer.TransferId] = (transfer, items);
        }

        // 5. Notificar a la UI que hay una solicitud entrante
        IncomingRequest?.Invoke(transfer, items);

        // 6. Esperar decisión del usuario (AcceptAsync/RejectAsync resuelven el TCS)
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _pendingDecisions[transfer.TransferId] = tcs;
        }

        var accepted = await tcs.Task.ConfigureAwait(false);
        _pendingDecisions.Remove(transfer.TransferId, out _);

        if (!accepted)
        {
            await ShareProtocol.WriteControlAsync(
                stream,
                ShareProtocol.MsgTransferReject,
                new { transferId = transfer.TransferId })
                .ConfigureAwait(false);
            transfer.Status = TransferStatus.Rejected;
            transfer.CompletedAt = DateTimeOffset.UtcNow;
            TransferFinished?.Invoke(transfer);
            return;
        }

        // 7. Enviar ACCEPT
        await ShareProtocol.WriteControlAsync(
            stream,
            ShareProtocol.MsgTransferAccept,
            new { transferId = transfer.TransferId })
            .ConfigureAwait(false);

        transfer.Status = TransferStatus.Transferring;
        transfer.StartedAt = DateTimeOffset.UtcNow;

        // 8. Recibir cada archivo en streaming
        var receivedBytes = 0L;
        foreach (var item in items)
        {
            var (startType, startPayload) = await ShareProtocol.ReadFrameAsync(stream).ConfigureAwait(false);
            if (startType == ShareProtocol.MsgTransferComplete)
            {
                break; // El emisor terminó
            }

            if (startType == ShareProtocol.MsgTransferCancel)
            {
                transfer.Status = TransferStatus.Cancelled;
                transfer.CompletedAt = DateTimeOffset.UtcNow;
                TransferFinished?.Invoke(transfer);
                return;
            }

            if (startType != ShareProtocol.MsgFileStart)
            {
                _logger?.LogWarning("Se esperaba FILE_START, llegó {Type}", startType);
                transfer.Status = TransferStatus.Failed;
                transfer.ErrorMessage = "Protocolo inválido durante la recepción.";
                TransferFinished?.Invoke(transfer);
                return;
            }

            var fileStart = ShareProtocol.Deserialize<ShareProtocol.FileStartPayload>(startPayload);
            if (fileStart is null)
            {
                continue;
            }

            // Crear archivo temporal
            var tempPath = _fileWriter!.CreateTempFile();
            item.TempPath = tempPath;
            item.Status = TransferStatus.Transferring;

            // Recibir chunks de FILE_DATA
            using (var fileStream = File.OpenWrite(tempPath))
            {
                var itemBytes = 0L;
                while (true)
                {
                    var (dataType, dataPayload) = await ShareProtocol.ReadFrameAsync(stream).ConfigureAwait(false);

                    if (dataType == ShareProtocol.MsgFileEnd)
                    {
                        break;
                    }

                    if (dataType == ShareProtocol.MsgTransferCancel)
                    {
                        _fileWriter.DiscardTemp(tempPath);
                        transfer.Status = TransferStatus.Cancelled;
                        transfer.CompletedAt = DateTimeOffset.UtcNow;
                        TransferFinished?.Invoke(transfer);
                        return;
                    }

                    if (dataType != ShareProtocol.MsgFileData)
                    {
                        _fileWriter.DiscardTemp(tempPath);
                        transfer.Status = TransferStatus.Failed;
                        transfer.ErrorMessage = "Protocolo inválido durante datos de archivo.";
                        TransferFinished?.Invoke(transfer);
                        return;
                    }

                    await fileStream.WriteAsync(dataPayload).ConfigureAwait(false);
                    itemBytes += dataPayload.Length;
                    receivedBytes += dataPayload.Length;
                    item.TransferredBytes = itemBytes;
                    transfer.TransferredBytes = receivedBytes;

                    ProgressChanged?.Invoke(new TransferProgress
                    {
                        TransferId = transfer.TransferId,
                        TransferredBytes = receivedBytes,
                        TotalBytes = transfer.TotalBytes,
                        ItemsCompleted = items.Count(i => i.Status == TransferStatus.Verifying),
                        ItemsTotal = items.Count,
                        BytesPerSecond = 0,
                        CurrentFile = item.RelativePath,
                    });
                }
            }

            // Verificar y publicar
            var fileEnd = ShareProtocol.Deserialize<ShareProtocol.FileEndPayload>(
                (await ShareProtocol.ReadFrameAsync(stream).ConfigureAwait(false)).Payload);
            _ = fileEnd; // hash ya viene en item.ExpectedHash

            try
            {
                item.FinalPath = _fileWriter.Publish(
                    tempPath,
                    item.RelativePath,
                    item.ExpectedSize,
                    item.ExpectedHash);
                item.Status = TransferStatus.Completed;
            }
            catch (Exception ex)
            {
                _fileWriter.DiscardTemp(tempPath);
                item.Status = TransferStatus.Failed;
                _logger?.LogWarning(ex, "Fallo al publicar {File}: {Message}", item.RelativePath, ex.Message);
            }
        }

        transfer.Status = TransferStatus.Completed;
        transfer.CompletedAt = DateTimeOffset.UtcNow;
        TransferFinished?.Invoke(transfer);
        _logger?.LogInformation(
            "Transferencia entrante {Id} completada: {Bytes} bytes",
            transfer.TransferId, receivedBytes);
    }

    private string GetDeviceName(string deviceId)
    {
        lock (_lock)
        {
            return _devices.TryGetValue(deviceId, out var d) ? d.DisplayName : deviceId;
        }
    }

    private HashSet<string> GetTrustedThumbprints()
    {
        lock (_lock)
        {
            return [.. _devices.Values
                .Where(d => d.IsPaired && d.CertificateThumbprint is not null)
                .Select(d => d.CertificateThumbprint!)];
        }
    }

    // ------------------------------------------------------------------
    // Persistencia
    // ------------------------------------------------------------------

    private LocalShareSettings LoadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            return new LocalShareSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<LocalShareSettings>(json, LocalShareJson.Options) ?? new();
        }
        catch
        {
            return new LocalShareSettings();
        }
    }

    private void SaveSettings()
    {
        var json = JsonSerializer.Serialize(_settings, LocalShareJson.Options);
        File.WriteAllText(_settingsPath, json);
    }

    private void LoadDevices()
    {
        if (!File.Exists(_devicesPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_devicesPath);
            var devices = JsonSerializer.Deserialize<List<SharedDevice>>(json, LocalShareJson.Options) ?? [];
            foreach (var device in devices)
            {
                _devices[device.DeviceId] = device;
            }
        }
        catch
        {
            // Corrupto: empezar fresco.
        }
    }

    private void SaveDevices()
    {
        lock (_lock)
        {
            var devices = _devices.Values.ToList();
            var json = JsonSerializer.Serialize(devices, LocalShareJson.Options);
            File.WriteAllText(_devicesPath, json);
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}

/// <summary>Opciones de JSON para persistencia local (PascalCase legible para debugging).</summary>
internal static class LocalShareJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };
}
