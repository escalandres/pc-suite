using PCSuite.Contracts;
using PCSuite.Core.Operations;
using System.Diagnostics;
using System.Text.Json;

namespace PCSuite.Application.Operations;

/// <summary>
/// Operación puente "explorer.openUrl": abre una URL http/https en el
/// navegador por defecto del sistema. El host bloquea window.open
/// (NewWindowRequested marcado como Handled), así que este es el único
/// camino para abrir enlaces externos desde la UI.
/// </summary>
public sealed class ExplorerOpenUrlOperation : IOperation
{
    /// <inheritdoc />
    public string Id => OperationIds.ExplorerOpenUrl;

    /// <inheritdoc />
    public bool RequiresElevation => false;

    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Payload.HasValue)
        {
            return OperationResult.Fail("error", "El parámetro 'url' es obligatorio.");
        }

        ExplorerOpenUrlPayload? payload;
        try
        {
            payload = context.Payload.Value.Deserialize<ExplorerOpenUrlPayload>(BridgeJson.Options);
        }
        catch
        {
            return OperationResult.Fail("error", "Payload inválido.");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Url))
        {
            return OperationResult.Fail("error", "El parámetro 'url' es obligatorio.");
        }

        var raw = payload.Url.Trim();

        // Solo http/https absolutas: con UseShellExecute=true una URL con
        // otro esquema (file:, ms-settings:, etc.) podría ejecutar
        // protocolos registrados del sistema. Se restringe a navegación web.
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return OperationResult.Fail("invalid_url", "Solo se permiten URLs http/https absolutas.");
        }

        try
        {
            // ShellExecute resuelve la URL con el navegador por defecto;
            // sin argumentos compuestos no hay superficie de inyección.
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true,
            });

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("error", $"No se pudo abrir la URL: {ex.Message}");
        }
    }
}
