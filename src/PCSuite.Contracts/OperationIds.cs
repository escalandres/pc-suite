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

    // --- WinGet (Task 006 / Stage 7) -------------------------------------
    // El frontend envía SIEMPRE payloads estructurados (p. ej. {"packageId"}):
    // jamás una línea "winget …" arbitraria; .NET valida la entrada y construye
    // los argumentos permitidos.

    /// <summary>Detección: winget instalado/no, versión, ruta y si corre elevado.</summary>
    public const string WingetDetect = "winget.detect";

    /// <summary>Busca paquetes en el índice winget (payload: {"query"}).</summary>
    public const string WingetSearch = "winget.search";

    /// <summary>Lista de paquetes instalados desde el origen winget.</summary>
    public const string WingetList = "winget.list";

    /// <summary>Paquetes con actualización disponible.</summary>
    public const string WingetUpdates = "winget.updates";

    /// <summary>Instala un paquete (payload: {"packageId"}); puede pedir UAC.</summary>
    public const string WingetInstall = "winget.install";

    /// <summary>Actualiza un paquete concreto (payload: {"packageId"}); puede pedir UAC.</summary>
    public const string WingetUpgrade = "winget.upgrade";

    /// <summary>Actualiza todo lo actualizable; puede pedir UAC.</summary>
    public const string WingetUpgradeAll = "winget.upgradeAll";

    /// <summary>Desinstala un paquete (payload: {"packageId"}); puede pedir UAC.</summary>
    public const string WingetUninstall = "winget.uninstall";

    // Operaciones WSL (Task 007): TODAS de payload cero — el frontend no
    // envía argumentos (§12/§53) y los argv son fijos en WslArguments.
    /// <summary>Estado de WSL: instalación, versión, distribución por defecto y running/stopped.</summary>
    public const string WslStatus = "wsl.status";

    /// <summary>Distribuciones WSL con su estado y versión de plataforma.</summary>
    public const string WslList = "wsl.list";

    /// <summary>Actualiza el componente WSL (wsl --update); puede pedir UAC.</summary>
    public const string WslUpdate = "wsl.update";

    /// <summary>Apaga el subsistema WSL (wsl --shutdown); nunca eleva.</summary>
    public const string WslShutdown = "wsl.shutdown";

    /// <summary>Arranca la distribución por defecto (wsl --exec true); nunca eleva.</summary>
    public const string WslStart = "wsl.start";

    // Operaciones ClamAV (Task 008 / §13): la ruta del customScan viaja como
    // payload estructurado validado en el servicio; el resto es de payload cero
    // y los argv (clamscan/freshclam) son arreglos fijos, nunca una línea
    // "clamscan …" arbitraria.

    /// <summary>Detección: clamscan instalado/no, ruta, versión y definiciones.</summary>
    public const string ClamavStatus = "clamav.status";

    /// <summary>Instala ClamAV (paquete fijo Cisco.ClamAV vía winget); puede pedir UAC.</summary>
    public const string ClamavInstall = "clamav.install";

    /// <summary>Actualiza las definiciones con freshclam; puede pedir UAC por permisos.</summary>
    public const string ClamavUpdateDefinitions = "clamav.updateDefinitions";

    /// <summary>Escaneo rápido de ubicaciones comunes del usuario.</summary>
    public const string ClamavQuickScan = "clamav.quickScan";

    /// <summary>Escaneo completo de los discos fijos.</summary>
    public const string ClamavFullScan = "clamav.fullScan";

    /// <summary>Escaneo de una ruta absoluta existente (payload: {"path"}).</summary>
    public const string ClamavCustomScan = "clamav.customScan";

    /// <summary>Historial de escaneos persistido (más reciente primero).</summary>
    public const string ClamavHistory = "clamav.history";
}
