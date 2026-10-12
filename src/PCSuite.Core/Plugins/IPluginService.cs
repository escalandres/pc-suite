namespace PCSuite.Core.Plugins;

using PCSuite.Contracts;

/// <summary>
/// Interfaz que define el servicio de plugins para la gestión del ciclo de vida
/// y estado de los plugins extensibles en PCSuite.
/// </summary>
public interface IPluginService
{
    /// <summary>Carga un plugin por su id. Intenta descubrir y cargar el assembly/implementación.</summary>
    /// <param name="pluginId">Identificador único del plugin a cargar.</param>
    /// <returns>Estado actualizado del plugin después del intento de carga.</returns>
    PluginStatus LoadPlugin(string pluginId);

    /// <summary>Descarga/descarta un plugin por su id.</summary>
    /// <param name="pluginId">Identificador único del plugin a descargar.</param>
    /// <returns>Estado actualizado del plugin después del descarte.</returns>
    PluginStatus UnloadPlugin(string pluginId);

    /// <summary>Obtiene el estado del plugin especificado.</summary>
    /// <param name="pluginId">Identificador único del plugin.</param>
    /// <returns>Estado actual del plugin.</returns>
    PluginStatus GetPluginStatus(string pluginId);

    /// <summary>Lista todos los plugins instalados con sus metadatos.</summary>
    /// <returns>Colección de PluginMetadata con información de todos los plugins descubiertos.</returns>
    IReadOnlyCollection<PluginMetadata> ListInstalledPlugins();

    /// <summary>Comprueba si hay actualizaciones disponibles para un plugin.</summary>
    /// <param name="pluginId">Identificador del plugin a verificar.</param>
    /// <returns>Resultado con información de update disponible o no.</returns>
    PluginOperationResult CheckUpdate(string pluginId);

    /// <summary>Lanza la ejecución de un plugin.</summary>
    /// <param name="pluginId">Identificador del plugin a lanzar.</param>
    /// <returns>Resultado de la operación de lanzamiento.</returns>
    PluginOperationResult Launch(string pluginId);
}