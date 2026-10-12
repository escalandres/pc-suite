namespace PCSuite.Contracts;

/// <summary>Resultado de la operación plugin.list: colección de plugins instalados.</summary>
public sealed class PluginCollectionResult
{
    /// <summary>Lista de plugins instalados.</summary>
    public IReadOnlyList<PluginMetadata> Plugins { get; init; } = [];

    /// <summary>Instante UTC de la lista (cuando fue escaneado).</summary>
    public DateTimeOffset ScannedAt { get; init; }
}