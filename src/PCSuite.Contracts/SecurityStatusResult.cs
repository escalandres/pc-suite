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

    /// <summary>Diagnóstico de seguridad (UAC, RDP, protección en tiempo real…).</summary>
    public IReadOnlyList<SecurityCheckResult> Diagnostics { get; init; } = [];

    /// <summary>Instante UTC del sondeo (la UI lo muestra como "revisado hace…").</summary>
    public DateTimeOffset ScannedAt { get; init; }
}
