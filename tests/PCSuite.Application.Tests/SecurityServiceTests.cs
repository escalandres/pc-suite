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
        ],
        Connections:
        [
            new ActiveConnection("192.168.1.10", 50000, "93.184.216.34", 443, 777,
                new PortProcessInfo("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", "Google LLC")),
            // Ruta bajo el TEMP REAL de la máquina: el marcador "en TEMP" es
            // determinista en cualquier entorno.
            new ActiveConnection("192.168.1.10", 50001, "45.33.32.156", 8080, 888,
                new PortProcessInfo("weird", Path.Combine(Path.GetTempPath(), "weird.exe"), null)),
        ],
        UdpEndpoints:
        [
            new UdpEndpoint("0.0.0.0", 5353, 1112, new PortProcessInfo("svchost", null, null)),
            new UdpEndpoint("192.168.1.10", 137, 4, new PortProcessInfo("svchost", null, null)),
        ],
        Audit: new SecurityAudit(
        [
            new AutostartEntry("Registro (HKCU Run)", "OneDrive", @"C:\Users\ana\AppData\Local\Microsoft\OneDrive\OneDrive.exe /background",
                @"C:\Users\ana\AppData\Local\Microsoft\OneDrive\OneDrive.exe", "Microsoft Corporation"),
            new AutostartEntry("Carpeta Startup (usuario)", "backup.lnk",
                @"C:\Users\ana\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\backup.lnk",
                null, null),
            new AutostartEntry("Servicio automático", "Windows Defender Antivirus Service",
                @"C:\ProgramData\Microsoft\Windows Defender\Platform\...\MsMpEng.exe", "Microsoft Windows"),
        ],
        [
            new FlaggedProcess(999, "weird", Path.Combine(Path.GetTempPath(), "weird.exe"), null, "sin firma · ejecutable en TEMP"),
        ],
        TotalProcesses: 15),
        Discovery: new SecurityDiscovery(
        [
            new DnsCacheEntry("aes-studio.dev", "A"),
            new DnsCacheEntry("github.com", "A"),
        ],
        [
            new ScheduledTaskEntry("GoogleUpdateTaskMachineUA", "Listo", @"C:\Program Files\Google\Update\GoogleUpdate.exe /ua /installbackground", "2026-10-26 02:00"),
        ],
        [
            new NetworkShareEntry("C$", @"C:\", "Recurso compartido predeterminado", "Admin (Disco)"),
            new NetworkShareEntry("IPC$", null, "Canalización remota", "Admin (IPC)"),
        ],
        [
            new HostsFileEntry("127.0.0.1", "localhost"),
            new HostsFileEntry("::1", "localhost"),
        ]));

    private static SecurityService Service(RawSecurityReading raw) => new(new FakeProbe(raw));

    [Fact]
    public async Task GetStatus_MapsFullReadingToSnapshot()
    {
        var snapshot = await Service(FullReading()).GetStatusAsync();

        Assert.Equal(SecurityStatuses.Good, snapshot.Defender.Status);
        Assert.True(snapshot.Defender.AntivirusEnabled);
        Assert.True(snapshot.Defender.RealTimeProtection);
        Assert.NotNull(snapshot.Defender.SignatureLastUpdated);
        Assert.Null(snapshot.Defender.SignatureNote); // con timestamp no hay nota

        // Task 029: las conexiones viajan con su extremo remoto + proceso.
        Assert.Equal(2, snapshot.Connections.Count);
        var chrome = snapshot.Connections.Single(c => c.Process?.Name == "chrome");
        Assert.Equal("93.184.216.34", chrome.RemoteAddress);
        Assert.Equal(443, chrome.RemotePort);
        Assert.Equal("Google LLC", chrome.Process!.Signer);
        Assert.Null(chrome.ReviewNote); // firmado + fuera de TEMP: nada que revisar

        var weird = snapshot.Connections.Single(c => c.Process?.Name == "weird");
        Assert.Contains("sin firma", weird.ReviewNote);
        Assert.Contains("TEMP", weird.ReviewNote);

        // Task 029: los endpoints UDP también viajan con su proceso.
        Assert.Equal(2, snapshot.UdpEndpoints.Count);
        var mdns = snapshot.UdpEndpoints.Single(e => e.LocalPort == 5353);
        Assert.Equal("svchost", mdns.Process?.Name);
        Assert.Null(mdns.ReviewNote); // proceso del sistema sin ruta: no acusa

        // Task 030: auditoría de persistencia y procesos.
        Assert.Equal(3, snapshot.Audit.Autostart.Count);
        Assert.Equal(15, snapshot.Audit.TotalProcesses);
        Assert.Single(snapshot.Audit.FlaggedProcesses);
        var oneDrive = snapshot.Audit.Autostart.Single(e => e.Name == "OneDrive");
        Assert.Equal("Microsoft Corporation", oneDrive.Signer);
        Assert.Null(oneDrive.ReviewNote); // firmado + fuera de TEMP
        var backupLnk = snapshot.Audit.Autostart.Single(e => e.Name == "backup.lnk");
        Assert.Null(backupLnk.Signer); // un .lnk no lleva firma Authenticode
        Assert.Null(backupLnk.ReviewNote); // sin ruta de exe legible: no acusa

        // Task 031: descubrimiento de actividad.
        Assert.Equal(2, snapshot.Discovery.DnsCache.Count);
        Assert.Equal("aes-studio.dev", snapshot.Discovery.DnsCache[0].Domain);
        Assert.Equal("A", snapshot.Discovery.DnsCache[0].RecordType);
        Assert.Single(snapshot.Discovery.ScheduledTasks);
        Assert.Equal(2, snapshot.Discovery.NetworkShares.Count);
        var cShare = snapshot.Discovery.NetworkShares.Single(s => s.Name == "C$");
        Assert.Contains("Admin", cShare.ShareType);
        Assert.Equal(2, snapshot.Discovery.HostsEntries.Count);

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
    public void BuildReviewNote_MarcadoresFactuales()
    {
        // Sin firma (hecho útil), fuera de TEMP: solo "sin firma".
        var unsigned = SecurityService.BuildReviewNote(
            new PortProcessInfo("x", @"C:\Program Files\x\x.exe", null),
            @"C:\Users\ana\AppData\Local\Temp\");
        Assert.Equal("sin firma", unsigned);

        // Firmado y en TEMP: solo "en TEMP".
        var inTemp = SecurityService.BuildReviewNote(
            new PortProcessInfo("y", @"C:\Users\ana\AppData\Local\Temp\y.exe", "Firma Válida"),
            @"C:\Users\ana\AppData\Local\Temp\");
        Assert.Equal("ejecutable en TEMP", inTemp);

        // Ambos marcadores: "sin firma · ejecutable en TEMP".
        var both = SecurityService.BuildReviewNote(
            new PortProcessInfo("z", @"C:\Users\ana\AppData\Local\Temp\z.exe", null),
            @"C:\Users\ana\AppData\Local\Temp\");
        Assert.Equal("sin firma · ejecutable en TEMP", both);

        // Firmado y en su sitio: nada que revisar.
        var clean = SecurityService.BuildReviewNote(
            new PortProcessInfo("chrome", @"C:\Program Files\...\chrome.exe", "Google LLC"),
            @"C:\Users\ana\AppData\Local\Temp\");
        Assert.Null(clean);

        // Proceso del sistema (ruta ilegible): jamás acusa.
        Assert.Null(SecurityService.BuildReviewNote(null, @"C:\Temp"));
        Assert.Null(SecurityService.BuildReviewNote(
            new PortProcessInfo("svchost", null, null), @"C:\Temp"));
    }

    [Fact]
    public void DecodeDefenderState_StatesTable()
    {
        // Task 028.2: Windows 11 actual ELIMINÓ root\Microsoft\Defender — el
        // tier 2 decodifica el productState del Centro de seguridad.

        // Activado + firmas al día (0x061100).
        var active = WindowsSecurityProbe.DecodeDefenderState(0x061100);
        Assert.NotNull(active);
        Assert.True(active!.AntivirusEnabled);
        Assert.True(active.RealTimeProtection);
        Assert.True(active.SignaturesUpToDate);

        // En espera (otro AV tomó el control) + al día — el caso real del
        // diagnóstico (Defender cedió ante 360 Total Security: 0x060100).
        var snoozed = WindowsSecurityProbe.DecodeDefenderState(0x060100);
        Assert.NotNull(snoozed);
        Assert.False(snoozed!.AntivirusEnabled);
        Assert.False(snoozed.RealTimeProtection);
        Assert.True(snoozed.SignaturesUpToDate);

        // Desactivado explícito (0x060000).
        var disabled = WindowsSecurityProbe.DecodeDefenderState(0x060000);
        Assert.NotNull(disabled);
        Assert.False(disabled!.AntivirusEnabled);

        // Activado + firmas desactualizadas (byte 3 = 10).
        var outdated = WindowsSecurityProbe.DecodeDefenderState(0x061110);
        Assert.NotNull(outdated);
        Assert.True(outdated!.AntivirusEnabled);
        Assert.False(outdated.SignaturesUpToDate);

        // Esquema desconocido: null antes que mentir.
        Assert.Null(WindowsSecurityProbe.DecodeDefenderState(0xFFFFFF));
    }

    [Fact]
    public async Task GetStatus_DefenderFromSecurityCenter_NoteReplacesMissingTimestamp()
    {
        // Tier 2 en acción: sin namespace WMI, el Centro de seguridad solo
        // sabe el bit de firmas — la fila del card muestra la nota y el
        // check sostiene el semáforo con ella.
        var reading = FullReading() with
        {
            Defender = new DefenderRaw(false, false, null, SignaturesUpToDate: true),
        };

        var snapshot = await Service(reading).GetStatusAsync();

        // Card: valores REALES (no "Sin datos") + nota en firmas.
        Assert.False(snapshot.Defender.AntivirusEnabled);
        Assert.False(snapshot.Defender.RealTimeProtection);
        Assert.Null(snapshot.Defender.SignatureLastUpdated);
        Assert.Equal("Al día", snapshot.Defender.SignatureNote);

        // El check de firmas sostiene el semáforo con el bit.
        var signatures = Assert.Single(snapshot.Diagnostics, c => c.Id == "signatures");
        Assert.Equal(SecurityStatuses.Ok, signatures.Status);
    }

    [Fact]
    public async Task GetStatus_DefenderFromSecurityCenter_OutdatedSignatures_WarnWithNote()
    {
        var reading = FullReading() with
        {
            Defender = new DefenderRaw(true, true, null, SignaturesUpToDate: false),
        };

        var snapshot = await Service(reading).GetStatusAsync();

        Assert.Equal("Desactualizadas", snapshot.Defender.SignatureNote);
        var signatures = Assert.Single(snapshot.Diagnostics, c => c.Id == "signatures");
        Assert.Equal(SecurityStatuses.Warn, signatures.Status);
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
            Ports: [],
            Connections: [],
            UdpEndpoints: [],
            Audit: new SecurityAudit([], [], 0),
            Discovery: new SecurityDiscovery([], [], [], []));

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
