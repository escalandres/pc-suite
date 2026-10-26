using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.Security;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Application.Tests;

/// <summary>
/// security.getStatus por el puente (Task 012): contrato camelCase completo,
/// payload cero (la basura del frontend jamás dicta lo que se lee), campos
/// sin datos AUSENTES del JSON (nunca false ni null literal), fallo
/// inesperado con mensaje genérico sin stack traces y RequiresElevation
/// falso (§14, Security Center de solo lectura).
/// </summary>
public class SecurityOperationTests
{
    private sealed class StubService(SecuritySnapshot snapshot) : ISecurityService
    {
        public Task<SecuritySnapshot> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class ThrowingService : ISecurityService
    {
        public Task<SecuritySnapshot> GetStatusAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }

    private static SecuritySnapshot FullSnapshot() => new(
        Defender: new DefenderInfo(
            SecurityStatuses.Good,
            true,
            true,
            DateTimeOffset.Parse("2026-09-20T00:00:00+00:00")),
        Firewall: new FirewallInfo(SecurityStatuses.Good, true, true, false),
        Antivirus: new AntivirusInfo(SecurityStatuses.Good, ["Windows Defender"]),
        Ports:
        [
            new ListeningPort("TCP", "0.0.0.0", 445, 4, "System"),
            new ListeningPort("UDP", "::", 5353, null, null),
        ],
        Diagnostics:
        [
            new SecurityCheck("uac", "Control de cuentas de usuario (UAC)", SecurityStatuses.Ok, "Activado."),
            new SecurityCheck("rdp", "Escritorio remoto (RDP)", SecurityStatuses.Warn, null),
        ],
        Connections: [],
        UdpEndpoints: [],
        Audit: new SecurityAudit([], [], 0),
        Discovery: new SecurityDiscovery([], [], [], []),
        ScannedAt: DateTimeOffset.Parse("2026-09-28T10:00:00+00:00"));

    private static SecuritySnapshot UnknownSnapshot() => new(
        Defender: new DefenderInfo(SecurityStatuses.Unknown, null, null, null),
        Firewall: new FirewallInfo(SecurityStatuses.Unknown, null, null, null),
        Antivirus: new AntivirusInfo(SecurityStatuses.Unknown, []),
        Ports: [],
        Connections: [],
        UdpEndpoints: [],
        Audit: new SecurityAudit([], [], 0),
        Discovery: new SecurityDiscovery([], [], [], []),
        Diagnostics: [new SecurityCheck("uac", "Control de cuentas de usuario (UAC)", SecurityStatuses.Unknown, null)],
        ScannedAt: DateTimeOffset.Parse("2026-09-28T10:00:00+00:00"));

    private static CommandRouter Router(ISecurityService service) =>
        new(
            new IOperation[] { new SecurityGetStatusOperation(service) },
            NullLogger<CommandRouter>.Instance);

    // ------------------------------------------------------------------
    // Contrato camelCase
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetStatus_ReturnsFullSnapshot_CamelCase()
    {
        var response = await Send(Router(new StubService(FullSnapshot())), "s1", OperationIds.SecurityGetStatus);

        Assert.True(response.Success);
        var result = response.Result!.Value;

        var defender = result.GetProperty("defender");
        Assert.Equal("good", defender.GetProperty("status").GetString());
        Assert.True(defender.GetProperty("antivirusEnabled").GetBoolean());
        Assert.True(defender.GetProperty("realTimeProtection").GetBoolean());
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-20T00:00:00+00:00"),
            defender.GetProperty("signatureLastUpdated").GetDateTimeOffset());

        var firewall = result.GetProperty("firewall");
        Assert.Equal("good", firewall.GetProperty("status").GetString());
        Assert.True(firewall.GetProperty("domainEnabled").GetBoolean());
        Assert.False(firewall.GetProperty("publicEnabled").GetBoolean());

        Assert.Equal("good", result.GetProperty("antivirus").GetProperty("status").GetString());
        Assert.Equal(
            ["Windows Defender"],
            result.GetProperty("antivirus").GetProperty("products").EnumerateArray().Select(p => p.GetString()));

        var tcp = result.GetProperty("ports")[0];
        Assert.Equal("TCP", tcp.GetProperty("protocol").GetString());
        Assert.Equal("0.0.0.0", tcp.GetProperty("localAddress").GetString());
        Assert.Equal(445, tcp.GetProperty("localPort").GetInt32());
        Assert.Equal(4, tcp.GetProperty("pid").GetInt32());
        Assert.Equal("System", tcp.GetProperty("process").GetString());

        var diagnostics = result.GetProperty("diagnostics");
        Assert.Equal(2, diagnostics.GetArrayLength());
        Assert.Equal("uac", diagnostics[0].GetProperty("id").GetString());
        Assert.Equal("Control de cuentas de usuario (UAC)", diagnostics[0].GetProperty("label").GetString());
        Assert.Equal("warn", diagnostics[1].GetProperty("status").GetString());

        Assert.Equal(
            DateTimeOffset.Parse("2026-09-28T10:00:00+00:00"),
            result.GetProperty("scannedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task GetStatus_WithoutData_KeepsNullFieldsOffTheWire()
    {
        // Campo sin datos = ausente en el JSON (WhenWritingNull); jamás false
        // ni null literal — la UI distingue "sin datos" de "desactivado".
        var response = await Send(Router(new StubService(UnknownSnapshot())), "s2", OperationIds.SecurityGetStatus);

        Assert.True(response.Success);
        var result = response.Result!.Value;

        Assert.Equal("unknown", result.GetProperty("defender").GetProperty("status").GetString());
        Assert.False(result.GetProperty("defender").TryGetProperty("antivirusEnabled", out _));
        Assert.False(result.GetProperty("defender").TryGetProperty("realTimeProtection", out _));
        Assert.False(result.GetProperty("defender").TryGetProperty("signatureLastUpdated", out _));
        Assert.False(result.GetProperty("firewall").TryGetProperty("domainEnabled", out _));
        Assert.Empty(result.GetProperty("antivirus").GetProperty("products").EnumerateArray());
        Assert.Empty(result.GetProperty("ports").EnumerateArray());

        var check = Assert.Single(result.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("unknown", check.GetProperty("status").GetString());
        Assert.False(check.TryGetProperty("detail", out _));
    }

    // ------------------------------------------------------------------
    // Payload cero: la basura del frontend nunca dicta lo que se lee
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetStatus_JunkPayload_IsIgnored_CannotInjectState()
    {
        var response = await Send(
            Router(new StubService(FullSnapshot())),
            "s3",
            OperationIds.SecurityGetStatus,
            """{"status":"poor","ports":[],"defender":{"status":"poor"}}""");

        Assert.True(response.Success);
        var result = response.Result!.Value;

        // El estado sale SIEMPRE del sondeo nativo, jamás del payload.
        Assert.Equal("good", result.GetProperty("defender").GetProperty("status").GetString());
        Assert.Equal(2, result.GetProperty("ports").GetArrayLength());
    }

    [Fact]
    public async Task UnknownSecurityOperation_IsOperationNotFound()
    {
        // §14: no existe ninguna acción de modificación en esta task.
        var response = await Send(Router(new StubService(FullSnapshot())), "x1", "security.setStatus");

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.OperationNotFound, response.Error!.Code);
    }

    // ------------------------------------------------------------------
    // Fallo inesperado: genérico, sin stack traces ni texto de la excepción
    // ------------------------------------------------------------------

    [Fact]
    public async Task ThrowingService_YieldsGenericError_WithoutStackTrace()
    {
        var response = await Send(Router(new ThrowingService()), "e1", OperationIds.SecurityGetStatus);

        Assert.False(response.Success);
        Assert.DoesNotContain("   at ", response.Error!.Message);
        Assert.DoesNotContain("boom", response.Error.Message);
        Assert.DoesNotContain("System.", response.Error.Message);
    }

    // ------------------------------------------------------------------
    // Metadato de elevación
    // ------------------------------------------------------------------

    [Fact]
    public void GetStatus_DoesNotRequireElevation()
    {
        var operation = new SecurityGetStatusOperation(new StubService(FullSnapshot()));

        Assert.False(operation.RequiresElevation);
        Assert.Equal(OperationIds.SecurityGetStatus, operation.Id);
    }

    private static Task<BridgeResponse> Send(
        CommandRouter router,
        string id,
        string operation,
        string? payloadJson = null)
    {
        var payload = payloadJson is null ? string.Empty : $",\"payload\":{payloadJson}";
        return router.RouteAsync(
            $"{{\"id\":\"{id}\",\"operation\":\"{operation}\"{payload}}}");
    }
}
