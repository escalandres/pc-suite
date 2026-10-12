namespace PCSuite.Contracts;

/// <summary>Resultado de una operación de checkUpdate o launch de plugin.</summary>
public sealed class PluginOperationResult
{
    /// <summary>Indica si la operación succeeded.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Mensaje descriptivo para la UI (toast, etc.).</summary>
    public string? Detail { get; init; }

    /// <summary>Versión actual después de la operación.</summary>
    public string? CurrentVersion { get; init; }

    /// <summary>Versión más reciente disponible.</summary>
    public string? LatestVersion { get; init; }
}