using PCSuite.Core.Security;
using PCSuite.Infrastructure.Security;

namespace PCSuite.Application.Tests;

// Task 012 / parte 12.2: SecurityService compone la lectura cruda del probe
// (falso, inyectado) en el snapshot del contrato: semáforos por fuente,
// diagnósticos (UAC/RDP/protección/firmas), puertos con orden fijo y
// degradación a "unknown" sin excepciones aunque el probe falle.
public class SecurityServiceTests
{
    private sealed class FakeProbe(RawSecurityReading raw) : ISecurityProbe
    {
        public RawSecurityReading ReadRaw() => raw;
    }

    private sealed class ThrowingProbe : ISecurityProbe
    {
        public RawSecurityReading ReadRaw() => throw new InvalidOperationException("boom");
    }

    private static RawSecurityReading FullReading() => new(
        FirewallHealth: SecurityStatuses.Good,
        AntivirusHealth: SecurityStatuses.Good,
        Defender: new DefenderRaw(true, true, DateTimeOffset.UtcNow.AddDays(-1)),
        AntivirusProducts: ["Windows Defender"],
        Firewall: new FirewallRaw(true, true, false),
        System: new SystemRaw(UacEnabled: true, RdpConnectionsDenied: true),
        Ports:
        [
            new ListeningPort("UDP", "0.0.0.0", 5353, null, null),
            new ListeningPort("TCP", "0.0.0.0", 443, 123, "app"),
            new ListeningPort("TCP", "0.0.0.0", 135, 4, "svchost"),
        ]);

    private static SecurityService Service(RawSecurityReading raw) => new(new FakeProbe(raw));

    [Fact]
    public async Task GetStatus_MapsFullReadingToSnapshot()
    {
        var snapshot = await Service(FullReading()).GetStatusAsync();

        Assert.Equal(SecurityStatuses.Good, snapshot.Defender.Status);
        Assert.True(snapshot.Defender.AntivirusEnabled);
        Assert.True(snapshot.Defender.RealTimeProtection);
        Assert.NotNull(snapshot.Defender.SignatureLastUpdated);

        Assert.Equal(SecurityStatuses.Good, snapshot.Firewall.Status);
        Assert.True(snapshot.Firewall.DomainEnabled);
        Assert.True(snapshot.Firewall.PrivateEnabled);
        Assert.False(snapshot.Firewall.PublicEnabled);

        Assert.Equal(SecurityStatuses.Good, snapshot.Antivirus.Status);
        Assert.Equal(["Windows Defender"], snapshot.Antivirus.Products);

        // Con la lectura completa todos los checks quedan en verde.
        Assert.Equal(["uac", "rdp", "realtime", "signatures"], snapshot.Diagnostics.Select(c => c.Id));
        Assert.All(snapshot.Diagnostics, c => Assert.Equal(SecurityStatuses.Ok, c.Status));
    }

    [Fact]
    public async Task GetStatus_NullSectionsBecomeUnknown_NotErrors()
    {
        var reading = new RawSecurityReading(
            FirewallHealth: null,
            AntivirusHealth: null,
            Defender: null,
            AntivirusProducts: null,
            Firewall: new FirewallRaw(null, null, null),
            System: new SystemRaw(null, null),
            Ports: []);

        var snapshot = await Service(reading).GetStatusAsync();

        Assert.Equal(SecurityStatuses.Unknown, snapshot.Defender.Status);
        Assert.Null(snapshot.Defender.RealTimeProtection);
        Assert.Equal(SecurityStatuses.Unknown, snapshot.Firewall.Status);
        Assert.Null(snapshot.Firewall.DomainEnabled);
        Assert.Equal(SecurityStatuses.Unknown, snapshot.Antivirus.Status);
        Assert.Empty(snapshot.Antivirus.Products);
        Assert.Empty(snapshot.Ports);
        Assert.Equal(4, snapshot.Diagnostics.Count);
        Assert.All(snapshot.Diagnostics, c => Assert.Equal(SecurityStatuses.Unknown, c.Status));
    }

    [Fact]
    public async Task GetStatus_ThrowingProbe_YieldsUnknownSnapshotInsteadOfFailing()
    {
        var snapshot = await new SecurityService(new ThrowingProbe()).GetStatusAsync();

        Assert.Equal(SecurityStatuses.Unknown, snapshot.Defender.Status);
        Assert.Equal(SecurityStatuses.Unknown, snapshot.Firewall.Status);
        Assert.Empty(snapshot.Ports);
        Assert.Empty(snapshot.Antivirus.Products);
        Assert.Equal(4, snapshot.Diagnostics.Count);
    }

    [Fact]
    public async Task GetStatus_UacOffAndRdpOpen_AreWarnings()
    {
        var reading = FullReading() with
        {
            System = new SystemRaw(UacEnabled: false, RdpConnectionsDenied: false),
        };

        var snapshot = await Service(reading).GetStatusAsync();

        var uac = Assert.Single(snapshot.Diagnostics, c => c.Id == "uac");
        Assert.Equal(SecurityStatuses.Warn, uac.Status);
        Assert.NotNull(uac.Detail);

        var rdp = Assert.Single(snapshot.Diagnostics, c => c.Id == "rdp");
        Assert.Equal(SecurityStatuses.Warn, rdp.Status);
        Assert.NotNull(rdp.Detail);
    }

    [Fact]
    public async Task GetStatus_ThirdPartyAntivirus_RealtimeCheckStaysOk()
    {
        // Defender sin datos (WMI caída) pero el Centro de seguridad marca el
        // antivirus bueno: la protección NO se reporta como advertencia.
        var reading = FullReading() with
        {
            Defender = null,
            AntivirusProducts = ["Bitdefender Antivirus"],
        };

        var snapshot = await Service(reading).GetStatusAsync();

        var realtime = Assert.Single(snapshot.Diagnostics, c => c.Id == "realtime");
        Assert.Equal(SecurityStatuses.Ok, realtime.Status);
        Assert.Contains("Bitdefender Antivirus", realtime.Detail);
    }

    [Fact]
    public async Task GetStatus_DefenderRealtimeOff_IsWarnAndPoorStatus()
    {
        var reading = FullReading() with
        {
            Defender = new DefenderRaw(AntivirusEnabled: true, RealTimeProtection: false, SignatureLastUpdated: null),
        };

        var snapshot = await Service(reading).GetStatusAsync();

        Assert.Equal(SecurityStatuses.Poor, snapshot.Defender.Status);
        var realtime = Assert.Single(snapshot.Diagnostics, c => c.Id == "realtime");
        Assert.Equal(SecurityStatuses.Warn, realtime.Status);
    }

    [Fact]
    public async Task GetStatus_SignaturesOlderThanWeek_IsWarn()
    {
        var reading = FullReading() with
        {
            Defender = new DefenderRaw(true, true, DateTimeOffset.UtcNow.AddDays(-30)),
        };

        var snapshot = await Service(reading).GetStatusAsync();

        var signatures = Assert.Single(snapshot.Diagnostics, c => c.Id == "signatures");
        Assert.Equal(SecurityStatuses.Warn, signatures.Status);
        Assert.Contains("Hace 30", signatures.Detail);
    }

    [Fact]
    public async Task GetStatus_RecentSignatures_IsOk()
    {
        var snapshot = await Service(FullReading()).GetStatusAsync();

        var signatures = Assert.Single(snapshot.Diagnostics, c => c.Id == "signatures");
        Assert.Equal(SecurityStatuses.Ok, signatures.Status);
    }

    [Fact]
    public async Task GetStatus_PortsAreSortedByProtocolThenPort()
    {
        var snapshot = await Service(FullReading()).GetStatusAsync();

        Assert.Equal(
            [("TCP", 135), ("TCP", 443), ("UDP", 5353)],
            snapshot.Ports.Select(p => (p.Protocol, p.LocalPort)).ToArray());
    }

    [Fact]
    public async Task GetStatus_ScannedAtIsRecent()
    {
        var before = DateTimeOffset.UtcNow;

        var snapshot = await Service(FullReading()).GetStatusAsync();

        Assert.InRange(snapshot.ScannedAt, before.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
    }
}
