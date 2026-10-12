namespace PCSuite.Core.Plugins;

using PCSuite.Contracts;

/// <summary>
/// Interfaz base que define el contrato para un plugin extensible en PCSuite.
/// Los plugins deben implementar esta interfaz y registrarse en el
/// PluginService para su descubrimiento y ciclo de vida.
/// </summary>
public interface IPlugin
{
    /// <summary>Id Ãºnico del plugin (clave de registro).</summary>
    string Id { get; }

    /// <summary>Nombre visible del plugin.</summary>
    string Name { get; }

    /// <summary>VersiÃ³n actual del plugin.</summary>
    string Version { get; }

    /// <summary>Estado actual del plugin.</summary>
    PluginStatus Status { get; }

    /// <summary>Indica si hay actualizaciÃ³n disponible.</summary>
    bool UpdateAvailable { get; }

    /// <summary>VersiÃ³n disponible (si hay update).</summary>
    string? AvailableVersion { get; }

    /// <summary>Obtiene el payload tipo para la operaciÃ³n correspondiente al plugin.</summary>
    /// <param name="operationId">Identificador de la operaciÃ³n (plugin.list, plugin.checkUpdate, plugin.launch).</summary>
    /// <returns>Payload tipado para la operaciÃ³n solicitada, o null si no estÃ¡ soportado.</returns>
    object? GetPayload(string operationId);
}
