namespace PCSuite.Contracts;

/// <summary>Resultado de <see cref="OperationIds.ClamavStatus"/> (Task 008).</summary>
public sealed class ClamAvStatusResult
{
    /// <summary>True si clamscan.exe se localizó (PATH o directorios comunes).</summary>
    public bool Installed { get; init; }

    /// <summary>Ruta resolvida de clamscan.exe; se omite si no está instalado.</summary>
    public string? Path { get; init; }

    /// <summary>Versión del motor (clamscan --version); se omite si es desconocida.</summary>
    public string? Version { get; init; }

    /// <summary>Build de definiciones conocidas; se omite si es desconocida.</summary>
    public string? Definitions { get; init; }

    /// <summary>True si freshclam.exe existe junto a clamscan (posible actualización de BD).</summary>
    public bool CanUpdateDefinitions { get; init; }
}
