namespace PCSuite.Core.Developers;

/// <summary>
/// Información de una comprobación de actualización. La Task 004 solo
/// detecta e informa versión; este modelo se usará cuando se implementen
/// las actualizaciones.
/// </summary>
public sealed class UpdateInfo
{
    /// <summary>Versión actualmente instalada.</summary>
    public required string CurrentVersion { get; init; }

    /// <summary>Última versión disponible.</summary>
    public required string LatestVersion { get; init; }

    /// <summary>True si <see cref="LatestVersion"/> es distinta de la instalada.</summary>
    public bool UpdateAvailable { get; init; }
}
