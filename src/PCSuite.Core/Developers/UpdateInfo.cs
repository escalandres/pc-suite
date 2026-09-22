namespace PCSuite.Core.Developers;

/// <summary>
/// Información de una comprobación de actualización de una herramienta de
/// desarrollador: versión instalada, última publicada y si hay actualización
/// disponible.
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
