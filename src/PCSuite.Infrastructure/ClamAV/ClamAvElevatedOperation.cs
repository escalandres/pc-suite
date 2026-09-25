using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Processes;

namespace PCSuite.Infrastructure.ClamAV;

/// <summary>
/// freshclam que corre DENTRO del proceso elevado (Task 008 sobre el marco del
/// Task 005). Seguridad en profundidad:
/// - Su ID ya validó el padre contra ElevatedOperationIds.Allowed antes de
///   pedir UAC, y el proceso elevado lo vuelve a comprobar al servir;
/// - el payload se IGNORA por completo (la operación no recibe entrada) y el
///   argv es un arreglo fijo (vacío: freshclam usa su carpeta de trabajo):
///   no existe ruta a un comando arbitrario (nunca un shell);
/// - devuelve resultado estructurado (ClamAvMutationResult) con mensaje
///   controlado: jamás salidas crudas ni stack traces.
/// </summary>
public sealed class ClamAvElevatedOperation : IElevatedOperation
{
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(900);

    private readonly ExecutableLocator _locator;
    private readonly IProcessRunner _processRunner;
    private readonly IReadOnlyList<string> _installDirectories;

    public ClamAvElevatedOperation(
        ExecutableLocator? locator = null,
        IProcessRunner? processRunner = null,
        IReadOnlyList<string>? installDirectories = null)
    {
        _locator = locator ?? new ExecutableLocator();
        _processRunner = processRunner ?? new ProcessRunner();
        _installDirectories = installDirectories ?? ClamAvPaths.DefaultInstallDirectories();
    }

    public string Id => ElevatedOperationIds.ClamavUpdateDefinitions;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return OperationResult.Fail(BridgeErrorCodes.NotSupported, "Solo disponible en Windows.");
        }

        try
        {
            var clamscan = ClamAvPaths.FindClamScan(_locator, _installDirectories);
            if (clamscan is null)
            {
                return OperationResult.Fail(BridgeErrorCodes.NotInstalled, "ClamAV no está instalado.");
            }

            var freshclam = ClamAvPaths.FindFreshclam(clamscan);
            if (freshclam is null)
            {
                return OperationResult.Fail(
                    BridgeErrorCodes.NotInstalled,
                    "No se encontró freshclam.exe junto a clamscan.exe.");
            }

            ProcessResult result;
            try
            {
                result = await _processRunner.ExecuteAsync(
                    new ProcessRequest
                    {
                        FileName = freshclam,
                        Arguments = ClamAvArguments.Freshclam,
                        Timeout = UpdateTimeout,
                        WorkingDirectory = Path.GetDirectoryName(freshclam),
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ProcessTimeoutException)
            {
                return OperationResult.Fail(
                    BridgeErrorCodes.ClamAvTimeout,
                    "La actualización de definiciones superó el tiempo límite y fue terminada.");
            }

            var dto = new ClamAvMutationResult
            {
                Succeeded = result.ExitCode == 0,
                ExitCode = result.ExitCode,
                Elevated = true,
                Detail = ClamAvOutput.Detail(result),
            };
            return OperationResult.Ok(BridgeJson.ToJson(dto));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // El hijo elevado no tiene logger ni expone detalles internos.
            return OperationResult.Fail(BridgeErrorCodes.OperationFailed, "La operación elevada falló.");
        }
    }
}
