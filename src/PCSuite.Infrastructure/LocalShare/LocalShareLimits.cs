using PCSuite.Core.LocalShare;

namespace PCSuite.Infrastructure.LocalShare;

/// <summary>
/// Validación de límites de transferencias entrantes (Task 016.7.2).
/// El receptor es quien debe protegerse: comprueba el límite de tamaño
/// configurado y el espacio real disponible en la unidad de la carpeta
/// de recepción ANTES de aceptar la solicitud (antes incluso de pedírselo
/// al usuario). Devuelve un error controlado o null si todo pasa.
/// </summary>
public static class LocalShareLimits
{
    /// <summary>
    /// Margen de seguridad sobre el espacio libre: el sistema necesita
    /// margen para los temporales y para no dejar la unidad al límite.
    /// </summary>
    private const double FreeSpaceMargin = 0.95;

    /// <summary>
    /// Comprueba una solicitud entrante. Devuelve (code, message) controlado
    /// para el frame ERROR, o null si la transferencia puede continuar.
    /// </summary>
    public static (string Code, string Message)? ValidateIncoming(
        long totalBytes,
        LocalShareSettings settings,
        string downloadDirectory)
    {
        // Límite de tamaño configurado por el usuario (0 = sin límite).
        if (settings.MaxTransferSizeBytes > 0 && totalBytes > settings.MaxTransferSizeBytes)
        {
            return (
                "size_limit",
                $"La transferencia ({totalBytes / 1024.0 / 1024.0:F1} MB) supera el límite configurado en el receptor ({settings.MaxTransferSizeBytes / 1024.0 / 1024.0:F1} MB).");
        }

        // Espacio real disponible en la unidad de la carpeta de recepción.
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(downloadDirectory));
            var drive = string.IsNullOrEmpty(root)
                ? null
                : DriveInfo.GetDrives().FirstOrDefault(d =>
                    string.Equals(d.Name, root, StringComparison.OrdinalIgnoreCase));
            if (drive is not null && drive.IsReady)
            {
                var usable = (long)(drive.AvailableFreeSpace * FreeSpaceMargin);
                if (totalBytes > usable)
                {
                    return (
                        "no_space",
                        $"No hay espacio suficiente en '{root}' para recibir {totalBytes / 1024.0 / 1024.0:F1} MB (libre: {drive.AvailableFreeSpace / 1024.0 / 1024.0:F1} MB).");
                }
            }
        }
        catch
        {
            // Best-effort: si no se puede consultar la unidad, no se bloquea
            // la transferencia; el fallo de disco se manejará al escribir.
        }

        return null;
    }
}
