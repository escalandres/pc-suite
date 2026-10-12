namespace PCSuite.Contracts;

/// <summary>Payload de la operación plugin.checkUpdate.</summary>
public sealed class PluginCheckUpdatePayload
{
    /// <summary>Id del plugin del cual comprobar la actualización.</summary>
    public string PluginId { get; init; } = string.Empty;
}

/// <summary>Payload de la operación plugin.launch.</summary>
public sealed class PluginLaunchPayload
{
    /// <summary>Id del plugin a lanzar.</summary>
    public string PluginId { get; init; } = string.Empty;
}