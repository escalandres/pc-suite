namespace PCSuite.Core.Plugins;

using PCSuite.Contracts;

/// <summary>
/// Interfaz base que define el contrato para un plugin extensible en PCSuite.
/// Los plugins deben implementar esta interfaz y registrarse en el
/// PluginService para su descubrimiento y ciclo de vida.
/// </summary>
public interface IPlugin
{
    /// <summary>Id único del plugin (clave de registro).</summary>
    string Id { get; }

    /// <summary>Nombre visible del plugin.</summary>
    string Name { get; }

    /// <summary>Versión actual del plugin.</summary>
    string Version { get; }

    /// <summary>Estado actual del plugin.</summary>
    PluginStatus Status { get; }

    /// <summary>Indica si hay actualización disponible.</summary>
    bool UpdateAvailable { get; }

    /// <summary>Versión disponible (si hay update).</summary>
    string? AvailableVersion { get; }

    /// <summary>Obtiene el payload tipo para la operación correspondiente al plugin.</summary>
    /// <param name="operationId">Identificador de la operación (plugin.list, plugin.checkUpdate, plugin.launch).</summary>
    /// <returns>Payload tipado para la operación solicitada, o null si no está soportado.</returns>
    object? GetPayload(string operationId);
}
