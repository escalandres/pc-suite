using PCSuite.Core.Processes;
using PCSuite.Infrastructure.WinGet;

namespace PCSuite.Infrastructure.ClamAV;

/// <summary>
/// Lectura CONTROLADA de la salida de ClamAV. Igual que WslOutput (Task 007),
/// delega en <see cref="WinGetOutput"/>: mismas reglas (marcadores de permisos,
/// última línea saneada) con 0 lógica duplicada.
/// </summary>
internal static class ClamAvOutput
{
    /// <summary>True si el fallo parece de permisos (candidato a reintentar elevado).</summary>
    public static bool NeedsElevation(ProcessResult result) =>
        WinGetOutput.NeedsElevation(result);

    /// <summary>Última línea no vacía de stdout+stderr, saneada; null si no hay nada.</summary>
    public static string? Detail(ProcessResult result) =>
        WinGetOutput.Detail(result);

    /// <summary>
    /// Mensaje para un fallo sin resultado o sin detalle utilizable: si la
    /// salida identifica el fallo (permisos/.) va ese texto; si no, el código
    /// de salida; sin proceso, el genérico.
    /// </summary>
    public static string FailureMessage(ProcessResult? result)
    {
        if (result is null)
        {
            return "La operación de ClamAV falló.";
        }

        return Detail(result) ?? $"ClamAV falló con el código de salida {result.ExitCode}.";
    }
}
