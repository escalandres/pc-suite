namespace PCSuite.Contracts;

/// <summary>Proceso dueño legible SIN elevación: nombre, ruta y firma (CN del
/// certificado Authenticode). Procesos protegidos ocultan lo que quieran.</summary>
public sealed class PortProcessResult
{
    public string? Name { get; init; }

    /// <summary>Ruta absoluta del ejecutable (null si ilegible).</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>CN del certificado de firma; null = sin firma (o ilegible).</summary>
    public string? Signer { get; init; }
}

/// <summary>Conexión TCP ESTABLECIDA con su extremo remoto y proceso dueño.</summary>
public sealed class ActiveConnectionResult
{
    public string LocalAddress { get; init; } = "";

    public int LocalPort { get; init; }

    public string RemoteAddress { get; init; } = "";

    public int RemotePort { get; init; }

    public int? Pid { get; init; }

    public PortProcessResult? Process { get; init; }

    /// <summary>Hechos para revisar la conexión (sin firma · ejecutable en TEMP);
    /// null = nada que revisar.</summary>
    public string? ReviewNote { get; init; }
}

/// <summary>Endpoint UDP VINCULADO con proceso dueño (Task 029).</summary>
public sealed class UdpEndpointResult
{
    public string LocalAddress { get; init; } = "";

    public int LocalPort { get; init; }

    public int? Pid { get; init; }

    public PortProcessResult? Process { get; init; }

    /// <summary>Hechos para revisar (sin firma · ejecutable en TEMP); null = nada.</summary>
    public string? ReviewNote { get; init; }
}

/// <summary>Una entrada de inicio automático (persistencia).</summary>
public sealed class AutostartResult
{
    /// <summary>Registro (HKCU Run) | Carpeta Startup | Servicio automático…</summary>
    public string Source { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>Comando completo del registro o ruta del archivo.</summary>
    public string? Command { get; init; }

    /// <summary>Ruta del ejecutable extraída (para tooltip).</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>CN del certificado de firma; null = sin firma.</summary>
    public string? Signer { get; init; }

    /// <summary>Hechos para revisar; null = nada.</summary>
    public string? ReviewNote { get; init; }
}

/// <summary>Proceso en ejecución que requiere revisión.</summary>
public sealed class FlaggedProcessResult
{
    public int Pid { get; init; }

    public string? Name { get; init; }

    public string? ExecutablePath { get; init; }

    public string? Signer { get; init; }

    public string? ReviewNote { get; init; }
}

/// <summary>Task 030: auditoría de persistencia y procesos.</summary>
public sealed class SecurityAuditResult
{
    public IReadOnlyList<AutostartResult> Autostart { get; init; } = [];

    public IReadOnlyList<FlaggedProcessResult> FlaggedProcesses { get; init; } = [];

    /// <summary>Total de procesos en ejecución (contexto para el contador de la UI).</summary>
    public int TotalProcesses { get; init; }
}

/// <summary>Entrada del caché DNS de Windows (Task 031).</summary>
public sealed class DnsCacheEntryResult
{
    /// <summary>Dominio resuelto.</summary>
    public string Domain { get; init; } = "";

    /// <summary>Tipo de registro (A, CNAME, AAAA, SRV…).</summary>
    public string RecordType { get; init; } = "";
}

/// <summary>Una tarea programada del Programador de Windows (Task 031).</summary>
public sealed class ScheduledTaskResult
{
    public string Name { get; init; } = "";

    public string? Status { get; init; }

    /// <summary>Comando que ejecuta la tarea (si es legible).</summary>
    public string? Command { get; init; }

    public string? NextRun { get; init; }
}

/// <summary>Un recurso compartido de red (Task 031).</summary>
public sealed class NetworkShareResult
{
    public string Name { get; init; } = "";

    public string? Path { get; init; }

    public string? Description { get; init; }

    /// <summary>Disco | Impresora | IPC | Admin (Disco)…</summary>
    public string ShareType { get; init; } = "";
}

/// <summary>Una entrada del hosts file (Task 031).</summary>
public sealed class HostsFileEntryResult
{
    public string Ip { get; init; } = "";

    /// <summary>Hostnames que apuntan a esta IP (uno o más).</summary>
    public string Hostnames { get; init; } = "";
}

/// <summary>Task 031: descubrimiento de actividad — DNS, tareas, shares, hosts.</summary>
public sealed class SecurityDiscoveryResult
{
    public IReadOnlyList<DnsCacheEntryResult> DnsCache { get; init; } = [];

    public IReadOnlyList<ScheduledTaskResult> ScheduledTasks { get; init; } = [];

    public IReadOnlyList<NetworkShareResult> NetworkShares { get; init; } = [];

    public IReadOnlyList<HostsFileEntryResult> HostsEntries { get; init; } = [];
}

/// <summary>Resultado de security.getStatus: snapshot completo de solo lectura.</summary>
public sealed class SecurityStatusResult
{
    /// <summary>Estado de Windows Defender (semáforo + protección en tiempo real + firmas).</summary>
    public DefenderStatusResult Defender { get; init; } = new();

    /// <summary>Estado del firewall por perfil (dominio/privado/público).</summary>
    public FirewallStatusResult Firewall { get; init; } = new();

    /// <summary>Antivirus registrados en el Centro de seguridad.</summary>
    public AntivirusStatusResult Antivirus { get; init; } = new();

    /// <summary>Puertos en escucha (orden fijo: protocolo y puerto).</summary>
    public IReadOnlyList<PortResult> Ports { get; init; } = [];

    /// <summary>Task 029: conexiones TCP establecidas con extremo remoto + proceso.</summary>
    public IReadOnlyList<ActiveConnectionResult> Connections { get; init; } = [];

    /// <summary>Task 029: endpoints UDP vinculados con proceso dueño.</summary>
    public IReadOnlyList<UdpEndpointResult> UdpEndpoints { get; init; } = [];

    /// <summary>Task 030: auditoría de persistencia y procesos.</summary>
    public SecurityAuditResult Audit { get; init; } = new();

    /// <summary>Task 031: descubrimiento de actividad (DNS, tareas, shares, hosts).</summary>
    public SecurityDiscoveryResult Discovery { get; init; } = new();

    /// <summary>Diagnóstico de seguridad (UAC, RDP, protección en tiempo real…).</summary>
    public IReadOnlyList<SecurityCheckResult> Diagnostics { get; init; } = [];

    /// <summary>Instante UTC del sondeo (la UI lo muestra como "revisado hace…").</summary>
    public DateTimeOffset ScannedAt { get; init; }
}
