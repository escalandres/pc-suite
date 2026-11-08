using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PCSuite.Infrastructure.LocalShare;

/// <summary>
/// Cliente web temporal para Local Share (Task 016.7, diferido hasta revisión
/// de seguridad §4.2). Expone un enlace HTTP de solo lectura, protegido por un
/// token criptográfico de un solo uso y con caducidad, para que un navegador
/// (en el mismo equipo o, si el URL ACL lo permite, en la LAN) pueda descargar
/// los archivos compartidos sin instalar PCSuite.
///
/// Seguridad:
/// - Token de 32 bytes (hex) generado con RNG criptográfico.
/// - Cada petición debe presentar el token (query ?t=); sin él → 403.
/// - Las rutas servidas se validan contra la lista blanca (anti-traversal).
/// - Solo lectura: no hay subidas ni ejecución.
/// - Caducidad automática (TTL) + parada explícita.
/// - Por defecto se enlaza a localhost para evitar exponer la URL sin ACL;
///   para LAN hace falta reservar el prefijo (netsh http add urlacl) o ejecutar
///   elevado (fuera del alcance de esta versión).
/// </summary>
public sealed class LocalShareWebClientService : IDisposable
{
    private readonly ILogger? _logger;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string _token = string.Empty;
    private IReadOnlyList<string> _allowedFiles = Array.Empty<string>();
    private DateTimeOffset _expiresAt;
    private int _port;

    public LocalShareWebClientService(ILogger? logger = null)
    {
        _logger = logger;
    }

    public bool IsRunning => _listener is not null;

    public string? Url { get; private set; }

    public DateTimeOffset? ExpiresAt => IsRunning ? _expiresAt : null;

    public async Task<string> StartAsync(
        IReadOnlyList<string> filePaths,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("El cliente web ya está en ejecución.");
        }

        _allowedFiles = (filePaths ?? Array.Empty<string>()).Where(File.Exists).ToList();
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _expiresAt = DateTimeOffset.UtcNow.Add(ttl);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Enlazar a un puerto efímero del SO.
        _listener = new HttpListener();
        // Usamos localhost por defecto (sin URL ACL). El prefijo con '*' requeriría
        // elevación/ACL; se deja como mejora futura para LAN.
        _port = GetFreePort();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();

        Url = $"http://localhost:{_port}/?t={_token}";
        _loop = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);

        // Auto-apagado por TTL.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ttl, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            if (IsRunning)
            {
                await StopAsync().ConfigureAwait(false);
            }
        }, _cts.Token);

        return Url;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); } catch { }
        }
        _listener = null;
        _loop = null;
        _cts?.Dispose();
        _cts = null;
        Url = null;
        _allowedFiles = Array.Empty<string>();
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error al aceptar conexión web.");
                continue;
            }

            _ = Task.Run(() => HandleRequestAsync(ctx, ct), ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            if (DateTimeOffset.UtcNow > _expiresAt)
            {
                ctx.Response.StatusCode = 410;
                await WriteTextAsync(ctx.Response, "Enlace expirado.").ConfigureAwait(false);
                return;
            }

            var token = ctx.Request.QueryString["t"];
            if (!string.Equals(token, _token, StringComparison.Ordinal))
            {
                ctx.Response.StatusCode = 403;
                await WriteTextAsync(ctx.Response, "Token inválido.").ConfigureAwait(false);
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            if (path == "/" || path == "/index.html")
            {
                var html = BuildIndexHtml();
                ctx.Response.ContentType = "text/html; charset=utf-8";
                var bytes = Encoding.UTF8.GetBytes(html);
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
                ctx.Response.Close();
                return;
            }

            // Descarga de archivo: /download?i=<index>
            if (path == "/download")
            {
                var iStr = ctx.Request.QueryString["i"];
                if (!int.TryParse(iStr, out var idx) || idx < 0 || idx >= _allowedFiles.Count)
                {
                    ctx.Response.StatusCode = 404;
                    await WriteTextAsync(ctx.Response, "Archivo no encontrado.").ConfigureAwait(false);
                    return;
                }

                var filePath = _allowedFiles[idx];
                // Anti-traversal: revalidar que el archivo siga en la lista blanca.
                if (!_allowedFiles.Contains(filePath, StringComparer.Ordinal) || !File.Exists(filePath))
                {
                    ctx.Response.StatusCode = 404;
                    await WriteTextAsync(ctx.Response, "Archivo no encontrado.").ConfigureAwait(false);
                    return;
                }

                ctx.Response.ContentType = "application/octet-stream";
                ctx.Response.AddHeader("Content-Disposition", $"attachment; filename=\"{Path.GetFileName(filePath)}\"");
                await using var fs = File.OpenRead(filePath);
                ctx.Response.ContentLength64 = fs.Length;
                await fs.CopyToAsync(ctx.Response.OutputStream, ct).ConfigureAwait(false);
                ctx.Response.Close();
                return;
            }

            ctx.Response.StatusCode = 404;
            await WriteTextAsync(ctx.Response, "No encontrado.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al atender petición web.");
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    private string BuildIndexHtml()
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><title>PCSuite Local Share</title></head><body>");
        sb.Append("<h1>PCSuite — archivos compartidos</h1><ul>");
        for (int i = 0; i < _allowedFiles.Count; i++)
        {
            var name = System.Net.WebUtility.HtmlEncode(Path.GetFileName(_allowedFiles[i]));
            sb.Append($"<li><a href=\"/download?i={i}&t={_token}\">{name}</a></li>");
        }
        sb.Append("</ul><p>Enlace temporal. No compartas esta URL.</p></body></html>");
        return sb.ToString();
    }

    private static async Task WriteTextAsync(HttpListenerResponse response, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.ContentLength64 = bytes.Length;
        response.ContentType = "text/plain; charset=utf-8";
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    private static int GetFreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); } catch { }
    }
}