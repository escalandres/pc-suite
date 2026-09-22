using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PCSuite.Application;
using PCSuite.Contracts;

namespace PCSuite.Desktop.Bridge;

/// <summary>
/// Typed WebView2 message bus: receives frontend requests, answers with
/// structured responses and pushes events. The frontend can only reach
/// allow-listed operations through <see cref="CommandRouter"/>.
/// </summary>
public sealed class WebViewBridge
{
    private readonly CommandRouter _router;
    private readonly ILogger<WebViewBridge> _logger;
    private readonly List<string> _allowedOrigins = [];
    private CoreWebView2? _core;

    public WebViewBridge(CommandRouter router, ILogger<WebViewBridge> logger)
    {
        _router = router;
        _logger = logger;
    }

    public void Attach(CoreWebView2 core, IReadOnlyList<string> allowedOrigins)
    {
        _core = core;
        _allowedOrigins.Clear();
        _allowedOrigins.AddRange(allowedOrigins);
        core.WebMessageReceived += OnWebMessageReceived;
    }

    public void SendEvent<TPayload>(string eventId, TPayload payload)
    {
        if (_core is null)
        {
            return;
        }

        try
        {
            var bridgeEvent = new BridgeEvent { Event = eventId, Payload = BridgeJson.ToJson(payload) };
            _core.PostWebMessageAsJson(BridgeJson.SerializeEvent(bridgeEvent));
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
