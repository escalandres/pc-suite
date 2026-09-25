namespace PCSuite.Contracts;

/// <summary>Una entrada del historial de escaneos (clamav.history).</summary>
public sealed class ClamAvHistoryEntryResult
{
    /// <summary>Marca de tiempo UTC del escaneo (ISO 8601).</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>quick | full | custom.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Ruta escaneada en customScan; se omite en quick/full.</summary>
    public string? Path { get; init; }

    /// <summary>True si el escaneo produjo un resultado interpretable.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Archivos escaneados.</summary>
    public int ScannedFiles { get; init; }

    /// <summary>Archivos infectados.</summary>
    public int InfectedFiles { get; init; }

    /// <summary>Detalle controlado (parcial/fallo); se omite si no hay.</summary>
    public string? Detail { get; init; }

    /// <summary>Amenazas registradas (recortada como en el escaneo en vivo).</summary>
    public IReadOnlyList<ClamAvThreatResult> Threats { get; init; } = [];
}
