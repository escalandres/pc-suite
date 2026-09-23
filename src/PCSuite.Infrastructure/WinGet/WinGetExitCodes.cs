namespace PCSuite.Infrastructure.WinGet;

/// <summary>
/// Códigos de salida del CLI winget (medidos empíricamente en v1.29, locale
/// es-ES) y clasificación de fallos que indican falta de privilegios.
/// </summary>
internal static class WinGetExitCodes
{
    /// <summary>Ninguna coincidencia (search/list/uninstall sin resultados).</summary>
    public const int NoApplicationsFound = -1978335212;

    /// <summary>No hay actualizaciones aplicables (upgrade de un paquete ya al día).</summary>
    public const int NoUpdateAvailable = -1978335189;

    /// <summary>ERROR_ACCESS_DENIED (5) y ERROR_ELEVATION_REQUIRED (740).</summary>
    public const int AccessDenied = 5;

    public const int ElevationRequired = 740;

    /// <summary>HRESULT 0x80070005 (E_ACCESSDENIED) tal como winget lo devuelve como entero.</summary>
    public const int HResultAccessDenied = unchecked((int)0x80070005);

    /// <summary>HRESULT 0x800702E4 (ERROR_ELEVATION_REQUIRED en some runtimes).</summary>
    public const int HResultElevationRequired = unchecked((int)0x800702E4);
}
