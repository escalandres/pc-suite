using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PCSuite.Application;
using PCSuite.Contracts;
using PCSuite.Core.Events;

namespace PCSuite.Desktop.Bridge;

/// <summary>
/// Typed WebView2 message bus: receives frontend requests, answers with
/// structured responses and pushes events. The frontend can only reach
/// allow-listed operations through <see cref="CommandRouter"/>.
/// </summary>
public sealed class WebViewBridge : IEventPublisher
{
    private readonly CommandRouter _router;
    private readonly ILogger<WebViewBridge> _logger;
    private readonly List<string> _allowedOrigins = [];
    private CoreWebView2? _core;

    /// <summary>
    /// SynchronizationContext del hilo UI, capturado en Attach. CoreWebView2
    /// SOLO acepta ese hilo: los eventos llegan desde hilos de fondo (handler
    /// TLS del Local Share, listener UDP del discovery, bombeo de stdout de
    /// ClamAV) y hay que encolar el post. Sin esto, TODO evento disparado
    /// desde un hilo que no sea el UI muere en
    /// "InvalidOperationException: CoreWebView2 members can only be
    /// accessed from the UI thread".
    /// </summary>
    private SynchronizationContext? _uiContext;

    public WebViewBridge(CommandRouter router, ILogger<WebViewBridge> logger)
    {
        _router = router;
        _logger = logger;
    }

    public void Attach(CoreWebView2 core, IReadOnlyList<string> allowedOrigins)
    {
        _core = core;
        _uiContext = SynchronizationContext.Current;
        _allowedOrigins.Clear();
        _allowedOrigins.AddRange(allowedOrigins);
        core.WebMessageReceived += OnWebMessageReceived;
    }

    public void SendEvent<TPayload>(string eventId, TPayload payload)
    {
        var core = _core;
        if (core is null)
        {
            return;
        }

        // Serializar SIEMPRE en el hilo del llamador: el resultado es un
        // string plano, seguro de cruzar al hilo UI.
        var json = BridgeJson.SerializeEvent(new BridgeEvent
        {
            Event = eventId,
            Payload = BridgeJson.ToJson(payload),
        });

        var uiContext = _uiContext;
        if (uiContext is not null && SynchronizationContext.Current != uiContext)
        {
            // Hilo de fondo: encolar el post al hilo UI. Post es fire-and-
            // forget — el emisor nunca se bloquea esperando a la UI.
            uiContext.Post(
                _ =>
                {
                    try
                    {
                        core.PostWebMessageAsJson(json);
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Failed to send event {Event}.", eventId);
                    }
                },
                null);
            return;
        }

        try
        {
            core.PostWebMessageAsJson(json);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to send event {Event}.", eventId);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var source = e.Source;
        if (!_allowedOrigins.Any(origin => BridgeOriginPolicy.Matches(source, origin)))
        {
            _logger.LogWarning("Blocked bridge message from untrusted source {Source}", source);
            return;
        }

        _ = ProcessAsync(e.WebMessageAsJson);
    }

    private async Task ProcessAsync(string rawMessage)
    {
        try
        {
            var response = await _router.RouteAsync(rawMessage);
            _core?.PostWebMessageAsJson(BridgeJson.SerializeResponse(response));
        }
        catch (Exception exception)
        {
            // Routing never throws by design; this is a last-resort guard.
            _logger.LogError(exception, "Failed to process bridge message.");
        }
    }
}
