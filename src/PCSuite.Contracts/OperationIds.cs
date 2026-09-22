namespace PCSuite.Contracts;

/// <summary>
/// Allow-listed operation identifiers. The frontend may only invoke
/// operations registered here; arbitrary command execution is never exposed.
/// </summary>
public static class OperationIds
{
    public const string GetSystemInfo = "system.info";

    /// <summary>Devuelve los metadatos de todas las tools registradas.</summary>
    public const string ToolsList = "tools.list";

    /// <summary>Busca tools por texto libre sobre sus metadatos.</summary>
    public const string ToolsSearch = "tools.search";

    /// <summary>Ejecuta una tool por su id con un payload tipado.</summary>
    public const string ToolExecute = "tool.execute";

    /// <summary>Estado (instalación, versión, ruta) de las tools de desarrollador.</summary>
    public const string DeveloperList = "developer.list";

    /// <summary>Comprueba actualizaciones de una tool de desarrollador.</summary>
    public const string DeveloperCheckUpdate = "developer.checkUpdate";

    /// <summary>Ejecuta el self-update nativo de una tool de desarrollador.</summary>
    public const string DeveloperUpdate = "developer.update";

    /// <summary>Lanza la interfaz interactiva (REPL/TUI) de una tool.</summary>
    public const string DeveloperLaunch = "developer.launch";
}
