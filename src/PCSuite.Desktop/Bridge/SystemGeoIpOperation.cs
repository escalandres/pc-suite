using System.Net.Http;
using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Desktop.Bridge;

/// <summary>
/// system.geoIp — consulta el país de una IP remota (Geo IP) desde el backend,
/// evitando las restricciones de CSP/CORS del frontend.
/// Usa ipapi.co (HTTPS) como proveedor.
/// </summary>
public sealed class SystemGeoIpOperation : IOperation
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public string Id => OperationIds.SystemGeoIp;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct = default)
    {
        string ip = string.Empty;
        if (context.Payload.HasValue && context.Payload.Value.TryGetProperty("ip", out var p))
            ip = p.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(ip))
            return OperationResult.Fail("error", "IP vacía.");

        // Validar que sea una IP (básico).
        if (!System.Net.IPAddress.TryParse(ip, out _))
            return OperationResult.Fail("error", "IP inválida.");

        try
        {
            using var resp = await Http.GetAsync($"https://ipapi.co/{ip}/country/", ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            string country = body.Trim();
            // ipapi.co devuelve el código de país como texto plano (ej. "US").
            // Si hay error, devuelve JSON con {"error":true,...}.
            if (country.StartsWith("{"))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("error", out var err) && err.GetBoolean())
                    {
                        var reason = doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : "unknown";
                        return OperationResult.Fail("error", $"ipapi.co error: {reason}");
                    }
                    if (doc.RootElement.TryGetProperty("country", out var c))
                        country = c.GetString() ?? string.Empty;
                    else if (doc.RootElement.TryGetProperty("country_code", out var cc))
                        country = cc.GetString() ?? string.Empty;
                }
                catch { /* keep raw */ }
            }

country = country.Trim();
            if (!resp.IsSuccessStatusCode || country.Length == 0 || country.Length > 3)
            {
                return OperationResult.Fail("error", $"HTTP {resp.StatusCode}: {body}");
            }

            var json = System.Text.Json.JsonSerializer.Serialize(new { country }, BridgeJson.Options);
            return OperationResult.Ok(System.Text.Json.JsonDocument.Parse(json).RootElement);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("error", ex.Message);
        }
    }
}