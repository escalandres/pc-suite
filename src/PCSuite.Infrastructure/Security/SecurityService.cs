using PCSuite.Core.Security;
using System.Runtime.Versioning;

namespace PCSuite.Infrastructure.Security;

// Task 012 / parte 12.2: SecurityService SOLO compone la lectura cruda del
// probe en el snapshot del contrato y traduce cada fallo a "unknown" con
// detalle legible — nunca excepciones ni stack traces hacia la UI (§14,
// Security Center de solo lectura y sin elevación). El probe se inyecta
// (WindowsSecurityProbe en producción, lecturas falsas en los tests).

public sealed class SecurityService : ISecurityService
{
    private const string UacLabel = "Control de cuentas de usuario (UAC)";
    private const string RdpLabel = "Escritorio remoto (RDP)";
    private const string RealtimeLabel = "Protección en tiempo real";
    private const string SignatureLabel = "Firmas de Windows Defender";

    private readonly ISecurityProbe _probe;

    /// <summary>Sondeo nativo real (WSC/WMI/registro/puertos).</summary>
    // Solo ESTE constructor es Windows-only (instancia el probe nativo): el
    // que inyecta el probe sigue neutral para los tests (net10.0).
    [SupportedOSPlatform("windows")]
    public SecurityService()
        : this(new WindowsSecurityProbe())
    {
    }

    /// <summary>Sondeo inyectable (tests).</summary>
    public SecurityService(ISecurityProbe probe) =>
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));

    public Task<SecuritySnapshot> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Defensa en profundidad: el probe promete no lanzar, pero la
                // página de Seguridad debe renderizarse aunque algo falle.
                RawSecurityReading raw;
                try
                {
                    raw = _probe.ReadRaw();
                }
                catch (Exception)
                {
                    raw = NoData;
                }

                return Build(raw, DateTimeOffset.UtcNow);
            },
            cancellationToken);

    /// <summary>Lectura equivalente a "todas las fuentes fallaron".</summary>
    private static RawSecurityReading NoData { get; } = new(
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

    private static SecuritySnapshot Build(RawSecurityReading raw, DateTimeOffset now)
    {
        var defender = raw.Defender is { } d
            ? new DefenderInfo(
                d.AntivirusEnabled && d.RealTimeProtection ? SecurityStatuses.Good
                    : d.AntivirusEnabled ? SecurityStatuses.Poor
                    : SecurityStatuses.NotMonitored,
                d.AntivirusEnabled,
                d.RealTimeProtection,
                d.SignatureLastUpdated,
                SignatureNote(d))
            : new DefenderInfo(SecurityStatuses.Unknown, null, null, null, null);

        var firewall = new FirewallInfo(
            raw.FirewallHealth ?? SecurityStatuses.Unknown,
            raw.Firewall.DomainEnabled,
            raw.Firewall.PrivateEnabled,
            raw.Firewall.PublicEnabled);

        var products = raw.AntivirusProducts ?? [];
        var antivirus = new AntivirusInfo(raw.AntivirusHealth ?? SecurityStatuses.Unknown, products);

        // Orden fijo y determinista: protocolo, puerto y dirección.
        var ports = raw.Ports
            .OrderBy(port => port.Protocol, StringComparer.Ordinal)
            .ThenBy(port => port.LocalPort)
            .ThenBy(port => port.LocalAddress, StringComparer.Ordinal)
            .ToList();

        // Task 029: marcadores factuales de revisión por conexión (la nota
        // viaja al snapshot; el usuario juzga — jamás veredictos).
        var tempRoot = Path.GetTempPath();
        var connections = raw.Connections
            .Select(connection => connection with
            {
                ReviewNote = BuildReviewNote(connection.Process, tempRoot),
            })
            .ToList();

        var udpEndpoints = raw.UdpEndpoints
            .Select(endpoint => endpoint with
            {
                ReviewNote = BuildReviewNote(endpoint.Process, tempRoot),
            })
            .ToList();

        // Task 030: marcadores en autostart + snapshot de auditoría.
        var autostart = raw.Audit.Autostart
            .Select(entry => entry with
            {
                ReviewNote = BuildReviewNote(
                    new PortProcessInfo(entry.Name, entry.ExecutablePath, entry.Signer),
                    tempRoot),
            })
            .ToList();
        var audit = raw.Audit with { Autostart = autostart };

        var diagnostics = new List<SecurityCheck>
        {
            BuildUacCheck(raw.System),
            BuildRdpCheck(raw.System),
            BuildRealtimeCheck(raw.Defender, raw.AntivirusHealth, products),
            BuildSignatureCheck(raw.Defender, now),
        };

        return new SecuritySnapshot(defender, firewall, antivirus, ports, connections, udpEndpoints, audit, raw.Discovery, diagnostics, now);
    }

    private static SecurityCheck BuildUacCheck(SystemRaw system) =>
        new(
            "uac",
            UacLabel,
            system.UacEnabled switch
            {
                null => SecurityStatuses.Unknown,
                true => SecurityStatuses.Ok,
                false => SecurityStatuses.Warn,
            },
            system.UacEnabled switch
            {
                null => null,
                true => "Activado: los cambios sensibles piden confirmación.",
                false => "Desactivado: los programas pueden cambiar el sistema sin avisar.",
            });

    private static SecurityCheck BuildRdpCheck(SystemRaw system) =>
        new(
            "rdp",
            RdpLabel,
            system.RdpConnectionsDenied switch
            {
                null => SecurityStatuses.Unknown,
                true => SecurityStatuses.Ok,
                false => SecurityStatuses.Warn,
            },
            system.RdpConnectionsDenied switch
            {
                null => null,
                true => "Conexiones entrantes bloqueadas.",
                false => "Conexiones entrantes permitidas; si no lo usas, conviene bloquearlas.",
            });

    private static SecurityCheck BuildRealtimeCheck(
        DefenderRaw? defender,
        string? antivirusHealth,
        IReadOnlyList<string> products)
    {
        if (defender is { RealTimeProtection: true })
        {
            return new SecurityCheck(
                "realtime", RealtimeLabel, SecurityStatuses.Ok,
                "Windows Defender protegiendo en tiempo real.");
        }

        if (defender is { AntivirusEnabled: true })
        {
            return new SecurityCheck(
                "realtime", RealtimeLabel, SecurityStatuses.Warn,
                "Windows Defender activo pero sin protección en tiempo real.");
        }

        // Defender cedido o WMI no disponible: el guardián es el AV registrado.
        return antivirusHealth switch
        {
            SecurityStatuses.Good => new SecurityCheck(
                "realtime", RealtimeLabel, SecurityStatuses.Ok,
                products.Count > 0
                    ? $"Protegido por: {string.Join(", ", products)}."
                    : "Protección activa según el Centro de seguridad."),
            SecurityStatuses.Poor => new SecurityCheck(
                "realtime", RealtimeLabel, SecurityStatuses.Warn,
                "El Centro de seguridad la marca como deficiente."),
            SecurityStatuses.Snoozed => new SecurityCheck(
                "realtime", RealtimeLabel, SecurityStatuses.Warn,
                "Protección pausada según el Centro de seguridad."),
            SecurityStatuses.NotMonitored => new SecurityCheck(
                "realtime", RealtimeLabel, SecurityStatuses.Warn,
                "Sin antivirus supervisado según el Centro de seguridad."),
            _ => new SecurityCheck("realtime", RealtimeLabel, SecurityStatuses.Unknown, null),
        };
    }

    private static SecurityCheck BuildSignatureCheck(DefenderRaw? defender, DateTimeOffset now)
    {
        if (defender?.SignatureLastUpdated is not { } updated)
        {
            // Task 028.2: fallback del Centro de seguridad (sin timestamp) —
            // el bit de firmas al día sostiene el semáforo igual.
            return defender?.SignaturesUpToDate switch
            {
                true => new SecurityCheck(
                    "signatures", SignatureLabel, SecurityStatuses.Ok,
                    "Al día según el Centro de seguridad de Windows."),
                false => new SecurityCheck(
                    "signatures", SignatureLabel, SecurityStatuses.Warn,
                    "Desactualizadas según el Centro de seguridad; revisa Windows Update."),
                _ => new SecurityCheck("signatures", SignatureLabel, SecurityStatuses.Unknown, null),
            };
        }

        var age = now - updated;
        var detail = $"Última actualización: {updated:yyyy-MM-dd}.";

        return age > TimeSpan.FromDays(7)
            ? new SecurityCheck(
                "signatures", SignatureLabel, SecurityStatuses.Warn,
                $"{detail} Hace {(int)age.TotalDays} días; revisa Windows Update.")
            : new SecurityCheck("signatures", SignatureLabel, SecurityStatuses.Ok, detail);
    }

    /// <summary>Nota para la fila "Firmas" cuando no hay timestamp: el Centro
    /// de seguridad solo dice al día/desactualizada (tier 2 de Windows 11
    /// actuales, sin root\Microsoft\Defender).</summary>
    private static string? SignatureNote(DefenderRaw d) =>
        d.SignatureLastUpdated is null
            ? d.SignaturesUpToDate switch
            {
                true => "Al día",
                false => "Desactualizadas",
                _ => null,
            }
            : null;

    /// <summary>
    /// Task 029: marcadores FACTUALES de revisión para escanear conexiones
    /// sospechosas: sin firma legible en el ejecutable o ejecutable dentro
    /// de TEMP. Son HECHOS, no veredictos — el usuario decide. Rutas
    /// ilegibles (procesos del sistema protegidos) no acusan nada.
    /// </summary>
    internal static string? BuildReviewNote(PortProcessInfo? process, string tempRoot)
    {
        if (process?.ExecutablePath is not { } path)
        {
            return null;
        }

        var notes = new List<string>();
        if (process.Signer is null)
        {
            notes.Add("sin firma");
        }

        if (path.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("ejecutable en TEMP");
        }

        return notes.Count > 0 ? string.Join(" · ", notes) : null;
    }
}
