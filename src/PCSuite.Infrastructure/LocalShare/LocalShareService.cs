using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Events;
using PCSuite.Core.Jobs;
using PCSuite.Core.LocalShare;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure;
using PCSuite.Infrastructure.Processes;

namespace PCSuite.Infrastructure.LocalShare;

/// <summary>
/// Implementación de ILocalShareService: orquesta el ciclo de vida del
/// listener TCP+TLS, coordina transferencias, gestiona dispositivos y
/// configura el módulo. Las transferencias usan el patrón Job (IJobManager).
/// </summary>
public sealed class LocalShareService : ILocalShareService, IDisposable
{
    private readonly CertificateStore _certificateStore;
    private readonly PartialsStore _partials;
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
    private readonly IElevationManager? _elevation;
    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// Claves de parciales en uso por transferencias activas (16.7.3).
    /// Evita que dos transferencias simultáneas del MISMO archivo del MISMO
    /// par pisen el mismo temporal del manifiesto.
    /// </summary>
    private readonly HashSet<string> _claimedPartials = new(StringComparer.Ordinal);

    /// <summary>CTS por transferencia saliente (16.7.4): cancelación REAL del envío.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _transferCts = new(StringComparer.Ordinal);

    /// <summary>Transferencias salientes pausadas por el usuario (16.7.4).</summary>
    private readonly HashSet<string> _pausedTransfers = new(StringComparer.Ordinal);

    /// <summary>
    /// Decisiones de emparejamiento entrantes esperando al usuario (receptor).
    /// Key = requestId, value = TCS (true = aceptar).
    /// </summary>
    private readonly Dictionary<string, TaskCompletionSource<bool>> _pendingPairDecisions = new(StringComparer.Ordinal);

    /// <summary>Tiempo que el receptor tiene para decidir un emparejamiento.</summary>
    private static readonly TimeSpan PairDecisionTimeout = TimeSpan.FromSeconds(120);

    /// <summary>CTS por intento de emparejamiento saliente (para CancelPairingAsync).</summary>
    private readonly Dictionary<string, CancellationTokenSource> _pairInitiationCts = new(StringComparer.Ordinal);

    /// <summary>Publisher de eventos hacia la UI (DeferredEventPublisher en la app).</summary>
    private readonly IEventPublisher? _events;

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
    public event Action<LocalSharePairRequest>? PairRequested;
    public event Action<LocalSharePairFinished>? PairFinished;

    // ------------------------------------------------------------------
    // Constructor
    // ------------------------------------------------------------------

    public LocalShareService(
        string dataDirectory,
        ILogger? logger = null,
        IElevationManager? elevation = null,
        IProcessRunner? processRunner = null,
        IEventPublisher? events = null)
    {
        _logger = logger;
        _elevation = elevation;
        _events = events;
        _processRunner = processRunner ?? new ProcessRunner();
        var localShareDir = Path.Combine(dataDirectory, "LocalShare");
        Directory.CreateDirectory(localShareDir);
        _certificateStore = new CertificateStore(localShareDir);
        _partials = new PartialsStore(localShareDir);
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
            // El archivo es la fuente de verdad (cada mutación persiste):
            // releer al arrancar. Cubre el caso real del bug 2026-10-19 —
            // un devices.json con la IP rancia arriba, escrito por una
            // sesión anterior, debe entrar en memoria tal cual para que
            // el fallback de TransferClient lo salve.
            lock (_lock)
            {
                _devices.Clear();
            }

            LoadDevices();

            _fileWriter = new SafeFileWriter(_settings.DownloadDirectory);

            // Descubiertos rancios (app muerta sin pasar por Stop): los
            // Unknown que llevan >5 min sin anunciarse ya no están en la
            // red (los anuncios van cada 15 s). Los emparejados/revocados
            // son decisiones del usuario: NUNCA se tocan aquí.
            lock (_lock)
            {
                var stale = _devices
                    .Where(kvp => kvp.Value.Trust == DeviceTrust.Unknown
                        && DateTimeOffset.UtcNow - kvp.Value.LastSeenAt > StaleDiscoveryAge)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var id in stale)
                {
                    _devices.Remove(id);
                }

                if (stale.Count > 0)
                {
                    SaveDevices();
                    _logger?.LogInformation("{Count} dispositivos descubiertos rancios retirados", stale.Count);
                }
            }

            // Reanudación (16.7.3): podar entradas caducadas (>7 días o temp
            // desaparecido) y conservar los temporales vivos del manifiesto.
            foreach (var staleTemp in _partials.Prune(PartialsMaxAge))
            {
                try { File.Delete(staleTemp); } catch { /* best-effort */ }
            }

            _fileWriter.CleanupOrphanedTempFiles(_partials.ActiveTempFiles());

            _server = new TlsTransferServer(
                _certificateStore.Certificate,
                _logger,
                Math.Clamp(_settings.MaxSimultaneousTransfers, 1, 10));

            // Adaptador (16.7.3): al terminar el diálogo por CUALQUIER camino
            // (éxito, rechazo, cancelación o caída de conexión) se persiste el
            // manifiesto de parciales y se liberan los claims de esta conexión.
            // Un claim filtrado desactivaría la reanudación de ese par+ruta
            // hasta reiniciar el servicio.
            _server.PeerConnected += async (sslStream, remoteIp) =>
            {
                var claimedKeys = new List<string>();
                try
                {
                    await HandleIncomingConnectionAsync(sslStream, remoteIp, claimedKeys).ConfigureAwait(false);
                }
                finally
                {
                    foreach (var key in claimedKeys)
                    {
                        ReleasePartialClaim(key);
                    }

                    _partials.Save();
                }
            };

            _server.Start(_settings.Port, ParseBindAddress(_settings.BindAddress));

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
                _discovery.DeviceLeft += OnDeviceLeft;
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
        List<SharedDevice> discovered;

        // Resolver decisiones pendientes como rechazadas: los handlers que
        // esperaban la confirmación del usuario terminan limpiamente en vez
        // de quedar esperando un TCS que ya nadie resolverá.
        lock (_lock)
        {
            foreach (var tcs in _pendingDecisions.Values)
            {
                tcs.TrySetResult(false);
            }

            _pendingDecisions.Clear();

            foreach (var tcs in _pendingPairDecisions.Values)
            {
                tcs.TrySetResult(false);
            }

            _pendingPairDecisions.Clear();
            _claimedPartials.Clear();
            _pausedTransfers.Clear();

            // Emparejamientos salientes en curso: abortarlos limpio.
            foreach (var cts in _pairInitiationCts.Values.ToList())
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { /* carrera con finally */ }
            }

            // Cancelar los envíos activos (16.7.4): desenlace único vía OCE.
            // No se vacía el diccionario: la tarea en background hace el
            // Remove+Dispose de su propio CTS en su finally.
            foreach (var cts in _transferCts.Values.ToList())
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { /* carrera con finally */ }
            }

            // Los dispositivos DESCUBIERTOS son perecederos: al detener el
            // servicio dejan de verse (nadie está anunciando ya). Los
            // emparejados y los revocados son decisiones del usuario y
            // sobreviven a las sesiones — su punto de existir.
            discovered = _devices
                .Where(kvp => kvp.Value.Trust == DeviceTrust.Unknown)
                .Select(kvp => kvp.Value)
                .ToList();
            foreach (var device in discovered)
            {
                _devices.Remove(device.DeviceId);
            }

            SaveDevices();

            // Notificar la limpieza: la UI refresca la lista con lo que queda.
            foreach (var device in discovered)
            {
                DeviceChanged?.Invoke(device);
                _events?.SendEvent(EventIds.LocalShareDeviceChanged, new LocalShareDeviceChangedPayload
                {
                    DeviceId = device.DeviceId,
                    DisplayName = device.DisplayName,
                    Trust = "removed",
                    Address = null,
                    Port = 0,
                });
            }
        }

        _discovery?.Dispose();
        _discovery = null;
        _server?.Dispose();
        _server = null;
        _fileWriter?.Dispose();
        _fileWriter = null;
        Status = LocalShareServiceStatus.Stopped;
        _logger?.LogInformation(
            "Local Share detenido ({Removed} dispositivos descubiertos retirados)",
            discovered.Count);
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
        string deviceId, string displayName, string address, int port, string thumbprint) =>
        NoteDiscoveredDevice(deviceId, displayName, address, port, thumbprint);

    /// <summary>
    /// GOODBYE recibido (el otro PCSuite se detiene): retirar su entrada de
    /// discovery AL INSTANTE si es un desconocido (los emparejados conservan
    /// la confianza; su badge "Sin señal" llegará al caducar el LastSeenAt).
    /// </summary>
    private void OnDeviceLeft(string deviceId)
    {
        SharedDevice? removed = null;
        lock (_lock)
        {
            if (_devices.TryGetValue(deviceId, out var device)
                && device.Trust == DeviceTrust.Unknown)
            {
                _devices.Remove(deviceId);
                SaveDevices();
                removed = device;
            }
        }

        if (removed is not null)
        {
            DeviceChanged?.Invoke(removed);
            _events?.SendEvent(EventIds.LocalShareDeviceChanged, new LocalShareDeviceChangedPayload
            {
                DeviceId = removed.DeviceId,
                DisplayName = removed.DisplayName,
                Trust = "removed",
                Address = null,
                Port = 0,
            });
            _logger?.LogInformation("Discovery: {Name} se ha ido", removed.DisplayName);
        }
    }

    /// <summary>
    /// Registra o actualiza un dispositivo descubierto por multicast (trust
    /// Unknown: el usuario debe emparejar antes de transferir). Internal para
    /// los tests E2E, que simulan el discovery sin sockets UDP.
    /// </summary>
    internal void NoteDiscoveredDevice(
        string deviceId, string displayName, string address, int port, string thumbprint)
    {
        lock (_lock)
        {
            if (_devices.TryGetValue(deviceId, out var existing))
            {
                // Actualizar: LastSeenAt + MRU de direcciones.
                existing.LastSeenAt = DateTimeOffset.UtcNow;

                // MRU (bug 2026-10-19): la dirección que anuncia AHORA va al
                // frente — las conexiones salientes prueban KnownAddresses[0]
                // primero. Antes solo se AÑADÍAN al final: la IP del día del
                // emparejamiento quedaba congelada arriba y, tras un cambio de
                // IP (DHCP/otra interfaz), los envíos conectaban a una IP
                // rancia y morían con "conexión rechazada" sin que el
                // receptor llegara a enterarse.
                var addresses = existing.KnownAddresses
                    .Where(a => !a.Equals(address, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                addresses.Insert(0, address);
                existing.KnownAddresses = addresses;

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
            RaiseDeviceChanged(_devices[deviceId]);
        }
    }

    /// <summary>
    /// Empareja un dispositivo por IP:puerto. Conecta via TCP+TLS para
    /// leer el certificado del par, obtiene su deviceId y registra la
    /// confianza mutua mediante verificación de PIN.
    /// </summary>
    public async Task<LocalSharePairInitiation> PairWithDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        if (Status != LocalShareServiceStatus.Running)
        {
            throw new InvalidOperationException("El servicio Local Share no está activo. Inítialo primero.");
        }

        SharedDevice device;
        lock (_lock)
        {
            if (!_devices.TryGetValue(deviceId, out device!))
            {
                throw new InvalidOperationException($"Dispositivo {deviceId} desconocido.");
            }

            if (device.KnownAddresses.Count == 0 || device.Port <= 0)
            {
                throw new InvalidOperationException(
                    "El dispositivo no tiene dirección conocida (aún no se ha descubierto por la red).");
            }
        }

        var requestId = Guid.NewGuid().ToString("D");
        var initiation = new TaskCompletionSource<LocalSharePairInitiation>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // CTS por intento: CancelPairingAsync aborta la conexión en curso.
        // La tarea en background es la dueña (lo dispone en su finally).
        var pairCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_lock)
        {
            _pairInitiationCts[requestId] = pairCts;
        }

        // El diálogo completo corre en background: la op vuelve en cuanto el
        // código está disponible (tras HELLO) y el desenlace llega por evento.
        _ = Task.Run(async () =>
        {
            LocalSharePairFinished? finished = null;
            try
            {
                var client = new TransferClient(_certificateStore.Certificate, _logger);
                var outcome = await client.PairAsync(
                    device,
                    _settings.DeviceId,
                    _settings.DisplayName,
                    _settings.CertificateThumbprint ?? _certificateStore.Thumbprint,
                    EffectivePort,
                    code => initiation.TrySetResult(new LocalSharePairInitiation(
                        requestId, code, device.DisplayName)),
                    pairCts.Token).ConfigureAwait(false);

                if (outcome.Accepted)
                {
                    // Registrar al par con su huella VIVA del handshake (no la
                    // anunciada: tras aceptar, la conexión es de confianza).
                    RegisterPairedDevice(
                        outcome.PeerDeviceId,
                        outcome.PeerDisplayName,
                        device.KnownAddresses[0],
                        device.Port,
                        outcome.PeerThumbprint);
                    finished = new LocalSharePairFinished(
                        requestId, LocalSharePairOutcome.Accepted,
                        outcome.PeerDeviceId, outcome.PeerDisplayName);
                }
                else
                {
                    finished = new LocalSharePairFinished(
                        requestId, LocalSharePairOutcome.Rejected,
                        outcome.PeerDeviceId, outcome.PeerDisplayName,
                        "El otro equipo rechazó el emparejamiento.");
                }
            }
            catch (OperationCanceledException)
            {
                initiation.TrySetCanceled(pairCts.Token);
                finished = new LocalSharePairFinished(
                    requestId, LocalSharePairOutcome.Failed, ErrorMessage: "Emparejamiento cancelado.");
            }
            catch (Exception ex)
            {
                initiation.TrySetException(ex);
                finished = new LocalSharePairFinished(
                    requestId, LocalSharePairOutcome.Failed, ErrorMessage: ex.Message);
            }
            finally
            {
                lock (_lock)
                {
                    _pairInitiationCts.Remove(requestId, out var owned);
                    owned?.Dispose();
                }
            }

            RaisePairFinished(finished);
        }, pairCts.Token);

        // Espera rápida: TLS + HELLO. Si el par no está accesible, falla aquí.
        return await initiation.Task.WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
    }

    public Task CancelPairingAsync(string requestId, CancellationToken ct = default)
    {
        CancellationTokenSource? toCancel;
        lock (_lock)
        {
            _pairInitiationCts.TryGetValue(requestId, out toCancel);
        }

        // La tarea en background hace el Remove+Dispose en su finally.
        // No es error cancelar dos veces o llegar tarde (idempotente).
        try { toCancel?.Cancel(); } catch (ObjectDisposedException) { /* carrera */ }
        return Task.CompletedTask;
    }

    public Task AcceptPairingAsync(string requestId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_pendingPairDecisions.TryGetValue(requestId, out var tcs))
            {
                throw new InvalidOperationException(
                    "La solicitud de emparejamiento ya no existe (¿expiró o ya fue respondida?).");
            }

            tcs.TrySetResult(true);
        }

        return Task.CompletedTask;
    }

    public Task RejectPairingAsync(string requestId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_pendingPairDecisions.TryGetValue(requestId, out var tcs))
            {
                tcs.TrySetResult(false);
            }
        }

        return Task.CompletedTask;
    }

    public Task RevokeTrustAsync(string deviceId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_devices.TryGetValue(deviceId, out var device))
            {
                device.Trust = DeviceTrust.Revoked;
                SaveDevices();
                RaiseDeviceChanged(device);
            }
        }

        return Task.CompletedTask;
    }

    public SharedDevice RegisterPairedDevice(
        string deviceId, string displayName, string address, int port, string certificateThumbprint)
    {
        lock (_lock)
        {
            var device = new SharedDevice
            {
                DeviceId = deviceId,
                DisplayName = displayName,
                KnownAddresses = [address],
                Port = port,
                Trust = DeviceTrust.Paired,
                CertificateThumbprint = certificateThumbprint,
                LastSeenAt = DateTimeOffset.UtcNow,
            };
            _devices[deviceId] = device;
            SaveDevices();
            RaiseDeviceChanged(device);
            return device;
        }
    }

    // ------------------------------------------------------------------
    // Firewall
    // ------------------------------------------------------------------

    public Task<LocalShareFirewallDiagnostic> GetFirewallDiagnosticAsync(CancellationToken ct = default)
    {
        // Solo lectura: netsh show rule no requiere elevación.
        return LocalShareFirewall.DiagnoseAsync(_processRunner, EffectivePort, ct);
    }

    public async Task AddFirewallRuleAsync(CancellationToken ct = default)
    {
        if (_elevation is null)
        {
            throw new InvalidOperationException(
                "La gestión de firewall no está disponible en este contexto.");
        }

        // El puerto es lo único del payload; el proceso elevado lo revalida.
        var payload = JsonSerializer.SerializeToElement(
            new LocalShareFirewall.FirewallRulePayload { Port = EffectivePort },
            LocalShareJson.Options);

        var elevated = await _elevation.RunElevatedAsync(
            new ElevatedOperation
            {
                OperationId = ElevatedOperationIds.LocalShareFirewallRule,
                Payload = payload,
            },
            ct).ConfigureAwait(false);

        if (!elevated.Success)
        {
            throw new InvalidOperationException(
                elevated.ErrorMessage ?? "No se pudo crear la regla de firewall.");
        }
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

        // Límite propio del emisor (16.7.2): falla rápido sin conectar. El
        // receptor aplica además SU límite al recibir la solicitud.
        if (_settings.MaxTransferSizeBytes > 0 && totalBytes > _settings.MaxTransferSizeBytes)
        {
            throw new InvalidOperationException(
                $"La transferencia ({totalBytes / 1024.0 / 1024.0:F1} MB) supera tu límite configurado ({_settings.MaxTransferSizeBytes / 1024.0 / 1024.0:F1} MB).");
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

        // CTS por transferencia (16.7.4): cancelación REAL del envío — tanto
        // desde el caller como desde CancelTransfer — con desenlace único.
        // La tarea en background es la dueña: lo dispone en su finally.
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_lock)
        {
            _transferCts[transferId] = linkedCts;
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
                    _settings.MaxBandwidthBytesPerSecond,
                    () => IsTransferPaused(transferId),
                    bytes =>
                    {
                        RaiseProgress(new TransferProgress
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
                    linkedCts.Token).ConfigureAwait(false);

                foreach (var item in items)
                {
                    item.Status = TransferStatus.Completed;
                }

                RaiseTransferFinished(transfer);
            }
            catch (OperationCanceledException)
            {
                transfer.Status = TransferStatus.Cancelled;
                transfer.CompletedAt = DateTimeOffset.UtcNow;
                RaiseTransferFinished(transfer);
            }
            catch (Exception ex)
            {
                transfer.Status = TransferStatus.Failed;
                transfer.ErrorMessage = ex.Message;
                transfer.CompletedAt = DateTimeOffset.UtcNow;
                _logger?.LogWarning(ex, "Transferencia {Id} falló: {Message}", transferId, ex.Message);
                RaiseTransferFinished(transfer);
            }
            finally
            {
                CancellationTokenSource? toDispose;
                lock (_lock)
                {
                    _transferCts.Remove(transferId, out toDispose);
                    _pausedTransfers.Remove(transferId);
                }

                toDispose?.Dispose();
            }
        }, linkedCts.Token);

        return Task.FromResult(transfer);
    }

    /// <summary>
    /// Envía un mensaje de TEXTO a un dispositivo emparejado (Task 016.8).
    /// Reusa el pipeline completo de transferencias — TLS, consentimiento
    /// del receptor, SHA-256, progreso, cancelación — con un único ítem
    /// marcado como mensaje. El texto viaja por el canal como el cuerpo del
    /// "archivo" (temporal del emisor, encriptado en tránsito igual que
    /// cualquier archivo); el receptor lo muestra como mensaje.
    /// </summary>
    public Task<ShareTransfer> SendMessageAsync(
        string deviceId, string text, CancellationToken ct = default)
    {
        if (Status != LocalShareServiceStatus.Running)
        {
            throw new InvalidOperationException("El servicio Local Share no está activo.");
        }

        var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
        var textBytes = Encoding.UTF8.GetByteCount(normalized);
        if (textBytes == 0)
        {
            throw new InvalidOperationException("El mensaje está vacío.");
        }

        if (textBytes > MaxMessageBytes)
        {
            throw new InvalidOperationException(
                $"El mensaje ({textBytes / 1024} KB) supera el límite de {MaxMessageBytes / 1024} KB.");
        }

        SharedDevice device;
        lock (_lock)
        {
            if (!_devices.TryGetValue(deviceId, out device!) || !device.IsPaired)
            {
                throw new InvalidOperationException($"El dispositivo {deviceId} no está emparejado.");
            }
        }

        var transferId = Guid.NewGuid().ToString("D");
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");

        // Carpeta propia para que los mensajes no se mezclen con descargas.
        var relativePath = $"Mensajes/mensaje-{timestamp}-{transferId[..8]}.txt";

        var tempPath = Path.Combine(Path.GetTempPath(), $"pcsuite-msg-{transferId}.txt");
        File.WriteAllText(tempPath, normalized, Encoding.UTF8);

        var items = new List<ShareTransferItem>
        {
            new()
            {
                ItemId = Guid.NewGuid().ToString("D"),
                TransferId = transferId,
                RelativePath = relativePath,
                ExpectedSize = new FileInfo(tempPath).Length,
                LocalSourcePath = tempPath,
                IsMessage = true,
            },
        };

        var transfer = new ShareTransfer
        {
            TransferId = transferId,
            Direction = TransferDirection.Sending,
            PeerDeviceId = deviceId,
            PeerDisplayName = device.DisplayName,
            TotalBytes = items[0].ExpectedSize,
            ItemCount = 1,
            IsMessage = true,
            Status = TransferStatus.PendingApproval,
        };

        lock (_lock)
        {
            _transfers[transferId] = (transfer, items);
        }

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_lock)
        {
            _transferCts[transferId] = linkedCts;
        }

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
                    _settings.MaxBandwidthBytesPerSecond,
                    () => IsTransferPaused(transferId),
                    bytes => { /* Un mensaje es un chunk: sin progreso intermedio. */ },
                    linkedCts.Token).ConfigureAwait(false);

                transfer.Status = TransferStatus.Completed;
                transfer.CompletedAt = DateTimeOffset.UtcNow;
                RaiseTransferFinished(transfer);
            }
            catch (OperationCanceledException)
            {
                transfer.Status = TransferStatus.Cancelled;
                transfer.CompletedAt = DateTimeOffset.UtcNow;
                RaiseTransferFinished(transfer);
            }
            catch (Exception ex)
            {
                transfer.Status = TransferStatus.Failed;
                transfer.ErrorMessage = ex.Message;
                transfer.CompletedAt = DateTimeOffset.UtcNow;
                RaiseTransferFinished(transfer);
            }
            finally
            {
                CancellationTokenSource? toDispose;
                lock (_lock)
                {
                    _transferCts.Remove(transferId, out toDispose);
                    _pausedTransfers.Remove(transferId);
                }

                toDispose?.Dispose();

                // El temporal del mensaje es desechable: el texto ya viajó
                // (o no) y el original vive en el input de la UI.
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // best-effort: queda en %TEMP% hasta la limpieza del SO.
                }
            }
        }, linkedCts.Token);

        return Task.FromResult(transfer);
    }

    /// <summary>Tope de un mensaje de texto (16 KB: un mensaje, no un archivo).</summary>
    public const int MaxMessageBytes = 16 * 1024;

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
        CancellationTokenSource? cts = null;

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

            if (entry.Transfer.Direction == TransferDirection.Sending
                && _transferCts.TryGetValue(transferId, out cts))
            {
                // Envío activo: cancelación REAL vía CTS. La transición de
                // estado y el ÚNICO evento TransferFinished los hace la tarea
                // en background al recibir la OCE (antes solo se cambiaba el
                // estado y el flujo seguía hasta el final, sobrescribiendo
                // Cancelled con Completed).
            }
            else
            {
                // Entrante (o saliente sin CTS, p. ej. pendiente de aprobación
                // cuya tarea ya terminó): transición directa. El handler del
                // receptor detecta el estado en el próximo chunk y para limpio.
                entry.Transfer.Status = TransferStatus.Cancelled;
                entry.Transfer.CompletedAt = DateTimeOffset.UtcNow;
                _pausedTransfers.Remove(transferId);
                RaiseTransferFinished(entry.Transfer);
            }
        }

        cts?.Cancel();
        return true;
    }

    public Task PauseTransferAsync(string transferId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_transfers.TryGetValue(transferId, out var entry))
            {
                throw new InvalidOperationException($"Transferencia {transferId} no encontrada.");
            }

            if (entry.Transfer.Direction != TransferDirection.Sending)
            {
                throw new InvalidOperationException("Solo se pueden pausar envíos.");
            }

            if (entry.Transfer.Status is not (TransferStatus.Transferring or TransferStatus.Paused))
            {
                throw new InvalidOperationException("Solo se puede pausar una transferencia en curso.");
            }

            _pausedTransfers.Add(transferId);
            entry.Transfer.Status = TransferStatus.Paused;
        }

        return Task.CompletedTask;
    }

    public Task ResumeTransferAsync(string transferId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_pausedTransfers.Remove(transferId)
                && _transfers.TryGetValue(transferId, out var entry)
                && entry.Transfer.Status == TransferStatus.Paused)
            {
                entry.Transfer.Status = TransferStatus.Transferring;
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>¿El envío está pausado? Lo consulta el bucle de chunks del emisor.</summary>
    private bool IsTransferPaused(string transferId)
    {
        lock (_lock)
        {
            return _pausedTransfers.Contains(transferId);
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
                    or TransferStatus.Paused
                    or TransferStatus.Verifying)
                .ToList();
            _transfers.Clear();
            foreach (var kvp in active)
            {
                _transfers[kvp.Key] = kvp.Value;
            }
        }

        _fileWriter?.CleanupOrphanedTempFiles(_partials.ActiveTempFiles());
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Notificaciones: evento de dominio + bridge (DeferredEventPublisher)
    // ------------------------------------------------------------------

    /// <summary>Dispara DeviceChanged y lo publica al bridge para la UI.</summary>
    private void RaiseDeviceChanged(SharedDevice device)
    {
        DeviceChanged?.Invoke(device);
        _events?.SendEvent(EventIds.LocalShareDeviceChanged, new LocalShareDeviceChangedPayload
        {
            DeviceId = device.DeviceId,
            DisplayName = device.DisplayName,
            Trust = device.Trust.ToString().ToLowerInvariant(),
            Address = device.KnownAddresses.FirstOrDefault(),
            Port = device.Port,
        });
    }

    /// <summary>Solicitud entrante de emparejamiento hacia la UI (receptor).</summary>
    private void RaisePairRequested(LocalSharePairRequest request)
    {
        PairRequested?.Invoke(request);
        _events?.SendEvent(EventIds.LocalSharePairRequested, new LocalSharePairRequestedPayload
        {
            RequestId = request.RequestId,
            PeerDeviceId = request.PeerDeviceId,
            PeerDisplayName = request.PeerDisplayName,
            Code = request.Code,
        });
    }

    /// <summary>Desenlace de un emparejamiento iniciado por este equipo.</summary>
    private void RaisePairFinished(LocalSharePairFinished finished)
    {
        PairFinished?.Invoke(finished);
        _events?.SendEvent(EventIds.LocalSharePairFinished, new LocalSharePairFinishedPayload
        {
            RequestId = finished.RequestId,
            Outcome = finished.Outcome.ToString().ToLowerInvariant(),
            DeviceId = finished.PeerDeviceId,
            DisplayName = finished.PeerDisplayName,
            ErrorMessage = finished.ErrorMessage,
        });
    }

    /// <summary>Publica una solicitud de transferencia entrante al bridge.</summary>
    private void RaiseIncomingRequest(ShareTransfer transfer, IReadOnlyList<ShareTransferItem> items)
    {
        IncomingRequest?.Invoke(transfer, items);
        _events?.SendEvent(EventIds.LocalShareIncomingRequest, new LocalShareIncomingRequestPayload
        {
            TransferId = transfer.TransferId,
            PeerDisplayName = transfer.PeerDisplayName,
            ItemCount = transfer.ItemCount,
            TotalBytes = transfer.TotalBytes,
            Items = [.. items.Select(i => new ShareTransferItemResult
            {
                ItemId = i.ItemId,
                RelativePath = i.RelativePath,
                ExpectedSize = i.ExpectedSize,
                ExpectedHash = i.ExpectedHash,
                Status = i.Status.ToString().ToLowerInvariant(),
                TransferredBytes = i.TransferredBytes,
            })],
        });
    }

    /// <summary>Publica el fin de una transferencia al bridge.</summary>
    private void RaiseTransferFinished(ShareTransfer transfer)
    {
        TransferFinished?.Invoke(transfer);
        _events?.SendEvent(EventIds.LocalShareTransferFinished, new LocalShareTransferFinishedPayload
        {
            TransferId = transfer.TransferId,
            Status = transfer.Status.ToString().ToLowerInvariant(),
            TransferredBytes = transfer.TransferredBytes,
            ErrorMessage = transfer.ErrorMessage,
            TotalBytes = transfer.TotalBytes,
        });
    }

    /// <summary>Publica el progreso de una transferencia al bridge.</summary>
    private void RaiseProgress(TransferProgress progress)
    {
        ProgressChanged?.Invoke(progress);
        _events?.SendEvent(EventIds.LocalShareTransferProgress, new LocalShareTransferProgressPayload
        {
            TransferId = progress.TransferId,
            TransferredBytes = progress.TransferredBytes,
            TotalBytes = progress.TotalBytes,
            BytesPerSecond = progress.BytesPerSecond,
            ItemsCompleted = progress.ItemsCompleted,
            ItemsTotal = progress.ItemsTotal,
            CurrentFile = progress.CurrentFile,
        });
    }

    /// <summary>¿La huella TLS pertenece a un dispositivo emparejado?</summary>
    private bool IsThumbprintTrusted(string thumbprint)
    {
        lock (_lock)
        {
            return GetTrustedThumbprints().Contains(thumbprint);
        }
    }

    // ------------------------------------------------------------------
    // Handlers internos
    // ------------------------------------------------------------------

    /// <summary>Vida máxima del estado de reanudación sin actividad.</summary>
    private static readonly TimeSpan PartialsMaxAge = TimeSpan.FromDays(7);

    /// <summary>Cada cuántos chunks se persiste el manifiesto de parciales.</summary>
    private const int PartialsSaveEveryChunks = 64; // 4 MB a chunks de 64KB

    /// <summary>
    /// Antigüedad a partir de la cual un dispositivo DESCUBIERTO (Unknown)
    /// se considera ausente de la red en el arranque. Los anuncios van cada
    /// 15 s: 5 min sin señal es inequívoco.
    /// </summary>
    private static readonly TimeSpan StaleDiscoveryAge = TimeSpan.FromMinutes(5);

    /// <summary>¿El deviceId remoto está emparejado (confianza activa)?</summary>
    private bool IsDevicePaired(string deviceId)
    {
        lock (_lock)
        {
            return _devices.TryGetValue(deviceId, out var device)
                && device.Trust == DeviceTrust.Paired;
        }
    }

    /// <summary>Libera el claim de un parcial (idempotente, thread-safe).</summary>
    private void ReleasePartialClaim(string partialKey)
    {
        lock (_lock)
        {
            _claimedPartials.Remove(partialKey);
        }
    }

    /// <summary>
    /// Diálogo de protocolo con un par conectado. La firma coincide con
    /// TlsTransferServer.PeerConnected (Func) para que el servidor espere
    /// a que este método termine antes de disponer la conexión.
    /// <paramref name="claimedKeys"/> recibe las claves de reanudación
    /// reclamadas por ESTA conexión: el adaptador de la suscripción las
    /// libera al terminar, vaya como vaya.
    /// </summary>
    private async Task HandleIncomingConnectionAsync(
        SslStream stream,
        string remoteIp,
        List<string> claimedKeys)
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

        // 3. Siguiente frame: TRANSFER_REQUEST (transferir) o PAIR_REQUEST
        //    (emparejar). Cualquier otra cosa cierra el diálogo.
        var (reqType, reqPayload) = await ShareProtocol.ReadFrameAsync(stream).ConfigureAwait(false);

        // Huella VIVA del cliente según el handshake TLS: material de
        // decisión para emparejar y de verificación para transferir.
        var clientThumbprint = TlsTransferServer.GetThumbprint(stream.RemoteCertificate);

        if (reqType == ShareProtocol.MsgPairRequest)
        {
            await HandlePairRequestAsync(stream, clientThumbprint, remoteIp, reqPayload)
                .ConfigureAwait(false);
            return;
        }

        if (reqType != ShareProtocol.MsgTransferRequest)
        {
            _logger?.LogWarning("Se esperaba TRANSFER_REQUEST, llegó {Type}", reqType);
            return;
        }

        // Seguridad (antes la imponía el handshake TLS, ahora relajado para
        // permitir emparejar): transferir exige una huella emparejada. El
        // rechazo llega limpio como ERROR not_paired, sin EOF críptico.
        if (clientThumbprint is null || !IsThumbprintTrusted(clientThumbprint))
        {
            _logger?.LogInformation(
                "TRANSFER_REQUEST de {Ip} con huella no emparejada: rechazado", remoteIp);
            await ShareProtocol.WriteControlAsync(
                stream,
                ShareProtocol.MsgError,
                new ShareProtocol.ErrorPayload(
                    "not_paired",
                    "Este equipo no está emparejado con el receptor. Pide al receptor que acepte el emparejamiento desde la página Local Share."))
                .ConfigureAwait(false);
            return;
        }

        var request = ShareProtocol.Deserialize<ShareProtocol.TransferRequestPayload>(reqPayload);
        if (request is null)
        {
            return;
        }

        // Límites del receptor (16.7.2): tamaño máximo y espacio disponible.
        // Se comprueban ANTES de molestar al usuario con una solicitud que no
        // puede completarse. El frame ERROR llega limpio al emisor.
        var limitError = LocalShareLimits.ValidateIncoming(
            request.Items.Sum(i => i.Size),
            _settings,
            _settings.DownloadDirectory);
        if (limitError is { } limit)
        {
            _logger?.LogInformation(
                "Solicitud de {Peer} rechazada por límites: {Code}", hello.DisplayName, limit.Code);
            await ShareProtocol.WriteControlAsync(
                stream,
                ShareProtocol.MsgError,
                new ShareProtocol.ErrorPayload(limit.Code, limit.Message)).ConfigureAwait(false);
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
            IsMessage = i.Kind == 1,
        }).ToList();

        // La lista pinta "Mensaje" sin cargar ítems: el flag viaja en la
        // transferencia (Task 16.8), como en el emisor.
        transfer.IsMessage = items.Any(i => i.IsMessage);

        lock (_lock)
        {
            _transfers[transfer.TransferId] = (transfer, items);
        }

        // 5. Registrar la decisión pendiente ANTES de notificar: la UI podría
        //    llamar AcceptAsync dentro del propio evento IncomingRequest.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _pendingDecisions[transfer.TransferId] = tcs;
        }

        if (_settings.AutoAcceptFromPaired && IsDevicePaired(hello.DeviceId))
        {
            // Aceptación automática de dispositivos emparejados: sin molestar
            // al usuario. La transferencia aparece en la lista como aprobada.
            transfer.Status = TransferStatus.Approved;
            tcs.TrySetResult(true);
        }
        else
        {
            // 6. Notificar a la UI que hay una solicitud entrante
            RaiseIncomingRequest(transfer, items);
        }

        // 7. Esperar decisión del usuario (AcceptAsync/RejectAsync resuelven el TCS)
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
            RaiseTransferFinished(transfer);
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
        var failedItems = new List<string>();
        foreach (var item in items)
        {
            var (startType, startPayload) = await ShareProtocol.ReadFrameAsync(stream).ConfigureAwait(false);
            if (startType == ShareProtocol.MsgTransferComplete)
            {
                break; // El emisor terminó sin más archivos.
            }

            if (startType == ShareProtocol.MsgTransferCancel)
            {
                transfer.Status = TransferStatus.Cancelled;
                transfer.CompletedAt = DateTimeOffset.UtcNow;
                RaiseTransferFinished(transfer);
                return;
            }

            if (startType != ShareProtocol.MsgFileStart)
            {
                _logger?.LogWarning("Se esperaba FILE_START, llegó {Type}", startType);
                transfer.Status = TransferStatus.Failed;
                transfer.ErrorMessage = "Protocolo inválido durante la recepción.";
                await ShareProtocol.WriteControlAsync(
                    stream,
                    ShareProtocol.MsgError,
                    new ShareProtocol.ErrorPayload("protocol", "Se esperaba FILE_START.")).ConfigureAwait(false);
                RaiseTransferFinished(transfer);
                return;
            }

            var fileStart = ShareProtocol.Deserialize<ShareProtocol.FileStartPayload>(startPayload);
            if (fileStart is null)
            {
                continue;
            }

            // ------------------------------------------------------------
            // RESUME (16.7.3): ¿existe un intento anterior de este par y
            // esta ruta con un prefijo resumible?
            // ------------------------------------------------------------
            var writer = _fileWriter!;
            var partialKey = $"{hello.DeviceId}\u001F{item.RelativePath}";
            var existing = _partials.Get(hello.DeviceId, item.RelativePath);
            long resumeOffset = 0;
            string tempPath;
            bool weClaimed;

            lock (_lock)
            {
                weClaimed = _claimedPartials.Add(partialKey);
                if (weClaimed)
                {
                    claimedKeys.Add(partialKey);
                }

                if (existing is not null
                    && weClaimed
                    && File.Exists(existing.TempPath)
                    && existing.Bytes > 0
                    && existing.Bytes <= item.ExpectedSize)
                {
                    // Prefijo válido de un intento anterior: continuar desde ahí.
                    resumeOffset = existing.Bytes;
                    tempPath = existing.TempPath;
                }
                else
                {
                    // Sin estado resumible (o en uso por otra transferencia
                    // simultánea): empezar desde cero. Si la clave está en
                    // uso por otra transferencia, NO tocamos su entrada del
                    // manifiesto ni su claim.
                    if (!weClaimed)
                    {
                        existing = null;
                    }
                    else if (existing is not null)
                    {
                        _partials.Remove(hello.DeviceId, item.RelativePath);
                        existing = null;
                    }

                    tempPath = writer.CreateTempFile();
                }
            }

            item.TempPath = tempPath;
            item.Status = TransferStatus.Transferring;
            item.TransferredBytes = resumeOffset;
            // El prefijo reanudado cuenta para la TRANSFERENCIA: el registro
            // y el progreso acumulan todo lo que ya viajó (no solo lo nuevo
            // de este intento) — si no, una reanudación completaría
            // mostrando menos bytes que el total.
            receivedBytes += resumeOffset;

            // Decir al emisor desde dónde continuar (v2: siempre responde).
            await ShareProtocol.WriteControlAsync(
                stream,
                ShareProtocol.MsgResume,
                new ShareProtocol.ResumePayload(item.ItemId, resumeOffset)).ConfigureAwait(false);

            // Registrar/actualizar el estado parcial (solo si esta
            // transferencia es la dueña del claim).
            if (weClaimed)
            {
                var entry = existing is not null
                    ? existing with { TempPath = tempPath, Bytes = resumeOffset }
                    : new PartialsStore.PartialEntry { TempPath = tempPath, Bytes = resumeOffset };
                _partials.Set(hello.DeviceId, item.RelativePath, entry);
            }

            var partialEntry = _partials.Get(hello.DeviceId, item.RelativePath);

            // Recibir chunks de FILE_DATA hasta FILE_END (capturado EN el bucle:
            // una lectura extra aquí consumiría el FILE_START del siguiente
            // archivo y rompería las transferencias multi-archivo).
            ShareProtocol.FileEndPayload? fileEnd = null;
            using (var fileStream = new FileStream(
                tempPath,
                resumeOffset > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write))
            {
                var itemBytes = resumeOffset;
                var chunkCount = 0;
                while (true)
                {
                    // Cancelación entrante (16.7.4): CancelTransfer ya marcó el
                    // estado y disparó el evento; el handler para limpio y el
                    // prefijo queda como estado de reanudación.
                    if (transfer.Status == TransferStatus.Cancelled)
                    {
                        if (weClaimed)
                        {
                            partialEntry!.Bytes = itemBytes;
                            ReleasePartialClaim(partialKey);
                            _partials.Save();
                        }

                        return;
                    }

                    var (dataType, dataPayload) = await ShareProtocol.ReadFrameAsync(stream).ConfigureAwait(false);

                    if (dataType == ShareProtocol.MsgFileEnd)
                    {
                        fileEnd = ShareProtocol.Deserialize<ShareProtocol.FileEndPayload>(dataPayload);
                        break;
                    }

                    if (dataType == ShareProtocol.MsgTransferCancel)
                    {
                        // El emisor abortó: el prefijo recibido ES reanudable
                        // (mismo criterio que una caída de conexión). NO borrar
                        // el temporal; Prune lo recogerá si nadie reintenta.
                        if (weClaimed)
                        {
                            partialEntry!.Bytes = itemBytes;
                            ReleasePartialClaim(partialKey);
                            _partials.Save();
                        }
                        transfer.Status = TransferStatus.Cancelled;
                        transfer.CompletedAt = DateTimeOffset.UtcNow;
                        RaiseTransferFinished(transfer);
                        return;
                    }

                    if (dataType != ShareProtocol.MsgFileData)
                    {
                        writer.DiscardTemp(tempPath);
                        if (weClaimed)
                        {
                            ReleasePartialClaim(partialKey);
                            _partials.Remove(hello.DeviceId, item.RelativePath);
                            _partials.Save();
                        }
                        transfer.Status = TransferStatus.Failed;
                        transfer.ErrorMessage = "Protocolo inválido durante datos de archivo.";
                        await ShareProtocol.WriteControlAsync(
                            stream,
                            ShareProtocol.MsgError,
                            new ShareProtocol.ErrorPayload("protocol", "Se esperaba FILE_DATA.")).ConfigureAwait(false);
                        RaiseTransferFinished(transfer);
                        return;
                    }

                    await fileStream.WriteAsync(dataPayload).ConfigureAwait(false);
                    itemBytes += dataPayload.Length;
                    receivedBytes += dataPayload.Length;
                    item.TransferredBytes = itemBytes;
                    transfer.TransferredBytes = receivedBytes;

                    // Mantener el manifiesto al día (cada 4 MB: si el proceso
                    // muere, se pierde como mucho esa ventana; el SHA-256 de
                    // Publish protege contra desalineos de offset).
                    if (weClaimed)
                    {
                        partialEntry!.Bytes = itemBytes;
                        if (++chunkCount % PartialsSaveEveryChunks == 0)
                        {
                            _partials.Save();
                        }
                    }

                    RaiseProgress(new TransferProgress
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

            // Verificar (tamaño + SHA-256 del archivo COMPLETO, incluida la
            // parte reanudada) y publicar. Publish solo devuelve si TODO
            // validó; si no, lanza y el archivo NUNCA aparece como completo.
            try
            {
                item.FinalPath = writer.Publish(
                    tempPath,
                    item.RelativePath,
                    item.ExpectedSize,
                    item.ExpectedHash);
                item.Status = TransferStatus.Completed;

                // Task 016.8: un mensaje llega por el mismo pipeline de
                // archivos (SHA-256, atómico, reanudable) pero se VIVE como
                // texto: leerlo tras publicar para que la UI lo muestre
                // como mensaje con botón de copiar. El archivo queda igual
                // en disco (procedencia).
                if (item.IsMessage)
                {
                    try
                    {
                        item.MessageText = await File.ReadAllTextAsync(
                            item.FinalPath!).ConfigureAwait(false);
                        // Task 021: también sobre la transferencia — así el
                        // evento TransferFinished (que solo pasa el registro)
                        // puede toalear el mensaje sin cargar ítems.
                        transfer.MessageText = item.MessageText;
                    }
                    catch
                    {
                        // best-effort: el archivo está publicado e íntegro;
                        // la UI muestra "ver en carpeta" aunque falte el texto.
                    }
                }

                if (weClaimed)
                {
                    _partials.Remove(hello.DeviceId, item.RelativePath);
                    ReleasePartialClaim(partialKey);
                    _partials.Save();
                }
            }
            catch (Exception ex)
            {
                writer.DiscardTemp(tempPath);
                item.Status = TransferStatus.Failed;
                failedItems.Add(item.RelativePath);
                // Estado de reanudación inservible: el prefijo no validó
                // (archivo fuente cambiado o desalineo). Forzar restart total.
                if (weClaimed)
                {
                    _partials.Remove(hello.DeviceId, item.RelativePath);
                    ReleasePartialClaim(partialKey);
                    _partials.Save();
                }
                _logger?.LogWarning(ex, "Fallo al publicar {File}: {Message}", item.RelativePath, ex.Message);
            }
        }

        // 9. Cierre del emisor: TRANSFER_COMPLETE (o CANCEL si abortó al final).
        var (closeType, _) = await ShareProtocol.ReadFrameAsync(stream).ConfigureAwait(false);
        if (closeType == ShareProtocol.MsgTransferCancel)
        {
            transfer.Status = TransferStatus.Cancelled;
            transfer.CompletedAt = DateTimeOffset.UtcNow;
            RaiseTransferFinished(transfer);
            return;
        }

        // 10. Confirmación de fin-a-fin: hasta este punto el "Completed" del
        // emisor no es verdad. Con fallos de publicación respondemos ERROR y
        // la transferencia queda Failed en AMBOS lados.
        if (failedItems.Count > 0)
        {
            transfer.Status = TransferStatus.Failed;
            transfer.ErrorMessage = failedItems.Count == 1
                ? $"No se pudo publicar '{failedItems[0]}' (integridad o disco)."
                : $"{failedItems.Count} archivos no se pudieron publicar (integridad o disco).";
            transfer.CompletedAt = DateTimeOffset.UtcNow;
            await ShareProtocol.WriteControlAsync(
                stream,
                ShareProtocol.MsgError,
                new ShareProtocol.ErrorPayload("publish_failed", transfer.ErrorMessage)).ConfigureAwait(false);
            RaiseTransferFinished(transfer);
            return;
        }

        await ShareProtocol.WriteControlAsync(
            stream,
            ShareProtocol.MsgTransferCompleteAck,
            new { transferId = transfer.TransferId }).ConfigureAwait(false);

        transfer.Status = TransferStatus.Completed;
        transfer.CompletedAt = DateTimeOffset.UtcNow;
        RaiseTransferFinished(transfer);
        _logger?.LogInformation(
            "Transferencia entrante {Id} completada: {Bytes} bytes",
            transfer.TransferId, receivedBytes);
    }

    /// <summary>
    /// Diálogo de emparejamiento entrante (lado receptor). Deriva el código
    /// de comparación de las dos huellas VIVAS del handshake, pide decisión
    /// al usuario vía evento (la UI muestra el código) y responde ACCEPT o
    /// REJECT. Al aceptar, registra al par con su huella viva y el puerto de
    /// servicio que anunció en el payload (el de la conexión es efímero).
    /// </summary>
    private async Task HandlePairRequestAsync(
        SslStream stream,
        string? clientThumbprint,
        string remoteIp,
        byte[] payload)
    {
        var pair = ShareProtocol.Deserialize<ShareProtocol.PairRequestPayload>(payload);
        if (pair is null || string.IsNullOrEmpty(pair.DeviceId) || clientThumbprint is null)
        {
            await ShareProtocol.WriteControlAsync(
                stream, ShareProtocol.MsgPairReject, new { }).ConfigureAwait(false);
            return;
        }

        var requestId = Guid.NewGuid().ToString("D");
        var code = PairingCode.Derive(
            _settings.CertificateThumbprint ?? _certificateStore.Thumbprint,
            clientThumbprint);

        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _pendingPairDecisions[requestId] = decision;
        }

        RaisePairRequested(new LocalSharePairRequest(
            requestId, pair.DeviceId, pair.DisplayName, code, remoteIp, pair.Port));

        _logger?.LogInformation(
            "Solicitud de emparejamiento de {Name} ({Ip}) — código {Code}",
            pair.DisplayName, remoteIp, code);

        var accepted = false;
        try
        {
            // El usuario tiene un margen limitado; la expiración cuenta como
            // rechazo limpio (el iniciador no se queda colgado esperando).
            accepted = await decision.Task.WaitAsync(PairDecisionTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            accepted = false;
        }
        finally
        {
            lock (_lock)
            {
                _pendingPairDecisions.Remove(requestId);
            }
        }

        if (!accepted)
        {
            await ShareProtocol.WriteControlAsync(
                stream, ShareProtocol.MsgPairReject, new { }).ConfigureAwait(false);
            return;
        }

        // Orden CRÍTICO: escribir el ACCEPT ANTES de registrar la confianza.
        // Si el iniciador ya se fue (canceló, cerró la app), el write falla y
        // NO registramos confianza unilateral: él nunca sabrá que fue
        // aceptado, así que ambos lados quedan igual de no-emparejados.
        try
        {
            await ShareProtocol.WriteControlAsync(
                stream, ShareProtocol.MsgPairAccept, new { }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex, "El iniciador desapareció al aceptar: no se registra la confianza");
            return;
        }

        // ACCEPT entregado: registrar al iniciador con su huella VIVA y su
        // puerto de servicio (el del payload, no el efímero de la conexión).
        RegisterPairedDevice(pair.DeviceId, pair.DisplayName, remoteIp, pair.Port, clientThumbprint);
    }

    private string GetDeviceName(string deviceId)    {
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

    /// <summary>
    /// Dirección de bind del receptor desde settings: null/vacía/inválida →
    /// todas las interfaces (comportamiento por defecto).
    /// </summary>
    private static System.Net.IPAddress? ParseBindAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return System.Net.IPAddress.TryParse(value.Trim(), out var address)
            && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? address
            : null;
    }

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
