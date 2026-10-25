namespace PCSuite.Contracts;

/// <summary>Estado de Windows Defender (Task 012 / §14).</summary>
public sealed class DefenderStatusResult
{
    /// <summary>Semáforo: good/poor/notMonitored/snoozed/unknown.</summary>
    public string Status { get; init; } = "unknown";

    /// <summary>Antivirus activo según Defender (null si WMI no respondió).</summary>
    public bool? AntivirusEnabled { get; init; }

    /// <summary>Protección en tiempo real activa (null si WMI no respondió).</summary>
    public bool? RealTimeProtection { get; init; }

    /// <summary>UTC de la última actualización de firmas (null si desconocido).</summary>
    public DateTimeOffset? SignatureLastUpdated { get; init; }

    /// <summary>
    /// Task 028.2: nota de firmas cuando la fuente (Centro de seguridad) solo
    /// sabe al día/desactualizada — reemplaza al timestamp en la fila del card.
    /// </summary>
    public string? SignatureNote { get; init; }
}
