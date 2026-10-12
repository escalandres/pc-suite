namespace PCSuite.Core.ClamAV;

/// <summary>
/// Progreso de escaneo ClamAV (clamscan).
/// Se reporta en tiempo real mientras clamscan recorre archivos y directorios.
/// </summary>
public sealed class ClamAvScanProgress
{
    /// <summary>Archivo actual siendo escaneado.</summary>
    public string? CurrentFile { get; init; }

    /// <summary>Directorio actual siendo escaneado.</summary>
    public string? CurrentDirectory { get; init; }

    /// <summary>Archivos escaneados hasta el momento.</summary>
    public long ScannedFiles { get; init; }

    /// <summary>Directorios escaneados hasta el momento.</summary>
    public long ScannedDirectories { get; init; }

    /// <summary>Amenazas encontradas hasta el momento.</summary>
    public long InfectedFiles { get; init; }

    /// <summary>Mensaje de estado legible (p. ej. "Escaneando C:\Users\X\Downloads\...").</summary>
    public string? StatusText { get; init; }

    /// <summary>Indica si la operación ha finalizado (éxito o fallo).</summary>
    public bool IsComplete { get; init; }

    /// <summary>Si IsComplete=true, indica si el escaneo tuvo éxito.</summary>
    public bool? Succeeded { get; init; }

    /// <summary>Mensaje de detalle si falló o terminó.</summary>
    public string? Detail { get; init; }

    /// <summary>Tipo de escaneo (quick, full, custom).</summary>
    public string? ScanKind { get; init; }

    /// <summary>Porcentaje de progreso estimado (0-100). Ausente si no se puede calcular.</summary>
    public int? Percent { get; init; }
}