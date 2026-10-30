using PCSuite.Core.Windows;

namespace PCSuite.Infrastructure.Windows;

/// <summary>
/// Catálogo de comandos de reparación de Windows (Task 036). CADA entrada es
/// una operación allow-listed con argv fijo: el frontend viaja con el id y el
/// argv jamás sale de este archivo — no existe ruta a un comando arbitrario.
///
/// AGREGAR UN COMANDO NUEVO = una entrada aquí (y, si querés mensaje de exit
/// code propio, un case en WindowsRepairService.Summarize). Nada más: ni ops
/// nuevas, ni superficie del puente, ni helper elevado — se ejecutan como los
/// escaneos de ClamAV (runas directo + consola visible).
///
/// NOTA sfc.exe: la herramienta de Microsoft escribe su salida directo a la
/// consola elevada (no redirige); el detalle queda en CBS.log. DISM soporta
/// /LogPath pero el v1 usa su log por defecto — la consola nativa es la
/// fuente visual del progreso.
/// </summary>
public static class WindowsCommandCatalog
{
    private static readonly string System32 = Environment.SystemDirectory;

    public const string SfcScannow = "sfc-scannow";
    public const string SfcVerifyOnly = "sfc-verifyonly";
    public const string DismCheckHealth = "dism-checkhealth";
    public const string DismScanHealth = "dism-scanhealth";
    public const string DismRestoreHealth = "dism-restorehealth";
    public const string DismAnalyzeComponentStore = "dism-analyzecomponentstore";
    public const string DismStartComponentCleanup = "dism-startcomponentcleanup";

    private const string SystemCategory = "Reparación del sistema";

    private static readonly string CbsLog = Path.Combine(
        Environment.GetEnvironmentVariable("SystemRoot") ?? Environment.SystemDirectory,
        "Logs", "CBS", "CBS.log");

    private static readonly string DismLog = Path.Combine(
        Environment.GetEnvironmentVariable("SystemRoot") ?? Environment.SystemDirectory,
        "Logs", "DISM", "dism.log");

    public static IReadOnlyList<WindowsCommand> All { get; } =
    [
        new(
            SfcScannow,
            "SFC — comprobar y reparar archivos de sistema",
            "Comprueba la integridad de los archivos de sistema protegidos y repara los que falten o estén dañados (p. ej. un explorer.exe dañado). Es el clásico sfc /scannow: la reparación usa el almacén de componentes como fuente.",
            SystemCategory,
            Path.Combine(System32, "sfc.exe"),
            ["/scannow"],
            "sfc /scannow",
            "10–30 min",
            Heavy: true,
            CbsLog),

        new(
            SfcVerifyOnly,
            "SFC — comprobar SIN reparar",
            "Igual que el anterior pero sin tocar nada: solo detecta y reporta violaciones de integridad al CBS.log. Útil para diagnosticar antes de reparar.",
            SystemCategory,
            Path.Combine(System32, "sfc.exe"),
            ["/verifyonly"],
            "sfc /verifyonly",
            "10–30 min",
            Heavy: true,
            CbsLog),

        new(
            DismCheckHealth,
            "DISM — chequeo rápido del almacén",
            "Chequeo casi instantáneo: solo lee la marca de corrupción que deja un escaneo anterior. No repara ni escanea por sí mismo.",
            SystemCategory,
            Path.Combine(System32, "Dism.exe"),
            ["/Online", "/Cleanup-Image", "/CheckHealth"],
            "DISM /Online /Cleanup-Image /CheckHealth",
            "segundos",
            Heavy: false,
            DismLog),

        new(
            DismScanHealth,
            "DISM — escanear el almacén de componentes",
            "Escanea el almacén de componentes del sistema en busca de corrupción (no repara). Es el paso previo al RestoreHealth.",
            SystemCategory,
            Path.Combine(System32, "Dism.exe"),
            ["/Online", "/Cleanup-Image", "/ScanHealth"],
            "DISM /Online /Cleanup-Image /ScanHealth",
            "minutos",
            Heavy: false,
            DismLog),

        new(
            DismRestoreHealth,
            "DISM — reparar el almacén de componentes",
            "REPARA el almacén de componentes (la fuente que usa sfc): si está dañado, sfc no puede reparar nada. Puede descargar piezas sanas desde Windows Update.",
            SystemCategory,
            Path.Combine(System32, "Dism.exe"),
            ["/Online", "/Cleanup-Image", "/RestoreHealth"],
            "DISM /Online /Cleanup-Image /RestoreHealth",
            "10–60 min",
            Heavy: true,
            DismLog),

        new(
            DismAnalyzeComponentStore,
            "DISM — analizar el almacén de componentes",
            "Analiza el almacén de componentes del sistema: muestra el tamaño actual, el tamaño después de una limpieza y si se puede reducir. No modifica nada.",
            SystemCategory,
            Path.Combine(System32, "Dism.exe"),
            ["/Online", "/Cleanup-Image", "/AnalyzeComponentStore"],
            "DISM /Online /Cleanup-Image /AnalyzeComponentStore",
            "minutos",
            Heavy: false,
            DismLog),

        new(
            DismStartComponentCleanup,
            "DISM — limpiar el almacén de componentes",
            "Inicia la limpieza del almacén de componentes: elimina versiones anteriores de archivos actualizados y reduce el tamaño del almacén. Puede tardar varios minutos.",
            SystemCategory,
            Path.Combine(System32, "Dism.exe"),
            ["/Online", "/Cleanup-Image", "/StartComponentCleanup"],
            "DISM /Online /Cleanup-Image /StartComponentCleanup",
            "5–15 min",
            Heavy: true,
            DismLog),
    ];

    /// <summary>Resuelve un id del catálogo (o null si no está: SIN UAC para desconocidos).</summary>
    public static WindowsCommand? Find(string commandId) =>
        All.FirstOrDefault(command => string.Equals(command.Id, commandId, StringComparison.Ordinal));
}
