namespace PCSuite.Contracts;

/// <summary>
/// Resultado de developer.checkUpdate. Una comprobación degradada (sin red,
/// sin fuente de updates conocida o herramienta no instalada) no es un error:
/// responde con <see cref="Supported"/> en false y sin versión disponible.
/// </summary>
public sealed class UpdateCheckResult
{
    /// <summary>Id de la herramienta comprobada.</summary>
    public string ToolId { get; init; } = "";

    /// <summary>Versión instalada; null si no se pudo determinar.</summary>
    public string? CurrentVersion { get; init; }

    /// <summary>Última versión publicada; null si la comprobación degradó.</summary>
    public string? LatestVersion { get; init; }

    /// <summary>True si hay una versión más reciente disponible.</summary>
    public bool UpdateAvailable { get; init; }

    /// <summary>
    /// False si la herramienta no tiene fuente de updates conocida, no está
    /// instalada o la consulta falló (sin red, timeout...).
    /// </summary>
    public bool Supported { get; init; }
}
