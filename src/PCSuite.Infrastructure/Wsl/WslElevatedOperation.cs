using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Processes;

namespace PCSuite.Infrastructure.Wsl;

/// <summary>
/// `wsl --update` que corre DENTRO del proceso elevado (Task 007 sobre el marco
/// del Task 005). Seguridad en profundidad:
/// - Su ID ya validó el padre contra ElevatedOperationIds.Allowed antes de
///   pedir UAC, y ElevatedIpcServer lo vuelve a comprobar al servir;
/// - el payload se IGNORA por completo (wsl.update es de payload cero) y los
///   argv son un arreglo fijo: aunque llegase manipulado no existe ruta a un
///   comando arbitrario (nunca un shell);
/// - devuelve resultado estructurado (WslMutationResult) con mensaje
///   controlado: jamás salidas crudas del sistema ni stack traces.
/// </summary>
public sealed class WslElevatedOperation : IElevatedOperation
{
    private static readonly TimeSpan MutationTimeout = TimeSpan.FromSeconds(900);

    private readonly ExecutableLocator _locator;
    private readonly IProcessRunner _processRunner;

    public WslElevatedOperation(
        ExecutableLocator? locator = null,
        IProcessRunner? processRunner = null)
    {
        _locator = locator ?? new ExecutableLocator();
        _processRunner = processRunner ?? new ProcessRunner();
    }

    public string Id => ElevatedOperationIds.WslUpdate;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return OperationResult.Fail(BridgeErrorCodes.NotSupported, "Solo disponible en Windows.");
        }

        try
        {
            var path = _locator.Find("wsl");
            if (path is null)
            {
                return OperationResult.Fail(BridgeErrorCodes.NotInstalled, "No se encontró wsl.exe en la PATH.");
            }

            ProcessResult result;
            try
            {
                result = await _processRunner.ExecuteAsync(
                    new ProcessRequest
                    {
                        FileName = path,
                        Arguments = WslArguments.Update,
                        Timeout = MutationTimeout,
                        OutputEncoding = WslEncoding.Utf16Le,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ProcessTimeoutException)
            {
                return OperationResult.Fail(
                    BridgeErrorCodes.WslTimeout,
                    "La operación wsl superó el tiempo límite y fue terminada.");
            }

            var dto = new WslMutationResult
            {
                Succeeded = result.ExitCode == 0,
                ExitCode = result.ExitCode,
                Elevated = true,
                Detail = WslOutput.Detail(result),
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
