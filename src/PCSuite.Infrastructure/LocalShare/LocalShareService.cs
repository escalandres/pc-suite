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

    public Task<SharedDevice> PairAsync(string address, int port, CancellationToken ct = default)
    {
        // Conectar al puerto remoto, leer su certificado y deviceId
        // Esto es un placeholder — el protocolo completo se implementa en 16.2
        var deviceId = $"pending-{address}:{port}";
        var device = new SharedDevice
        {
            DeviceId = deviceId,
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
        SaveDevices();
        DeviceChanged?.Invoke(device);

        return Task.FromResult(device);
    }

    public Task<SharedDevice> ConfirmPairingAsync(string deviceId, string pin, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_devices.TryGetValue(deviceId, out var device))
            {
                throw new InvalidOperationException($"Dispositivo {deviceId} no encontrado.");
            }

            device.Trust = DeviceTrust.Paired;
            SaveDevices();
            DeviceChanged?.Invoke(device);
            _server!.TrustedThumbprints = GetTrustedThumbprints();
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

        lock (_lock)
        {
            if (!_devices.TryGetValue(deviceId, out var device) || !device.IsPaired)
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
                var relativePath = Path.GetFileName(localPath);
                var size = new FileInfo(localPath).Length;
                items.Add(new ShareTransferItem
                {
                    ItemId = Guid.NewGuid().ToString("D"),
                    TransferId = transferId,
                    RelativePath = relativePath,
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
                    var relativePath = Path.Combine(rootName, file[(localPath.Length + 1)..]);
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

        var transfer = new ShareTransfer
        {
            TransferId = transferId,
            Direction = TransferDirection.Sending,
            PeerDeviceId = deviceId,
            PeerDisplayName = GetDeviceName(deviceId),
            TotalBytes = totalBytes,
            ItemCount = items.Count,
            Status = TransferStatus.PendingApproval,
        };

        lock (_lock)
        {
            _transfers[transferId] = (transfer, items);
        }

        // TODO 16.2: conectar al receptor y enviar la solicitud

        return Task.FromResult(transfer);
    }

    // ------------------------------------------------------------------
    // Transferencias (entrantes)
    // ------------------------------------------------------------------

    public Task AcceptAsync(string transferId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_transfers.TryGetValue(transferId, out var entry))
            {
                entry.Transfer.Status = TransferStatus.Approved;
                // TODO 16.2: iniciar recepción en el TlsTransferServer
            }
        }

        return Task.CompletedTask;
    }

    public Task RejectAsync(string transferId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_transfers.TryGetValue(transferId, out var entry))
            {
                entry.Transfer.Status = TransferStatus.Rejected;
                entry.Transfer.CompletedAt = DateTimeOffset.UtcNow;
                TransferFinished?.Invoke(entry.Transfer);
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

    private void OnPeerConnected(SslStream stream, string remoteIp)
    {
        // TODO 16.2: leer el protocolo del stream, crear transferencia
        // entrante, disparar IncomingRequest, y procesar el contenido.
        _logger?.LogInformation("Peer conectado desde {Ip}", remoteIp);
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
