namespace PCSuite.Contracts;

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

    /// <summary>Diagnóstico de seguridad (UAC, RDP, protección en tiempo real…).</summary>
    public IReadOnlyList<SecurityCheckResult> Diagnostics { get; init; } = [];

    /// <summary>Instante UTC del sondeo (la UI lo muestra como "revisado hace…").</summary>
    public DateTimeOffset ScannedAt { get; init; }
}
