using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.WinGet;

/// <summary>
/// Lectura CONTROLADA de la salida de winget: últimas líneas para el detalle
/// de fallos (sin stack traces, sin caracteres de control, tope de tamaño) y
/// detección de "faltan privilegios" para decidir si merece la pena pedir UAC.
/// </summary>
internal static class WinGetOutput
{
    /// <summary>
    /// Marcadores de falta de permisos en la salida (HRESULTs en hex + frases
    /// de ERROR_ACCESS_DENIED en inglés y español, y strerror(EACCES) de los
    /// binarios CRT como freshclam). Un falso positivo solo provoca un UAC
    /// innecesario cuyo reejecución devuelve fallo estructurado; un falso
    /// negativo deja el fallo como resultado sin UAC (sin riesgo).
    /// </summary>
    private static readonly string[] ElevationMarkers =
    [
        "0x80070005",
        "0x800702e4",
        "access is denied",
        "access denied",
        "acceso denegado",
        "permission denied",
        "permiso denegado",
        // freshclam-specific: errores genéricos que a menudo son por permisos
        // al escribir en Program Files / directorio de base de datos
        "error: update failed",
        "error: update failed.",
        "can't create",
        "can't open",
        "could not open",
        "failed to create",
        "failed to open",
        "write error",
        "unable to create",
        "unable to open",
        "no se puede crear",
        "no se puede abrir",
        "error al crear",
        "error al abrir",
    ];

    /// <summary>True si el fallo parece de permisos (candidato a reintentar elevado).</summary>
    public static bool NeedsElevation(ProcessResult result) =>
        result.ExitCode is WinGetExitCodes.AccessDenied
            or WinGetExitCodes.ElevationRequired
            or WinGetExitCodes.HResultAccessDenied
            or WinGetExitCodes.HResultElevationRequired
        || ContainsMarker(result.StandardOutput)
        || ContainsMarker(result.StandardError);

    /// <summary>
    /// Última línea no vacía de stdout+stderr, saneada (sin controles,
    /// máximo 300 caracteres). null si no hay nada que mostrar.
    /// </summary>
    public static string? Detail(ProcessResult result)
    {
        var combined = result.StandardOutput + "\n" + result.StandardError;
        string? last = null;

        foreach (var raw in combined.Split('\n'))
        {
            var line = Sanitize(raw);
            if (line.Length > 0)
            {
                last = line;
            }
        }

        return last;
    }

    /// <summary>Mensaje controlado para un fallo sin detalle utilizable.</summary>
    public static string FailureMessage(ProcessResult result) =>
        Detail(result) ?? $"winget falló con el código de salida {result.ExitCode}.";

    private static bool ContainsMarker(string? text) =>
        !string.IsNullOrEmpty(text)
        && ElevationMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string Sanitize(string line)
    {
        var cleaned = new string(line.Trim().Where(character => !char.IsControl(character)).ToArray());
        return cleaned.Length <= 300 ? cleaned : cleaned[..300] + "…";
    }
}
