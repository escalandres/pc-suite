namespace PCSuite.Contracts;

/// <summary>Resultado de un escaneo ClamAV (clamav.quickScan/fullScan/customScan).</summary>
public sealed class ClamAvScanResult
{
    /// <summary>True si el escaneo produjo un resultado interpretable (amenazas o no).</summary>
    public bool Succeeded { get; init; }

    /// <summary>Código de salida de clamscan (0: limpio, 1: amenazas, 2+: parcial/error).</summary>
    public int ExitCode { get; init; }

    /// <summary>Archivos escaneados (resumen de clamscan).</summary>
    public int ScannedFiles { get; init; }

    /// <summary>Directorios escaneados (resumen de clamscan).</summary>
    public int ScannedDirectories { get; init; }

    /// <summary>Archivos infectados (resumen de clamscan; exacto aunque la lista venga recortada).</summary>
    public int InfectedFiles { get; init; }

    /// <summary>Versión del motor en este escaneo; se omite si es desconocida.</summary>
    public string? EngineVersion { get; init; }

    /// <summary>Número de firmas conocidas; se omite si es desconocido.</summary>
    public int? KnownViruses { get; init; }

    /// <summary>Detalle controlado (p. ej. escaneo parcial por permisos); se omite si no hay.</summary>
    public string? Detail { get; init; }

    /// <summary>Firmas detectadas (recortada a un tope razonable; usa InfectedFiles para el total).</summary>
    public IReadOnlyList<ClamAvThreatResult> Threats { get; init; } = [];
}
