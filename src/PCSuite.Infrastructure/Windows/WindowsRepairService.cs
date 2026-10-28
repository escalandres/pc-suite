using PCSuite.Contracts;
using PCSuite.Core.Processes;
using PCSuite.Core.Windows;

namespace PCSuite.Infrastructure.Windows;

/// <summary>
/// Ejecuta los comandos del catálogo (Task 036) con el patrón de los escaneos
/// de ClamAV: runas directo (UAC por operación, lo genera Windows), consola
/// elevada VISIBLE (el progreso nativo de sfc/DISM lo dibuja la propia
/// herramienta) y SIN tope de reloj (lección 024: ningún número fijo cubre
/// trabajos legítimos largos sin matar trabajo válido).
///
/// El padre NO elevado puede esperar el exit del proceso elevado, pero no
/// puede matarlo (limitación documentada 024/025): job.cancel detiene la
/// espera y la consola queda a cargo del usuario — la UI lo dice honesto.
/// </summary>
public sealed class WindowsRepairService : IWindowsRepairService
{
    private readonly IProcessRunner _processRunner;

    public WindowsRepairService(IProcessRunner processRunner) =>
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public IReadOnlyList<WindowsCommand> List() => WindowsCommandCatalog.All;

    public async Task<WindowsRunOutcome> RunAsync(
        string commandId,
        CancellationToken cancellationToken = default)
    {
        // Allow-list ANTES de nada: id desconocido = cero UAC, cero proceso.
        var command = WindowsCommandCatalog.Find(commandId ?? string.Empty);
        if (command is null)
        {
            throw new WindowsRepairException(
                BridgeErrorCodes.WindowsFailed,
                $"Comando desconocido: '{commandId}'. Usa uno del catálogo.");
        }

        if (!File.Exists(command.FileName))
        {
            throw new WindowsRepairException(
                BridgeErrorCodes.WindowsFailed,
                $"No se encontró {Path.GetFileName(command.FileName)} en el sistema.");
        }

        ProcessResult result;
        try
        {
            result = await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = command.FileName,
                    Arguments = command.Arguments,
                    Timeout = null, // 024: sfc/DISM pueden tardar horas — el control es del usuario
                    WorkingDirectory = Environment.SystemDirectory,
                    RequiresElevation = true, // ShellExecute "runas": UAC por operación
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ElevationDeclinedException)
        {
            throw new WindowsRepairException(
                BridgeErrorCodes.ElevationCancelled,
                "El usuario canceló el control de cuentas de usuario.");
        }
        catch (OperationCanceledException)
        {
            throw; // job.cancel: el desenlace del job lo maneja la op
        }
        catch (Exception)
        {
            throw new WindowsRepairException(
                BridgeErrorCodes.WindowsFailed,
                $"No se pudo ejecutar {command.DisplayCommand}.");
        }

        return Summarize(command, result.ExitCode);
    }

    /// <summary>
    /// Exit code → mensaje controlado por comando (los códigos oficiales de
    /// sfc/DISM) con fallback genérico que SIEMPRE apunta al log oficial.
    /// </summary>
    private static WindowsRunOutcome Summarize(WindowsCommand command, int exitCode)
    {
        string message = command.Id switch
        {
            WindowsCommandCatalog.SfcScannow => exitCode switch
            {
                0 => "SFC terminó: no encontró violaciones de integridad.",
                1 => "SFC encontró y reparó archivos dañados.",
                2 => "SFC encontró violaciones que no pudo reparar — revisá el CBS.log (o ejecutá DISM RestoreHealth y repetí).",
                _ => $"SFC terminó con código {exitCode}.",
            },
            WindowsCommandCatalog.SfcVerifyOnly => exitCode switch
            {
                0 => "La verificación no encontró violaciones de integridad.",
                _ => $"La verificación encontró violaciones (código {exitCode}) — el detalle está en el CBS.log.",
            },
            WindowsCommandCatalog.DismCheckHealth => exitCode switch
            {
                0 => "El almacén de componentes no está marcado como corrupto.",
                _ => $"CheckHealth reportó una marca de corrupción (código {exitCode}).",
            },
            WindowsCommandCatalog.DismScanHealth => exitCode switch
            {
                0 => "El escaneo del almacén de componentes no halló corrupción reparable.",
                _ => $"El escaneo halló corrupción en el almacén (código {exitCode}) — RestoreHealth la repara.",
            },
            WindowsCommandCatalog.DismRestoreHealth => exitCode switch
            {
                0 => "La reparación del almacén de componentes se completó.",
                _ => $"RestoreHealth no pudo completar la reparación (código {exitCode}) — revisá el dism.log; si fue por fuente faltante, puede requerir un medio de instalación de Windows.",
            },
            _ => $"El comando terminó con código {exitCode}.",
        };

        return new WindowsRunOutcome(
            exitCode == 0,
            exitCode,
            message,
            $"Detalle completo en: {command.LogPath}");
    }
}
