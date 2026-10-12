using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PCSuite.Infrastructure.LocalShare;

/// <summary>
/// Descubrimiento de dispositivos PCSuite en la LAN vía UDP multicast.
/// Anuncia esta instancia periódicamente y escucha anuncios de otras.
/// No usa mDNS/DNS-SD completo (RFC 6762) — usa un protocolo JSON simple
/// sobre el grupo multicast 239.255.67.85:5354 (grupo privado, no colisiona
/// con mDNS estándar en 224.0.0.251:5353).
///
/// Wire format: JSON en UTF-8, un datagrama por anuncio:
/// { "deviceId": "...", "displayName": "...", "port": 51980, "thumbprint": "..." }
/// </summary>
public sealed class DiscoveryService : IDisposable
{
    /// <summary>Grupo multicast privado para PCSuite Local Share.</summary>
    public const string MulticastGroup = "239.255.67.85";
    public const int MulticastPort = 5354;

    /// <summary>Intervalo entre anuncios (RFC 6762 sugiere 30s; usamos 15s para respuesta rápida).</summary>
    private static readonly TimeSpan AnnounceInterval = TimeSpan.FromSeconds(15);

    private readonly ILogger? _logger;
    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _announceTask;
    private Task? _listenTask;
    private bool _disposed;

    // Anuncio de ESTA instancia
    private readonly string _deviceId;
    private readonly string _displayName;
    private readonly int _servicePort;
    private readonly string _thumbprint;

    /// <summary>
    /// Se dispara al recibir un anuncio válido de otro PCSuite.
    /// Args: (deviceId, displayName, address, port, thumbprint)
    /// </summary>
    public event Action<string, string, string, int, string>? DeviceAnnounced;

    public DiscoveryService(
        string deviceId,
        string displayName,
        int servicePort,
        string thumbprint,
        ILogger? logger = null)
    {
        _deviceId = deviceId;
        _displayName = displayName;
        _servicePort = servicePort;
        _thumbprint = thumbprint;
        _logger = logger;
    }

    public bool IsRunning => _client is not null;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _client = new UdpClient();
        _client.Client.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress,
            true);
        _client.Client.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));
        _client.JoinMulticastGroup(IPAddress.Parse(MulticastGroup));

        _announceTask = Task.Run(() => AnnounceLoopAsync(_cts.Token));
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        _logger?.LogInformation("Discovery activo en {Group}:{Port}", MulticastGroup, MulticastPort);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _client?.DropMulticastGroup(IPAddress.Parse(MulticastGroup)); }
        catch { /* best-effort */ }
        _client?.Dispose();
        _client = null;
        _logger?.LogInformation("Discovery detenido");
    }

    // ------------------------------------------------------------------
    // Anunciar (emisor)
    // ------------------------------------------------------------------

    private async Task AnnounceLoopAsync(CancellationToken ct)
    {
        // Enviar el primer anuncio inmediatamente
        await SendAnnouncementAsync(ct).ConfigureAwait(false);

        using var timer = new PeriodicTimer(AnnounceInterval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            await SendAnnouncementAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task SendAnnouncementAsync(CancellationToken ct)
    {
        try
        {
            var announcement = new DiscoveryAnnouncement
            {
                DeviceId = _deviceId,
                DisplayName = _displayName,
                Port = _servicePort,
                Thumbprint = _thumbprint,
            };
            var json = JsonSerializer.SerializeToUtf8Bytes(announcement);
            var endpoint = new IPEndPoint(IPAddress.Parse(MulticastGroup), MulticastPort);
            await _client!.SendAsync(json, endpoint, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogDebug(ex, "Error enviando anuncio de discovery");
        }
    }

    // ------------------------------------------------------------------
    // Escuchar (receptor)
    // ------------------------------------------------------------------

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _client!.ReceiveAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => ProcessAnnouncement(result), ct);
        }
    }

    private void ProcessAnnouncement(UdpReceiveResult result)
    {
        try
        {
            // Ignorar nuestros propios anuncios
            var senderIp = result.RemoteEndPoint.Address.ToString();
            if (senderIp is "0.0.0.0" or "::" || IsLocalAddress(senderIp))
            {
                return;
            }

            var announcement = JsonSerializer.Deserialize<DiscoveryAnnouncement>(
                result.Buffer,
                LocalShareJson.Options);
            if (announcement is null
                || string.IsNullOrEmpty(announcement.DeviceId)
                || announcement.Port <= 0)
            {
                return;
            }

            _logger?.LogDebug(
                "Discovery: {Device} ({Id}) en {Ip}:{Port}",
                announcement.DisplayName, announcement.DeviceId[..8] + "...",
                senderIp, announcement.Port);

            DeviceAnnounced?.Invoke(
                announcement.DeviceId,
                announcement.DisplayName,
                senderIp,
                announcement.Port,
                announcement.Thumbprint ?? string.Empty);
        }
        catch (JsonException)
        {
            // No es un anuncio PCSuite: ignorar silenciosamente.
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error procesando datagrama de discovery");
        }
    }

    private static bool IsLocalAddress(string ip)
    {
        try
        {
            var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
            return hostEntry.AddressList
                .Any(a => a.ToString() == ip);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts?.Dispose();
    }

    /// <summary>Payload del anuncio de discovery.</summary>
    private sealed record DiscoveryAnnouncement
    {
        public string DeviceId { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public int Port { get; init; }
        public string? Thumbprint { get; init; }
    }
}
