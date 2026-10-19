using System.Runtime.InteropServices;

namespace PCSuite.Infrastructure.ClamAV;

/// <summary>
/// argv de ClamAV fijos por operación (Task 008 / §10.1/§53): el frontend solo
/// envía payloads estructurados (la ruta validada del customScan) y jamás
/// argumentos de comando; aquí no hay parsing de entrada, solo arreglos
/// inmutables que se pasan al sistema sin pasar por un shell.
/// </summary>
internal static class ClamAvArguments
{
    public static readonly string[] Version = ["--version"];

    /// <summary>freshclam sin argumentos: corre con WorkingDirectory = su carpeta (conf + BD).</summary>
    public static readonly string[] Freshclam = [];

    /// <summary>Flags base de escaneo (los objetivos van siempre después).</summary>
    private static readonly string[] BaseScanFlags = ["--recursive"];

    /// <summary>Flags para escaneos con progreso detallado (quick, custom).</summary>
    private static readonly string[] VerboseScanFlags = ["--recursive", "--verbose"];

    public static string[] QuickScan() => [.. VerboseScanFlags, .. QuickTargets()];

    /// <summary>Escaneo completo SIN --verbose para evitar millones de líneas de salida
    /// al escanear discos enteros. El parser usa el SCAN SUMMARY para contadores exactos.
    /// </summary>
    public static string[] FullScan(IReadOnlyList<string> driveRoots) => [.. BaseScanFlags, .. driveRoots];

    public static string[] CustomScan(string fullPath) => [.. VerboseScanFlags, fullPath];

    /// <summary>
    /// Inyecta la ruta del log de escaneo en el argv. Los escaneos corren
    /// elevados (decisión del autor 2026-10-18: leer también los archivos del
    /// sistema) y ShellExecute("runas") no permite redirigir stdout: el
    /// reporte de clamscan vive en este archivo, que el padre taila en vivo
    /// para mantener el progreso por archivo.
    /// </summary>
    public static string[] WithScanLog(string[] arguments, string logPath) =>
        [.. arguments, "--log=" + logPath];

    /// <summary>
    /// Escaneo rápido: escritorio, Descargas y temp del usuario. Descargas se
    /// resuelve por Known Folder (no existe SpecialFolder.Downloads y el nombre
    /// de carpeta está localizado: "Downloads"/"Descargas"/.); si no se puede
    /// resolver, se omite el objetivo y los otros dos siguen presentes.
    /// </summary>
    private static string[] QuickTargets()
    {
        var targets = new List<string>(3);
        AddIfExists(targets, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        if (DownloadsFolder() is { } downloads)
        {
            AddIfExists(targets, downloads);
        }

        AddIfExists(targets, Path.GetTempPath());
        return [.. targets];
    }

    private static void AddIfExists(List<string> targets, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !targets.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            targets.Add(path);
        }
    }

    // Known Folder API: FOLDERID_Downloads. Sin esto, la carpeta Descargas no
    // se puede resolver con Environment.GetFolderPath (enum sin Downloads).
    private static readonly Guid DownloadsFolderGuid = new("374DE290-123F-4565-9164-39C4925E467B");

    private static string? DownloadsFolder()
    {
        try
        {
            var hr = SHGetKnownFolderPath(DownloadsFolderGuid, 0, IntPtr.Zero, out var path);
            if (hr != 0 || path == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(path);
            }
            finally
            {
                CoTaskMemFree(path);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint flags,
        IntPtr token,
        out IntPtr path);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pointer);
}
