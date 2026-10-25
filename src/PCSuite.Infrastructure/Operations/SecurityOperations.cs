using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.Security;

namespace PCSuite.Infrastructure.Operations;

// Task 012 (parte 12.3): security.getStatus, la única operación de la task —
// payload cero (lo que llegue se ignora: jamás influye en qué se lee), solo
// lectura y sin elevación (§14). Traduce el snapshot de Core al DTO
// camelCase del contrato; un fallo inesperado lo captura el router con
// mensaje genérico (nunca stack traces hacia la UI).

public sealed class SecurityGetStatusOperation : IOperation
{
    private readonly ISecurityService _service;

    public SecurityGetStatusOperation(ISecurityService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    public string Id => OperationIds.SecurityGetStatus;

    /// <summary>
    /// §14: Security Center de solo lectura — jamás eleva ni toca ajustes de
    /// seguridad; ninguna acción de modificación existe en esta task.
    /// </summary>
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        var snapshot = await _service.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(ToResult(snapshot));
    }

    private static JsonElement? ToResult(SecuritySnapshot snapshot) =>
        BridgeJson.ToJson(new SecurityStatusResult
        {
            Defender = new DefenderStatusResult
            {
                Status = snapshot.Defender.Status,
                AntivirusEnabled = snapshot.Defender.AntivirusEnabled,
                RealTimeProtection = snapshot.Defender.RealTimeProtection,
                SignatureLastUpdated = snapshot.Defender.SignatureLastUpdated,
                SignatureNote = snapshot.Defender.SignatureNote,
            },
            Firewall = new FirewallStatusResult
            {
                Status = snapshot.Firewall.Status,
                DomainEnabled = snapshot.Firewall.DomainEnabled,
                PrivateEnabled = snapshot.Firewall.PrivateEnabled,
                PublicEnabled = snapshot.Firewall.PublicEnabled,
            },
            Antivirus = new AntivirusStatusResult
            {
                Status = snapshot.Antivirus.Status,
                Products = snapshot.Antivirus.Products,
            },
            Ports = snapshot.Ports
                .Select(port => new PortResult
                {
                    Protocol = port.Protocol,
                    LocalAddress = port.LocalAddress,
                    LocalPort = port.LocalPort,
                    Pid = port.Pid,
                    Process = port.Process,
                    ProcessDetail = ToProcessResult(port.ProcessDetail),
                })
                .ToList(),
            Connections = snapshot.Connections
                .Select(connection => new ActiveConnectionResult
                {
                    LocalAddress = connection.LocalAddress,
                    LocalPort = connection.LocalPort,
                    RemoteAddress = connection.RemoteAddress,
                    RemotePort = connection.RemotePort,
                    Pid = connection.Pid,
                    Process = ToProcessResult(connection.Process),
                    ReviewNote = connection.ReviewNote,
                })
                .ToList(),
            UdpEndpoints = snapshot.UdpEndpoints
                .Select(endpoint => new UdpEndpointResult
                {
                    LocalAddress = endpoint.LocalAddress,
                    LocalPort = endpoint.LocalPort,
                    Pid = endpoint.Pid,
                    Process = ToProcessResult(endpoint.Process),
                    ReviewNote = endpoint.ReviewNote,
                })
                .ToList(),
            Diagnostics = snapshot.Diagnostics
                .Select(check => new SecurityCheckResult
                {
                    Id = check.Id,
                    Label = check.Label,
                    Status = check.Status,
                    Detail = check.Detail,
                })
                .ToList(),
            ScannedAt = snapshot.ScannedAt,
        });

    private static PortProcessResult? ToProcessResult(PortProcessInfo? process) =>
        process is null
            ? null
            : new PortProcessResult
            {
                Name = process.Name,
                ExecutablePath = process.ExecutablePath,
                Signer = process.Signer,
            };
}
