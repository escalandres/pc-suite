using System.Net;
using System.Net.NetworkInformation;
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

    /// <summary>
    /// SIO_UDP_CONNRESET (0x9800000C): desactiva que un ICMP "port
    /// unreachable" provocado por un datagrama propio lance
    /// SocketException(10054) en el siguiente Receive del socket UDP.
    /// </summary>
    private const int SioUdpConnreset = unchecked((int)0x9800000C);

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

        // Clásico de Windows: sin SIO_UDP_CONNRESET=0, un datagrama que
        // provoque un ICMP "port unreachable" hace que el SIGUIENTE
        // Receive lance SocketException(10054). En máquinas con VPN/WSL/
        // Hyper-V pasa más de lo que parece — y con el bucle antiguo
        // mataba la escucha para siempre.
        try
        {
            _client.Client.IOControl(
                SioUdpConnreset,
                [.. BitConverter.GetBytes(0)],
                null);
        }
        catch
        {
            // Best-effort: si la plataforma no lo soporta, el bucle
            // resiliente de abajo cubre el hueco.
        }

        _client.Client.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));

        // Interfaz de SALIDA de los anuncios: la que tiene la pasarela por
        // defecto (la red REAL). El SO escoge por métrica de interfaz y los
        // adaptadores virtuales (VirtualBox/WSL/Hyper-V) suelen ganarle a la
        // Wi-Fi: sin esto, los anuncios salen por una red que no llega a
        // ningún sitio y el otro equipo jamás los recibe.
        IPAddress? egress = SelectEgressInterface();
        if (egress is not null)
        {
            try
            {
                _client.Client.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.MulticastInterface,
                    BitConverter.ToInt32(egress.GetAddressBytes(), 0));
                _logger?.LogInformation("Discovery: anuncios salen por {Address}", egress);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "No se pudo fijar la interfaz de salida del discovery");
            }
        }

        // Unirse al grupo en TODAS las interfaces IPv4 reales. El join
        // "por defecto" (una sola interfaz elegida por el SO) falla en
        // máquinas multitarjeta: si el SO escoge la VPN o un adaptador
        // virtual, los anuncios que llegan por la Wi-Fi/Ethernet jamás
        // entran al socket.
        var group = IPAddress.Parse(MulticastGroup);
        var joinedInterfaces = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                || !nic.SupportsMulticast)
            {
                continue;
            }

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork
                    || IPAddress.IsLoopback(addr.Address)
                    || addr.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    _client.Client.SetSocketOption(
                        SocketOptionLevel.IP,
                        SocketOptionName.AddMembership,
                        new MulticastOption(group, addr.Address));
                    joinedInterfaces++;
                }
                catch
                {
                    // Adaptadores virtuales que no aceptan el join: ignorar.
                }
            }
        }

        _announceTask = Task.Run(() => AnnounceLoopAsync(_cts.Token));
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        _logger?.LogInformation(
            "Discovery activo en {Group}:{Port} (unido en {Count} interfaces)",
            MulticastGroup, MulticastPort, joinedInterfaces);
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
        var consecutiveErrors = 0;
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _client!.ReceiveAsync(ct).ConfigureAwait(false);
                consecutiveErrors = 0;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                // NUNCA romper el bucle: un error transitorio (flap de
                // interfaz, VPN conectando, ICMP retardado) solo puede
                // costarnos UN anuncio. Antes, un SocketException dejaba
                // el discovery SORDO PARA SIEMPRE hasta reiniciar el
                // servicio — indistinguible de "no hay nadie en la red".
                consecutiveErrors++;
                _logger?.LogDebug(
                    ex,
                    "Receive de discovery falló ({Count} consecutivos)",
                    consecutiveErrors);

                try
                {
                    await Task.Delay(
                        Math.Min(consecutiveErrors * 200, 2000), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            _ = Task.Run(() => ProcessAnnouncement(result), CancellationToken.None);
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

    /// <summary>
    /// Interfaz IPv4 de salida para los anuncios: la de la red REAL, la que
    /// tiene pasarela configurada (VirtualBox/WSL/Hyper-V no tienen: son
    /// redes virtuales aisladas). Fallback: primera interfaz válida.
    /// </summary>
    private static IPAddress? SelectEgressInterface()
    {
        IPAddress? fallback = null;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                || !nic.SupportsMulticast)
            {
                continue;
            }

            var props = nic.GetIPProperties();
            var hasGateway = props.GatewayAddresses.Count > 0;

            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork
                    || IPAddress.IsLoopback(addr.Address)
                    || addr.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (hasGateway)
                {
                    return addr.Address;
                }

                fallback ??= addr.Address;
            }
        }

        return fallback;
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
