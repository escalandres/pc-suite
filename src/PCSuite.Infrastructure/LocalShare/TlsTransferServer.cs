using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PCSuite.Infrastructure.LocalShare;

/// <summary>
/// Listener TCP+TLS para recibir transferencias de otros PCSuite en la LAN.
/// Acepta una conexión a la vez (SemaphoreSlim), valida el certificado del
/// par contra la lista de huellas emparejadas, y delega el protocolo.
/// </summary>
public sealed class TlsTransferServer : IDisposable
{
    private readonly X509Certificate2 _serverCertificate;
    private readonly ILogger? _logger;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    public int Port { get; private set; }
    public bool IsRunning => _listener is not null;

    /// <summary>Conexión entrante lista para procesar (con SslStream autenticado).</summary>
    /// <remarks>
    /// Es Func (no Action) a propósito: el servidor espera a que el handler
    /// termine el diálogo de protocolo ANTES de disponer la conexión. Con
    /// Action el handler async void devolvería el control en el primer await
    /// y el finally destruiría el stream a mitad de protocolo.
    /// </remarks>
    public event Func<SslStream, string, Task>? PeerConnected;

    /// <summary>Huellas SHA-256 de certificados emparejados (hex minúscula). Se actualiza externamente.</summary>
    public IReadOnlySet<string> TrustedThumbprints { get; set; } = new HashSet<string>();

    public TlsTransferServer(X509Certificate2 serverCertificate, ILogger? logger = null)
    {
        _serverCertificate = serverCertificate;
        _logger = logger;
    }

    public void Start(int preferredPort)
    {
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, preferredPort);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _logger?.LogInformation("TlsTransferServer escuchando en el puerto {Port}", Port);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
        try
        {
            _acceptTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort: el accept task termina con el cancel token.
        }
        _acceptTask = null;
        _cts?.Dispose();
        _cts = null;
        _logger?.LogInformation("TlsTransferServer detenido");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
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

            _ = Task.Run(() => HandleConnectionAsync(client, ct), ct);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        var remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "desconocido";

        // Una transferencia a la vez
        await _connectionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var sslStream = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                ValidatePeerCertificate);

            var sslOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = _serverCertificate,
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            };

            await sslStream.AuthenticateAsServerAsync(sslOptions, ct).ConfigureAwait(false);

            var thumbprint = GetThumbprint(sslStream.RemoteCertificate);
            _logger?.LogInformation(
                "Conexión TLS de {Ip} (huella: {Thumbprint})",
                remoteIp, thumbprint is null ? "sin certificado" : thumbprint[..Math.Min(16, thumbprint.Length)] + "...");

            // El handler es dueño del diálogo de protocolo; no disponemos
            // la conexión hasta que termine (puede esperar decisión del usuario).
            var handler = PeerConnected;
            if (handler is not null)
            {
                await handler(sslStream, remoteIp).ConfigureAwait(false);
            }
        }
        catch (AuthenticationException ex)
        {
            _logger?.LogWarning("TLS authentication fallida desde {Ip}: {Message}", remoteIp, ex.Message);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Error en conexión de {Ip}: {Message}", remoteIp, ex.Message);
        }
        finally
        {
            _connectionGate.Release();
            client.Dispose();
        }
    }

    private bool ValidatePeerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (certificate is null)
        {
            return false;
        }

        var thumbprint = GetThumbprint(certificate);
        return thumbprint is not null
            && TrustedThumbprints.Contains(thumbprint);
    }

    private static string? GetThumbprint(X509Certificate? certificate)
    {
        if (certificate is null)
        {
            return null;
        }

        var certBytes = certificate.Export(X509ContentType.Cert);
        var hash = System.Security.Cryptography.SHA256.HashData(certBytes);
        return Convert.ToHexStringLower(hash);
    }

    public void Dispose()
    {
        Stop();
        _connectionGate.Dispose();
    }
}
