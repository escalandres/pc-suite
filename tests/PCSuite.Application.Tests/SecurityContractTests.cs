using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Security;

namespace PCSuite.Application.Tests;

// Task 012 / parte 12.1: contrato security.* — vocabulario fijo de estados,
// snapshot completo en camelCase (nunca PascalCase) y nulls conservados
// (null = "sin datos", distinto de false en la wire).
public class SecurityContractTests
{
    [Fact]
    public void Statuses_AreTheFixedVocabulary()
    {
        Assert.Equal("good", SecurityStatuses.Good);
        Assert.Equal("notMonitored", SecurityStatuses.NotMonitored);
        Assert.Equal("poor", SecurityStatuses.Poor);
        Assert.Equal("snoozed", SecurityStatuses.Snoozed);
        Assert.Equal("unknown", SecurityStatuses.Unknown);
        Assert.Equal("ok", SecurityStatuses.Ok);
        Assert.Equal("warn", SecurityStatuses.Warn);
    }

    [Fact]
    public void OperationId_IsSecurityGetStatus()
    {
        Assert.Equal("security.getStatus", OperationIds.SecurityGetStatus);
    }

    [Fact]
    public void Snapshot_UsesCamelCaseWireFormat_AllSections()
    {
        var json = JsonSerializer.Serialize(
            new SecurityStatusResult
            {
                Defender = new DefenderStatusResult
                {
                    Status = "good",
                    AntivirusEnabled = true,
                    RealTimeProtection = true,
                    SignatureLastUpdated = DateTimeOffset.Parse("2026-09-28T08:00:00+00:00"),
                },
                Firewall = new FirewallStatusResult
                {
                    Status = "good",
                    DomainEnabled = true,
                    PrivateEnabled = true,
                    PublicEnabled = false,
                },
                Antivirus = new AntivirusStatusResult
                {
                    Status = "good",
                    Products = ["Windows Defender"],
                },
                Ports =
                [
                    new PortResult
                    {
                        Protocol = "TCP",
                        LocalAddress = "0.0.0.0",
                        LocalPort = 135,
                        Pid = 1234,
                        Process = "svchost",
                    },
                ],
                Diagnostics =
                [
                    new SecurityCheckResult
                    {
                        Id = "uac",
                        Label = "Control de cuentas de usuario (UAC)",
                        Status = "ok",
                        Detail = null,
                    },
                ],
                ScannedAt = DateTimeOffset.Parse("2026-09-28T10:00:00+00:00"),
            },
            BridgeJson.Options);

        Assert.Contains("\"defender\":", json);
        Assert.Contains("\"firewall\":", json);
        Assert.Contains("\"antivirus\":", json);
        Assert.Contains("\"ports\":", json);
        Assert.Contains("\"diagnostics\":", json);
        Assert.Contains("\"scannedAt\":", json);

        Assert.Contains("\"realTimeProtection\":true", json);
        Assert.Contains("\"signatureLastUpdated\":", json);
        Assert.Contains("\"domainEnabled\":true", json);
        Assert.Contains("\"publicEnabled\":false", json);
        Assert.Contains("\"localPort\":135", json);

        Assert.DoesNotContain("RealTimeProtection", json);
        Assert.DoesNotContain("LocalPort", json);
        Assert.DoesNotContain("ScannedAt", json);
    }

    [Fact]
    public void Snapshot_NullsAreOmitted_NotCoercedToFalse()
    {
        var json = JsonSerializer.Serialize(
            new SecurityStatusResult
            {
                Defender = new DefenderStatusResult
                {
                    Status = "unknown",
                    RealTimeProtection = null,
                },
                Firewall = new FirewallStatusResult
                {
                    Status = "unknown",
                    DomainEnabled = null,
                },
            },
            BridgeJson.Options);

        // null = "no se pudo leer" → campo AUSENTE en la wire (WhenWritingNull);
        // jamás se convierte en false ni aparece como literal null.
        Assert.DoesNotContain("realTimeProtection", json);
        Assert.DoesNotContain("domainEnabled", json);
        Assert.DoesNotContain("\"realTimeProtection\":false", json);
        Assert.DoesNotContain("\"domainEnabled\":false", json);
    }

    [Fact]
    public void EmptySnapshot_SerializesEmptyCollectionsNotNulls()
    {
        var json = JsonSerializer.Serialize(
            new SecurityStatusResult
            {
                Antivirus = new AntivirusStatusResult(),
                Ports = [],
                Diagnostics = [],
            },
            BridgeJson.Options);

        Assert.Contains("\"products\":[]", json);
        Assert.Contains("\"ports\":[]", json);
        Assert.Contains("\"diagnostics\":[]", json);
        Assert.DoesNotContain("\"ports\":null", json);
    }
}
