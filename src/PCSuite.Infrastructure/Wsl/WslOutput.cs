using PCSuite.Core.Processes;
using PCSuite.Infrastructure.WinGet;

namespace PCSuite.Infrastructure.Wsl;

/// <summary>
/// Lectura CONTROLADA de la salida de wsl. Durante la transición (Task 007)
/// delega en <see cref="WinGetOutput"/> porque son exactamente las mismas
/// cadenas y reglas (marcadores de permisos, última línea saneada): una
/// propiedad de enlace por método, 0 lógica duplicada. Si esa capa se elimina,
/// se mueven aquí tal cual.
/// </summary>
internal static class WslOutput
{
    /// <summary>True si el fallo parece de permisos (candidato a reintentar elevado).</summary>
    public static bool NeedsElevation(ProcessResult result) =>
        WinGetOutput.NeedsElevation(result);

    /// <summary>Última línea no vacía de stdout+stderr, saneada; null si no hay nada.</summary>
    public static string? Detail(ProcessResult result) =>
        WinGetOutput.Detail(result);

    /// <summary>
    /// Mensaje para un fallo sin resultado o sin detalle utilizable: si la
    /// salida identifica el fallo (permisos/…) va ese texto; si no, el código
    /// de salida; sin proceso, el genérico.
    /// </summary>
    public static string FailureMessage(ProcessResult? result)
    {
        if (result is null)
        {
            return "La operación wsl falló.";
        }

        return Detail(result) ?? $"wsl falló con el código de salida {result.ExitCode}.";
    }
}
