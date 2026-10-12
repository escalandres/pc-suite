namespace PCSuite.Core.ClamAV;

/// <summary>
/// Progreso de actualización de definiciones ClamAV (freshclam).
/// Se reporta en tiempo real mientras freshclam descarga y actualiza las bases de datos.
/// </summary>
public sealed class ClamAvFreshclamProgress
{
    /// <summary>Archivo actual que se está descargando/procesando (p. ej. "main.cvd", "daily.cvd").</summary>
    public string? CurrentFile { get; init; }

    /// <summary>Porcentaje de descarga del archivo actual (0-100). null si no aplica.</summary>
    public int? Percent { get; init; }

    /// <summary>Bytes descargados del archivo actual.</summary>
    public long? DownloadedBytes { get; init; }

    /// <summary>Total de bytes del archivo actual (si se conoce).</summary>
    public long? TotalBytes { get; init; }

    /// <summary>Velocidad de descarga en bytes/segundo.</summary>
    public long? BytesPerSecond { get; init; }

    /// <summary>Mensaje de estado legible (p. ej. "Descargando main.cvd...", "Verificando firmas...").</summary>
    public string? StatusText { get; init; }

    /// <summary>Indica si la operación ha finalizado (éxito o fallo).</summary>
    public bool IsComplete { get; init; }

    /// <summary>Si IsComplete=true, indica si la actualización tuvo éxito.</summary>
    public bool? Succeeded { get; init; }

    /// <summary>Mensaje de detalle si falló o terminó.</summary>
    public string? Detail { get; init; }
}