namespace PCSuite.Contracts;

/// <summary>Antivirus registrados en el Centro de seguridad de Windows (WMI SecurityCenter2).</summary>
public sealed class AntivirusStatusResult
{
    /// <summary>Semáforo agregado: good/poor/notMonitored/snoozed/unknown.</summary>
    public string Status { get; init; } = "unknown";

    /// <summary>Display names registrados (p. ej. "Windows Defender").</summary>
    public IReadOnlyList<string> Products { get; init; } = [];
}
