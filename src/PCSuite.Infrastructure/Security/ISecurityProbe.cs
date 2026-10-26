using PCSuite.Core.Security;

namespace PCSuite.Infrastructure.Security;

// Task 012 / parte 12.2: seam de testeo de TODO el contacto con el OS. El
// probe lee fuentes nativas de SOLO LECTURA (WSC/WMI/registro/ipphlpapi) y
// jamás lanza: cada sección que falle llega null/vacía y SecurityService la
// traduce a "unknown" con detalle legible (§14 Security Center). Así los tests
// inyectan lecturas falsas y corren deterministas en cualquier máquina.

/// <summary>
/// Windows Defender: tier 1 = WMI clásico (root\Microsoft\Defender, donde
/// el namespace existe); tier 2 = Centro de seguridad (root\SecurityCenter2,
/// productState) en Windows 11 actuales que lo eliminaron. null = ninguna
/// fuente respondió. SignaturesUpToDate solo lo sabe el tier 2 (el WMI
/// clásico entrega el timestamp de firmas).
/// </summary>
public sealed record DefenderRaw(
    bool AntivirusEnabled,
    bool RealTimeProtection,
    DateTimeOffset? SignatureLastUpdated,
    bool? SignaturesUpToDate = null);

/// <summary>Perfiles de firewall desde el registro (StandardProfile = privado); null = sin datos.</summary>
public sealed record FirewallRaw(bool? DomainEnabled, bool? PrivateEnabled, bool? PublicEnabled);

/// <summary>UAC (EnableLUA), RDP (fDenyTSConnections), BitLocker y SMBv1 del registro/sistema; null = ilegible.</summary>
public sealed record SystemRaw(
    bool? UacEnabled,
    bool? RdpConnectionsDenied,
    bool? BitLockerOn = null,
    bool? Smb1Disabled = null);

/// <summary>
/// Lectura cruda completa: <c>null</c>/vacío significa "esa fuente falló",
/// distinto de "está desactivado". Nunca una excepción.
/// </summary>
public sealed record RawSecurityReading(
    string? FirewallHealth,
    string? AntivirusHealth,
    DefenderRaw? Defender,
    IReadOnlyList<string>? AntivirusProducts,
    FirewallRaw Firewall,
    SystemRaw System,
    IReadOnlyList<ListeningPort> Ports,
    IReadOnlyList<ActiveConnection> Connections,
    IReadOnlyList<UdpEndpoint> UdpEndpoints,
    SecurityAudit Audit,
    SecurityDiscovery Discovery);

public interface ISecurityProbe
{
    /// <summary>Todas las fuentes nativas en una pasada; no lanza excepciones.</summary>
    RawSecurityReading ReadRaw();
}
