using System.Net.Http;
using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Desktop.Bridge;

/// <summary>
/// system.geoIp — consulta el país de una IP remota (Geo IP) desde el backend,
/// evitando las restricciones de CSP/CORS del frontend.
/// Usa api.country.is (HTTPS, CORS-friendly) como proveedor.
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
            using var resp = await Http.GetAsync($"https://api.country.is/{ip}", ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            string country = string.Empty;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("country", out var c))
                    country = c.GetString() ?? string.Empty;
            }
            catch
            {
                country = body.Trim();
            }

            country = country.Trim();
            if (!resp.IsSuccessStatusCode || country.Length > 3)
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