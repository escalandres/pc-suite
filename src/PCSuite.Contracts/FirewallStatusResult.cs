namespace PCSuite.Contracts;

/// <summary>Estado del firewall de Windows por perfil (Task 012 / §14).</summary>
public sealed class FirewallStatusResult
{
    /// <summary>Semáforo agregado: good/poor/notMonitored/snoozed/unknown.</summary>
    public string Status { get; init; } = "unknown";

    /// <summary>Perfil de dominio activo (null = el registro no se pudo leer).</summary>
    public bool? DomainEnabled { get; init; }

    /// <summary>Perfil privado activo (null = sin datos).</summary>
    public bool? PrivateEnabled { get; init; }

    /// <summary>Perfil público activo (null = sin datos).</summary>
    public bool? PublicEnabled { get; init; }
}
