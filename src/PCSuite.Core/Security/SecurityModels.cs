namespace PCSuite.Core.Security;

/// <summary>
/// Vocabulario fijo de estados del Security Center (Task 012 / §14). El
/// semáforo (<c>good</c>/<c>poor</c>/<c>notMonitored</c>/<c>snoozed</c>/<c>unknown</c>)
/// mapea la salud WSC de cada proveedor; los checks de diagnóstico usan
/// <c>ok</c>/<c>warn</c>/<c>unknown</c>. Nada más llega a la UI.
/// </summary>
public static class SecurityStatuses
{
    /// <summary>Proveedor sano (WSC: Good).</summary>
    public const string Good = "good";

    /// <summary>Proveedor sin supervisión (WSC: NotMonitored).</summary>
    public const string NotMonitored = "notMonitored";

    /// <summary>Proveedor en mal estado (WSC: Poor).</summary>
    public const string Poor = "poor";

    /// <summary>Protección pausada/snoozed (WSC: Snoozed).</summary>
    public const string Snoozed = "snoozed";

    /// <summary>No se pudo determinar (WMI/registro no disponible); la UI lo muestra como gris.</summary>
    public const string Unknown = "unknown";

    /// <summary>Check de diagnóstico superado.</summary>
    public const string Ok = "ok";

    /// <summary>Check de diagnóstico con advertencia.</summary>
    public const string Warn = "warn";
}

/// <summary>Windows Defender: salud general + protección en tiempo real y firmas.
/// SignatureNote reemplaza al timestamp cuando la fuente (Centro de seguridad)
/// solo sabe al día/desactualizada — nunca una fecha inventada.</summary>
public sealed record DefenderInfo(
    string Status,
    bool? AntivirusEnabled,
    bool? RealTimeProtection,
    DateTimeOffset? SignatureLastUpdated,
    string? SignatureNote = null);

/// <summary>
/// Firewall por perfil (dominio/privado/público). <c>null</c> en un perfil
/// significa que el registro no se pudo leer — no que esté desactivado.
/// </summary>
public sealed record FirewallInfo(
    string Status,
    bool? DomainEnabled,
    bool? PrivateEnabled,
    bool? PublicEnabled);

/// <summary>Antivirus registrados en el Centro de seguridad (WMI SecurityCenter2).</summary>
public sealed record AntivirusInfo(
    string Status,
    IReadOnlyList<string> Products);

/// <summary>Proceso dueño de un puerto/conexión: lo legible SIN elevación
/// (procesos del sistema pueden ocultar ruta/firma — jamás un error).</summary>
public sealed record PortProcessInfo(
    string? Name,
    string? ExecutablePath,
    string? Signer);

/// <summary>Conexión TCP ESTABLECIDA: local → remoto + proceso dueño.
/// ReviewNote son marcadores FACTUALES de revisión (sin firma legible,
/// ejecutable en TEMP) para escanear actividad sospechosa — decisiones
/// del usuario, jamás veredictos.</summary>
public sealed record ActiveConnection(
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    int? Pid,
    PortProcessInfo? Process,
    string? ReviewNote = null);

/// <summary>Puerto en escucha (IPGlobalProperties). PID/proceso pueden ser null si la ACL lo niega.</summary>
public sealed record ListeningPort(
    string Protocol,
    string LocalAddress,
    int LocalPort,
    int? Pid,
    string? Process,
    PortProcessInfo? ProcessDetail = null);

/// <summary>Una línea de diagnóstico (UAC, RDP, protección en tiempo real…).</summary>
public sealed record SecurityCheck(
    string Id,
    string Label,
    string Status,
    string? Detail);

/// <summary>Snapshot completo de solo lectura que devuelve security.getStatus.</summary>
public sealed record SecuritySnapshot(
    DefenderInfo Defender,
    FirewallInfo Firewall,
    AntivirusInfo Antivirus,
    IReadOnlyList<ListeningPort> Ports,
    IReadOnlyList<ActiveConnection> Connections,
    IReadOnlyList<SecurityCheck> Diagnostics,
    DateTimeOffset ScannedAt);
